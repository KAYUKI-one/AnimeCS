using System.Linq;
using AnimeCS.Content;
using AnimeCS.Editor;
using AnimeCS.Match;
using AnimeCS.Play;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AnimeCS.Tests
{
    public sealed class MovementAndRigTests
    {
        [Test] public void SkirtedOutfitsHaveTheirOwnHumanoidIdlesAndWorkingBodyCorrectives()
        {
            foreach (string id in new[]{"lily","flor"})
            {
                var outfit=ContentLibrary.Catalog.Find<OutfitDefinition>("outfit."+id);
                var stance=outfit.PoseController as AnimatorOverrideController;
                Assert.That(stance,Is.Not.Null,id);
                Assert.That(stance.runtimeAnimatorController,Is.EqualTo(outfit.Character.Controller));
                foreach(string name in new[]{"Idle","DuckIdle"})
                {
                    var clip=stance[name];
                    Assert.That(clip && clip.isHumanMotion && clip.isLooping,Is.True,id+" "+name);
                    Assert.That(AssetDatabase.GetAssetPath(clip),Does.EndWith("Idle.fbx"));
                }
                var instance=Object.Instantiate(outfit.Character.Model);
                try
                {
                    var rig=new CharacterRig(instance,outfit.Character,stance);
                    var renderer=instance.GetComponentInChildren<SkinnedMeshRenderer>();
                    var region=outfit.Regions(false).First(r=>r.source==renderer.sharedMesh);
                    int idle=renderer.sharedMesh.GetBlendShapeIndex("DrapeStand"),duck=renderer.sharedMesh.GetBlendShapeIndex("DrapeCrouch");
                    Assert.That(idle,Is.GreaterThanOrEqualTo(0));Assert.That(duck,Is.GreaterThanOrEqualTo(0));
                    Assert.That(region.partitioned.GetBlendShapeIndex("DrapeCrouch"),Is.EqualTo(duck));
                    renderer.sharedMesh=region.partitioned; rig.FitDrape(.65f);
                    Assert.That(renderer.GetBlendShapeWeight(idle),Is.EqualTo(35).Within(.01));
                    Assert.That(renderer.GetBlendShapeWeight(duck),Is.EqualTo(65).Within(.01));
                }
                finally {Object.DestroyImmediate(instance);}
            }
        }

        [TestCase(-1)]
        [TestCase(1)]
        public void CounterTapStopsWithinFiveTicksWithoutReverseDisplacement(int original)
        {
            const float speed = 6;
            var motion = new MotionContact { Grounded = true, Velocity = Vector2.right * (original * speed) };
            var reverse = new SeatIntent { Move = Vector2.left * original };
            bool stopped = false;
            for (int i = 0; i < 5; i++)
            {
                motion.Velocity = SeatMovement.Advance(motion, reverse, speed, ref stopped);
                Assert.That(motion.Velocity.x * original, Is.GreaterThanOrEqualTo(0));
                if (i == 3) Assert.That(Mathf.Abs(motion.Velocity.x), Is.GreaterThan(.01f));
            }
            Assert.That(motion.Velocity, Is.EqualTo(Vector2.zero));
            var held = motion;
            bool dwell = stopped;
            for (int i = 0; i < 10; i++)
                held.Velocity = SeatMovement.Advance(held, reverse, speed, ref dwell);
            Assert.That(held.Velocity.x * original, Is.EqualTo(-speed).Within(.001));
            float drift = 0;
            for (int i = 0; i < 15; i++)
            {
                motion.Velocity = SeatMovement.Advance(motion, default, speed, ref stopped);
                drift += motion.Velocity.x * SeatMovement.StepTime;
            }
            Assert.That(drift, Is.Zero);
        }

        [Test] public void NeutralReleaseAndSubtickTapHaveDistinctBraking()
        {
            const float runSpeed = 6f;
            const float steadyFraction = .34f;
            const float floatTolerance = .00001f;
            var tuning = new MovementTuning();
            int steadyTick = Mathf.CeilToInt(tuning.coastToSteadySeconds / SeatMovement.StepTime);
            bool stopped = false;
            var moving = new MotionContact { Grounded = true, Velocity = Vector2.right * runSpeed };
            var oneTap = new SeatIntent { BrakeTap = Vector2.left };
            var tapped = SeatMovement.Advance(moving, oneTap, runSpeed, ref stopped, tuning);
            var released = SeatMovement.Advance(moving, default, runSpeed, ref stopped, tuning);
            Assert.That(tapped.x, Is.LessThan(released.x));

            for (int tick = 1; tick <= steadyTick; tick++)
            {
                float previousSpeed = moving.Velocity.x;
                moving.Velocity = SeatMovement.Advance(moving, default, runSpeed, ref stopped, tuning);
                Assert.That(moving.Velocity.x, Is.LessThan(previousSpeed), $"Expected less speed at tick {tick}.");
                if (tick < steadyTick)
                    Assert.That(moving.Velocity.x / runSpeed, Is.GreaterThan(steadyFraction + floatTolerance));
            }

            Assert.That(moving.Velocity.x / runSpeed, Is.LessThanOrEqualTo(steadyFraction + floatTolerance));
            Assert.That(SeatMovement.Advance(MotionContact.Standing, oneTap, runSpeed, ref stopped, tuning),
                Is.EqualTo(Vector2.zero));
        }

        [Test] public void CrouchPeekLowersTheEyeAndSpeedTogetherWhileContinuingLaterally()
        {
            var m = MotionContact.Standing; var tuning=new MovementTuning();
            bool stopped=false;
            var run=new SeatIntent{Move=Vector2.right};var walk=new SeatIntent{Move=Vector2.right,Walk=true};
            for(int i=0;i<20;i++)m.Velocity=SeatMovement.Advance(m,run,6,ref stopped,tuning);
            Assert.That(m.Velocity.x,Is.EqualTo(6).Within(.001));
            for(int i=0;i<20;i++)m.Velocity=SeatMovement.Advance(m,walk,6,ref stopped,tuning);
            Assert.That(m.Velocity.x,Is.EqualTo(6*tuning.walkFraction).Within(.001));
            var peek=new SeatIntent{Move=Vector2.right,Crouch=true,Walk=true};
            float last=m.Velocity.x;
            for(int i=0;i<12;i++)
            {
                m.Velocity=SeatMovement.Advance(m,peek,6,ref stopped,tuning);
                m.Crouch=Mathf.MoveTowards(m.Crouch,1,SeatMovement.StepTime/tuning.crouchSeconds);
                Assert.That(m.Velocity.x,Is.GreaterThan(0).And.LessThanOrEqualTo(last+.0001f));last=m.Velocity.x;
            }
            Assert.That(last,Is.EqualTo(6*tuning.duckFraction).Within(.001));
            Assert.That(SeatMovement.EyeHeight(m.Crouch),Is.EqualTo(1.16f).Within(.001));
        }

        [Test] public void AirTurnsPreserveMomentumAndRespectProjectedAcceleration()
        {
            var m = new MotionContact { Grounded = false, Velocity = new Vector2(0,6) };
            bool stopped = false;
            Assert.That(SeatMovement.Advance(m, new SeatIntent { Yaw = 90 }, 6, ref stopped), Is.EqualTo(m.Velocity));
            var side = new SeatIntent { Move = Vector2.right };
            for (int i=0; i<14; i++) m.Velocity = SeatMovement.Advance(m,side,6,ref stopped);
            Assert.That(m.Velocity.x, Is.EqualTo(1.08f).Within(.001));
            Assert.That(m.Velocity.y, Is.EqualTo(6).Within(.001));
            m.Grounded = true;
            m.Velocity = new Vector2(2, -3);
            var diagonal = SeatMovement.Advance(m,new SeatIntent { Move = Vector2.up },6,ref stopped);
            Assert.That(diagonal.x, Is.GreaterThan(0)); Assert.That(diagonal.y, Is.LessThan(0));
        }

        [Test] public void ReloadRigPreservesOriginalSilhouetteAndMovesOnlyDuringAnimation()
        {
            foreach (var gun in ContentLibrary.Catalog.Entries.OfType<WeaponDefinition>().Where(g=>g.Class!=WeaponClass.Knife))
            {
                var original=ArenaWorld.ModelBounds(gun.WorldModel);
                var rig=gun.PosedModel(true);var bound=ArenaWorld.ModelBounds(rig);
                Assert.That(Vector3.Distance(original.size,bound.size),Is.LessThan(.002f),gun.Id);
                Assert.That(Vector3.Distance(original.center,bound.center),Is.LessThan(.002f),gun.Id);
                Assert.That(rig.GetComponentsInChildren<MeshRenderer>().Count(r=>r.name=="ReloadInsert"),Is.EqualTo(1),gun.Id);
                if(gun.Id=="weapon.ember47")
                {
                    var parts=rig.GetComponentsInChildren<MeshRenderer>();
                    float receiver=parts.Single(part=>part.name=="Body").bounds.min.y;
                    float magazine=parts.Single(part=>part.name=="ReloadInsert").bounds.min.y;
                    Assert.That(receiver-magazine,Is.GreaterThan(.04f),"The inner magazine shell must leave with its ribbed cover.");
                }
                var seat=new SeatView{Match=new PublicMatchView{Tick=60},EquipTick=-1000,InspectTick=-1000,ReloadStart=10,ReloadEnd=110};
                Assert.That(WeaponActionPose.Sample(seat,gun,0,true,1).InsertOffset.magnitude,Is.GreaterThan(.1f),gun.Id);
                seat.Match.Tick=110;Assert.That(WeaponActionPose.Sample(seat,gun,0,true,1).InsertOffset,Is.EqualTo(Vector3.zero));
            }
        }

        [Test] public void EachDisplayMeshUsesItsOwnContactsOnBothArmRigs()
        {
            var catalog=ContentLibrary.Catalog;
            var weapons=catalog.Entries.OfType<WeaponDefinition>().ToArray();
            Assert.That(weapons.Select(w=>w.WorldModel).Distinct().Count(),Is.EqualTo(weapons.Length));
            foreach (var outfit in catalog.SeatOutfits)
            foreach (bool view in new[]{false,true})
            {
                var root=new GameObject("Pose test");
                try
                {
                    var model=Object.Instantiate(view ? outfit.ViewRig : outfit.Character.Model,root.transform,false);
                    var animator=model.GetComponent<Animator>();
                    Assert.That(animator.avatar.isValid && animator.isHuman,Is.True);
                    var hands=new WeaponGripRig(animator);
                    Assert.That(hands.Valid,Is.True,outfit.name);
                    var mount=new GameObject("Weapon").transform; mount.SetParent(root.transform,false);
                    var idle=outfit.Character.Controller.animationClips.First(c=>c.name=="Idle");
                    var graph=PlayableGraph.Create();
                    try
                    {
                        var output=AnimationPlayableOutput.Create(graph,"Pose",animator);
                        output.SetSourcePlayable(AnimationClipPlayable.Create(graph,idle)); graph.Play();
                        foreach (var gun in weapons)
                        foreach (float pitch in new[]{0f,34f,-28f})
                        foreach (int actionIndex in new[]{0,1,2,3})
                        {
                            graph.Evaluate(.015f);
                            var aim=Quaternion.Euler(pitch,15,-3);
                            var seat=new SeatView{Match=new PublicMatchView{Tick=50}, EquipTick=actionIndex==3?45:-500,
                                InspectTick=actionIndex==1?9:-500,ReloadStart=actionIndex==2?20:0,ReloadEnd=actionIndex==2?84:0};
                            var action=WeaponActionPose.Sample(seat,gun,.4f,view,1f);
                            hands.Pose(root.transform,mount,gun,aim,view,0,action);
                            foreach(bool left in new[]{false,true})
                            {
                                var bone=animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
                                if(left && gun.Class==WeaponClass.Knife)
                                {
                                    Assert.That(Vector3.Distance(bone.position,mount.TransformPoint(gun.Grip)),Is.GreaterThan(.19f),outfit.name+" knife offhand");
                                    continue;
                                }
                                if(left && action.Release>.8f) // The support hand deliberately leaves its idle grip during a reload.
                                {
                                    Assert.That(Vector3.Distance(bone.position,mount.TransformPoint(gun.Support)),Is.GreaterThan(.05f));
                                    continue;
                                }
                                float error=Vector3.Distance(bone.position,mount.TransformPoint(left ? gun.Support : gun.Grip));
                                Assert.That(error,Is.LessThan(.012f),$"{outfit.name}, {gun.name}, view={view}, left={left}, pitch={pitch}");
                            }
                        }
                    }
                    finally { graph.Destroy(); }
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [Test] public void AllPlayableAvatarsRetargetCrouchWithoutMovingThePlantedFoot()
        {
            var catalog = ContentLibrary.Catalog;
            Assert.That(catalog.SeatOutfits.Count,Is.EqualTo(6));
            var clips = catalog.Character.Controller.animationClips.Distinct().ToDictionary(c => c.name);
            Assert.That(clips.Count,Is.EqualTo(19));
            foreach (var outfit in catalog.SeatOutfits)
            {
                Assert.That(outfit.Arms.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount,Is.GreaterThan(1000));
                var body = Object.Instantiate(outfit.Character.Model);
                var baked = new Mesh();
                var graph = PlayableGraph.Create();
                try
                {
                    var animator = body.GetComponent<Animator>();
                    Assert.That(animator.avatar.isValid && animator.avatar.isHuman,Is.True);
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.applyRootMotion = false;
                    var skin = body.GetComponentInChildren<SkinnedMeshRenderer>();
                    Assert.That(skin.bones.Length,Is.GreaterThanOrEqualTo(22));
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var output = AnimationPlayableOutput.Create(graph,"Pose",animator);
                    void Sample(string name)
                    {
                        var playable = AnimationClipPlayable.Create(graph,clips[name]);
                        output.SetSourcePlayable(playable); playable.SetTime(.15);
                        graph.Play(); graph.Evaluate(0);
                        skin.BakeMesh(baked);
                    }
                    Sample("Idle");
                    Vector3 hip = animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    Vector3 foot = animator.GetBoneTransform(HumanBodyBones.RightFoot).position;
                    var standing = baked.vertices;
                    Sample("DuckIdle");
                    Assert.That(hip.y-animator.GetBoneTransform(HumanBodyBones.Hips).position.y, Is.InRange(.32f,.57f),outfit.DisplayName);
                    Assert.That(Vector3.Distance(foot,animator.GetBoneTransform(HumanBodyBones.RightFoot).position),Is.LessThan(.06f));
                    var duck = baked.vertices;
                    int moved = 0;
                    for(int i=0;i<duck.Length;i+=8) if ((standing[i]-duck[i]).sqrMagnitude > .008f) moved++;
                    Assert.That(moved,Is.GreaterThan(duck.Length/32),outfit.DisplayName);
                }
                finally { graph.Destroy(); Object.DestroyImmediate(baked); Object.DestroyImmediate(body); }
            }
        }
    }
}
