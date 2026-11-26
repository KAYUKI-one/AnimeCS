using System.Linq;
using AnimeCS.Content;
using AnimeCS.Editor;
using AnimeCS.Match;
using AnimeCS.Play;
using AnimeCS.Profile;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace AnimeCS.Tests
{
    public sealed class DemoPresentationTests
    {
        [Test] public void EveryDustSpawnHasSolidFloorAndCanReachBothSites()
        {
            var map=ContentLibrary.Catalog.Find<MapDefinition>("map.dust2");
            Assert.That(map && map.Navigation && map.Radar,Is.True);
            var offset=Vector3.right*10000;GameObject geometry=null;
            var nav=NavMesh.AddNavMeshData(map.Navigation);
            try
            {
                geometry=Object.Instantiate(map.Geometry.transform.Find("Map collision").gameObject);
                geometry.transform.position+=offset;Physics.SyncTransforms();var physics=geometry.scene.GetPhysicsScene();
                foreach(var side in new[]{Side.Attack,Side.Defend})
                for(int i=0;i<5;i++)
                {
                    var p=map.Spawn(side,i);
                    Assert.That(physics.Raycast(offset+p+Vector3.up*.5f,Vector3.down,out var floor,1.2f,~0,QueryTriggerInteraction.Ignore),Is.True,side+" "+i);
                    Assert.That(Mathf.Abs(floor.point.y-p.y),Is.LessThan(.25f));
                    foreach(var site in new[]{Site.A,Site.B})
                    {
                        var path=new NavMeshPath();
                        Assert.That(NavMesh.CalculatePath(p,map.SitePosition(site),-1,path),Is.True);
                        Assert.That(path.status,Is.EqualTo(NavMeshPathStatus.PathComplete),side+" "+i+" "+site);
                    }
                }
                var radar=map.RadarPoint(map.SitePosition(Site.A));
                Assert.That(radar.x,Is.EqualTo(.80).Within(.025));Assert.That(radar.y,Is.EqualTo(.16).Within(.025));
            }
            finally{nav.Remove();if(geometry)Object.DestroyImmediate(geometry);}
        }

        [Test] public void WardrobeRepairsOneSlotAndKeepsEachPlayableCharactersFittedGloves()
        {
            var c=ContentLibrary.Catalog;var p=WardrobeProfile.Create(c);
            p.current.worn=new WornItems{headwear="wear.catband",gloves="wear.irisgloves",shoes="wear.fieldjacket",bottom="wear.fieldtrousers"};
            var resolved=p.Resolve(p.current,c);
            Assert.That(resolved.worn.shoes,Is.Null);Assert.That(resolved.worn.gloves,Is.EqualTo("wear.irisgloves"));
            Assert.That(resolved.worn.headwear,Is.EqualTo("wear.catband"));Assert.That(resolved.worn.bottom,Is.EqualTo("wear.fieldtrousers"));
            var glove=c.Find<WearableDefinition>(resolved.worn.gloves);
            foreach(var fit in c.SeatOutfits)
            {
                Assert.That(p.owned,Does.Contain(fit.Character.Id));
                Assert.That(glove.Model(true,fit.Character),Is.Not.Null);
                var src=Object.Instantiate(fit.Character.Model);var gear=Object.Instantiate(glove.Model(false,fit.Character));
                try{var rig=new CharacterRig(src,fit.Character);Assert.That(rig.BindClothing(gear),Is.True,fit.DisplayName);}
                finally{Object.DestroyImmediate(src);Object.DestroyImmediate(gear);}
            }
        }

        [Test] public void PlayableCharactersHaveNoFactionFaceAttachment()
        {
            foreach(var fit in ContentLibrary.Catalog.SeatOutfits)
            {
                var data=new UnityEditor.SerializedObject(fit.Character);
                Assert.That(data.FindProperty("_mask"),Is.Null);
                Assert.That(fit.Character.Model,Is.Not.Null);
            }
            Assert.That(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AnimeCS/Content/Models/AttackMask.prefab"),Is.Null);
        }

        [Test] public void BothInsigniaFollowEachCharactersOwnSkin()
        {
            foreach (var outfit in ContentLibrary.Catalog.SeatOutfits)
            {
                var body = Object.Instantiate(outfit.Character.Model);
                try
                {
                    var rig = new CharacterRig(body, outfit.Character);
                    foreach (var side in new[] { Side.Attack, Side.Defend })
                    {
                        var patch = Object.Instantiate(outfit.Character.Insignia(side), body.transform, false);
                        var mesh = patch.GetComponentInChildren<SkinnedMeshRenderer>();
                        Assert.That(mesh, Is.Not.Null, outfit.name);
                        Assert.That(rig.BindClothing(patch), Is.True, outfit.name);
                        Assert.That(mesh.bones.All(bone => bone && bone.IsChildOf(body.transform) && !bone.IsChildOf(patch.transform)), Is.True, outfit.name);
                        Assert.That(mesh.sharedMaterial.GetTexture("_BaseMap"), Is.Not.Null);
                    }
                }
                finally { Object.DestroyImmediate(body); }
            }
        }

        [Test] public void ThirdPersonAimFollowsTheEyesAndKeepsTheWeaponOutsideTheCoreOfTheBody()
        {
            var c=ContentLibrary.Catalog;
            foreach(var fit in c.SeatOutfits)
            {
                var stage=new GameObject("Turn preview");var model=Object.Instantiate(fit.Character.Model,stage.transform,false);
                var rig=new CharacterRig(model,fit.Character);var anim=model.GetComponent<Animator>();
                var mount=new GameObject("Mount").transform;mount.SetParent(stage.transform,false);
                var graph=PlayableGraph.Create();graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                try
                {
                    var output=AnimationPlayableOutput.Create(graph,"Preview",anim);
                    foreach(var motion in new[]{"Idle","DuckIdle"})
                    {
                        var clip=fit.Character.Controller.animationClips.First(a=>a.name==motion);
                        var playable=AnimationClipPlayable.Create(graph,clip);output.SetSourcePlayable(playable);graph.Play();
                        foreach(var gun in c.Entries.OfType<WeaponDefinition>())
                        foreach(var look in new[]{new Vector2(0,0),new Vector2(32,50),new Vector2(-38,-46)})
                        foreach(int action in new[]{0,1,2,3})
                        {
                            rig.RestoreHitPose();playable.SetTime(.15);graph.Evaluate(0);
                            var before=rig.HeadMount.forward;rig.Look(stage.transform,look.x,look.y);
                            if(look.y!=0)Assert.That((rig.HeadMount.forward.y-before.y)*Mathf.Sign(look.y),Is.GreaterThan(.28f),fit.name+" pitch");
                            var aim=Quaternion.Euler(-look.y,look.x,0);
                            var view=new SeatView{Match=new PublicMatchView{Tick=60},EquipTick=action==1?55:-500,
                                InspectTick=action==2?11:-500,ReloadStart=action==3?34:0,ReloadEnd=action==3?92:0};
                            var handling=WeaponActionPose.Sample(view,gun,.35f,false,1);
                            rig.Grip.Pose(stage.transform,mount,gun,aim,false,0,handling);
                            Vector3 start=mount.TransformPoint(gun.Grip);
                            Vector3 front=gun.Class==WeaponClass.Knife ? mount.TransformPoint(new Vector3(0,0,.26f)):
                                mount.TransformPoint(new Vector3(0,Mathf.Max(.02f,gun.Support.y),Mathf.Max(.18f,gun.Support.z+.09f)));
                            var chest=rig.Chest.position;
                            var face=rig.Head.position+rig.HeadMount.up*.055f+rig.HeadMount.forward*.035f;
                            for(int j=0;j<=6;j++)
                            {
                                var p=Vector3.Lerp(start,front,j/6f);
                                Assert.That(Vector3.Distance(p,chest),Is.GreaterThan(.105f),fit.name+" "+motion+" "+gun.name+" chest "+look);
                                Assert.That(Vector3.Distance(p,face),Is.GreaterThan(.077f),fit.name+" "+motion+" "+gun.name+" face "+look);
                            }
                        }
                    }
                }
                finally{graph.Destroy();Object.DestroyImmediate(stage);}
            }
        }
    }
}
