using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AnimeCS.Content;
using AnimeCS.Match;
using AnimeCS.Play;
using NUnit.Framework;
using StylizedCharacters;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AnimeCS.Tests
{
    public sealed class PracticeAndOpticsTests
    {
        private const string AttackOpening = "余烬 · 先攻";
        private const string DefendOpening = "苍盾 · 先守";

        [UnityTest] public IEnumerator RangeOpensFromLobbyAndChangesItsTargetsAndEquipment()
        {
            using var services=new LobbyTestServices();
            yield return services.Ready();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Gameplay/LocalMatch.unity");
            var game=Object.FindAnyObjectByType<LocalGame>();
            while(!game.InLobby) yield return null;
            string bombScenePath = game.SelectedMap.PlayScenePath;
            Assert.That(SceneManager.GetSceneByPath(bombScenePath).isLoaded,Is.False);
            var root=Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            yield return services.Register(root);
            void Click(string name)
            {
                var button=root.Q<Button>(name); Assert.That(button,Is.Not.Null,name);
                using var click=NavigationSubmitEvent.GetPooled();click.target=button;button.SendEvent(click);
            }
            Click("mode"); Click("mode-practice"); yield return null;
            Assert.That(game.SelectedMap.Practice,Is.True);
            var openingChoices = root.Q("sheet-content").Query<Button>().ToList().Select(button => button.text);
            Assert.That(openingChoices, Does.Not.Contain(AttackOpening));
            Assert.That(openingChoices, Does.Not.Contain(DefendOpening));
            Click("close");Click("start");for(int i=0;i<150 && !game.Ready;i++)yield return null;
            Assert.That(game.MatchState.Phase,Is.EqualTo(RoundPhase.Practice));yield return null;
            string practiceScenePath = game.SelectedMap.PlayScenePath;
            Assert.That(game.SelectedMap.Id, Is.EqualTo("map.practice"));
            Assert.That(practiceScenePath, Is.Not.EqualTo(bombScenePath));
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(practiceScenePath));
            Assert.That(SceneManager.GetSceneByPath(bombScenePath).isLoaded, Is.False);
            var practiceProbes = LightmapSettings.lightProbes;
            Assert.That(practiceProbes, Is.Not.Null);
            Assert.That(practiceProbes.count, Is.GreaterThan(1));
            var sun=RenderSettings.sun;
            Assert.That(sun.gameObject.scene.path,Is.EqualTo(practiceScenePath));
            Assert.That(sun.bakingOutput.mixedLightingMode,Is.EqualTo(MixedLightingMode.Shadowmask));
            Assert.That(sun.bakingOutput.occlusionMaskChannel,Is.InRange(0,3));
            var sideWall=SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(o=>o.GetComponentsInChildren<MeshRenderer>()).First(r=>r.name=="侧墙");
            Assert.That(LightmapSettings.lightmaps[sideWall.lightmapIndex].shadowMask,Is.Not.Null);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var match=(LocalMatch)typeof(LocalGame).GetField("_match",flags).GetValue(game);
            var catalog=(ContentCatalog)typeof(LocalGame).GetField("_catalog",flags).GetValue(game);
            var actors=(ArenaPawn[])typeof(LocalGame).GetField("_actors",flags).GetValue(game);
            Assert.That(actors.Count(a=>a.Alive),Is.EqualTo(6));
            Click("open-shop");yield return null;yield return null;
            var shop=root.Q("shop");
            Assert.That(shop.Query<Button>().ToList().Count(b=>b.name?.StartsWith("weapon.")==true),
                Is.EqualTo(catalog.Entries.OfType<WeaponDefinition>().Count(w=>!w.Archived)));
            foreach (var card in shop.Query<Button>(className:"shop-weapon").ToList())
                Assert.That(card.Q<Image>()?.image,Is.Not.Null,card.name);
            Assert.That(shop.Query<Button>(className:"shop-weapon").ToList().Count,Is.EqualTo(13));
            Click("Helmet");yield return Ticks(2);
            Assert.That(game.Player.Read().Helmet && !game.Player.Read().Armor,Is.True);
            Click("Armor");yield return Ticks(2);
            Assert.That(game.Player.Read().Armor,Is.True);
            Click("weapon.fold44");yield return Ticks(4);
            Assert.That(game.Player.Read().WeaponId,Is.EqualTo("weapon.fold44"));
            float startX = actors[5].transform.position.x;
            Click("target-motion");
            Assert.IsTrue(match.MovingTargets);
            // A sinusoidal target can return to its starting x at a particular sample tick.
            float excursion = 0;
            int until = match.Public.Tick + MatchRules.TickRate * 3;
            while (match.Public.Tick < until && excursion <= .18f)
            {
                yield return null;
                excursion = Mathf.Max(excursion, Mathf.Abs(actors[5].transform.position.x - startX));
            }
            Assert.That(excursion, Is.GreaterThan(.18f));
            Assert.That(shop.Q<Button>("target-motion").text, Is.EqualTo("靶位移动　开"));
            Click("target-motion"); yield return null;
            Assert.That(shop.Q<Button>("target-motion").text, Is.EqualTo("靶位移动　关"));
            Click("close-shop");
            game.ReturnToLobby();Click("mode");Click("mode-bots");
            yield return null; Assert.That(game.SelectedMap.Practice,Is.False);
            var loadDeadline = Time.realtimeSinceStartupAsDouble + 30;
            while (!SceneManager.GetSceneByPath(bombScenePath).isLoaded && Time.realtimeSinceStartupAsDouble < loadDeadline)
                yield return null;
            Assert.That(SceneManager.GetSceneByPath(bombScenePath).isLoaded, Is.True);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(game.gameObject.scene));
            Assert.That(SceneManager.GetSceneByPath(practiceScenePath).isLoaded, Is.False);
            openingChoices = root.Q("sheet-content").Query<Button>().ToList().Select(button => button.text);
            Assert.That(openingChoices, Does.Contain(AttackOpening));
            Assert.That(openingChoices, Does.Contain(DefendOpening));
            Click("close"); Click("start");
            yield return new WaitUntil(()=>game.Ready);
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(bombScenePath));
            var bombProbes = LightmapSettings.lightProbes;
            Assert.That(bombProbes, Is.Not.Null);
            Assert.That(bombProbes.count, Is.GreaterThan(1));
            IEnumerator Ticks(int n){int end=match.Public.Tick+n;while(match.Public.Tick<end)yield return null;}
        }

        [UnityTest] public IEnumerator BothFactionsShareTheEyePlaneAndFlashFollowsTheWorldWeapon()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Gameplay/LocalMatch.unity");
            var game=Object.FindAnyObjectByType<LocalGame>();while(!game.InLobby)yield return null;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var catalog=(ContentCatalog)typeof(LocalGame).GetField("_catalog",flags).GetValue(game);
            game.OpeningSide=FirstSide.Attack; game.Restart();
            for(int n=0;n<160 && !game.Ready;n++)yield return null;
            var match=(LocalMatch)typeof(LocalGame).GetField("_match",flags).GetValue(game);
            var actors=(ArenaPawn[])typeof(LocalGame).GetField("_actors",flags).GetValue(game);
            var viewers=(AppearancePresenter[])typeof(LocalGame).GetField("_avatars",flags).GetValue(game);
            while(match.Public.Phase==RoundPhase.Buy)match.Step();
            var drivers=new ObserverPoseTests.PoseDriver[LocalMatch.Seats];
            for(int i=0;i<drivers.Length;i++){drivers[i]=new ObserverPoseTests.PoseDriver();game.SetSeatDriver(i,drivers[i]);}
            var main=Camera.main;int hidden=CharacterCamera.HiddenLayerMask;
            Assert.That(hidden, Is.Not.Zero);
            Assert.That(main.cullingMask & hidden, Is.Zero);
            Assert.That(main.nearClipPlane, Is.EqualTo(.05f).Within(.0001f));
            Assert.That(main.GetComponentsInChildren<Camera>(true).Length, Is.EqualTo(1));
            drivers[0].crouch = false;
            drivers[0].pitch = 0;
            int lookAhead = match.Public.Tick + 28;
            while (match.Public.Tick < lookAhead) yield return null;
            yield return LobbyTestServices.Capture("FirstPerson");
            drivers[0].pitch = -80;
            int lookDown = match.Public.Tick + 28;
            while (match.Public.Tick < lookDown) yield return null;
            yield return LobbyTestServices.Capture("FirstPersonLookDown");
            var original = actors[0].Visuals.GetComponentInChildren<Animator>();
            var held = actors[0].Visuals.GetComponentsInChildren<Transform>().Single(t => t.name == "Weapon");
            var visible = original.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s => s.name == "First-person body");
            var shadow = original.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s => s.name == "Full character shadow");
            Assert.That(visible.enabled, Is.True);
            Assert.That(shadow.enabled, Is.True);
            game.SetThirdPerson(true);
            yield return null; yield return new WaitForEndOfFrame();
            Assert.That(game.ThirdPerson, Is.True);
            Assert.That(actors[0].Visuals.GetComponentInChildren<Animator>(), Is.SameAs(original));
            Assert.That(held.gameObject.activeInHierarchy, Is.True);
            Assert.That(visible.enabled || shadow.enabled, Is.False);
            Assert.That(Vector3.Distance(main.transform.position, actors[0].Visuals.position), Is.GreaterThan(.6f));
            drivers[0].pitch = 0;
            var orbit = (CharacterCamera)typeof(LocalGame).GetField("_characterCamera",flags).GetValue(game);
            orbit.Orbit(new Vector2(139f, 0), 1f);
            yield return LobbyTestServices.Capture("ThirdPersonOrbit");
            game.SetThirdPerson(false);
            foreach(bool crouch in new[]{false,true})
            {
                var attackIds = new HashSet<string>();
                var defendIds = new HashSet<string>();
                for(int i=0;i<drivers.Length;i++){drivers[i].crouch=crouch;drivers[i].yaw=i*23;drivers[i].pitch=crouch?18:-12;}
                int end=match.Public.Tick+40;while(match.Public.Tick<end)yield return null;
                yield return new WaitForEndOfFrame();
                float expected=crouch?1.16f:1.62f;
                Assert.That(main.transform.position.y-actors[0].Visuals.position.y,Is.EqualTo(expected).Within(.004));
                var facing = Quaternion.Euler(0, drivers[0].yaw, 0) * Vector3.forward;
                Assert.That(Vector3.Dot(main.transform.position - actors[0].Visuals.position, facing), Is.EqualTo(.08f).Within(.006f));
                // Seat zero uses the first-person camera. The other four attack seats
                // and five defend seats cover all eight third-person rigs.
                for(int i=1;i<LocalMatch.Seats;i++)
                {
                    var view = match.BindSeat(i).Read();
                    var rig=actors[i].Visuals.GetComponentInChildren<Animator>();
                    var character=catalog.Find<OutfitDefinition>(view.OutfitId).Character;
                    Assert.That(rig, Is.Not.Null, character.Id);
                    Assert.That(character.Faction, Is.EqualTo(view.Side), $"Seat {i}");
                    (view.Side == Side.Attack ? attackIds : defendIds).Add(character.Id);
                    var face=rig.GetBoneTransform(HumanBodyBones.Head).TransformPoint(character.FaceAnchor);
                    Assert.That(face.y-actors[i].Visuals.position.y,Is.EqualTo(expected).Within(.008),character.Id);
                    var renderer=rig.GetComponentInChildren<SkinnedMeshRenderer>();
                    var baked=new Mesh();
                    try
                    {
                        renderer.BakeMesh(baked);
                        var vertices=baked.vertices;var weights=renderer.sharedMesh.boneWeights;
                        var feet=renderer.bones.Select(b=>b.name.EndsWith("Foot") || b.name.EndsWith("Toes")).ToArray();
                        var toWorld = renderer.transform.localToWorldMatrix;
                        var sole = new Vector3(0, float.PositiveInfinity, 0);
                        for(int v=0;v<vertices.Length;v++)
                        {
                            if (!feet[weights[v].boneIndex0]) continue;
                            var vertex = toWorld.MultiplyPoint3x4(vertices[v]);
                            if (vertex.y < sole.y) sole = vertex;
                        }
                        if (view.Motion.Grounded)
                        {
                            Assert.That(float.IsPositiveInfinity(sole.y), Is.False, character.Id + " has a weighted sole");
                            float floor = actors[i].Visuals.position.y - actors[i].GroundSkin;
                            if (Physics.Raycast(sole + Vector3.up * .28f, Vector3.down, out var contact, .56f,
                                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && contact.normal.y < .9999f)
                                floor = contact.point.y;
                            Assert.That(sole.y, Is.EqualTo(floor).Within(.045f),
                                $"{character.Id} sole / crouch={crouch}, contact={contact.collider?.name}, normal={contact.normal:F4}, capsule={actors[i].Visuals.position.y:F5}");
                        }
                    }
                    finally {Object.Destroy(baked);}
                    var hit=actors[i].HeadHitbox;
                    Assert.That(hit.transform.TransformPoint(hit.center).y-actors[i].transform.position.y,Is.EqualTo(expected+.006).Within(.001));
                    Assert.That(hit.radius * hit.transform.lossyScale.x * 2f, Is.EqualTo(.29f).Within(.001f), character.Id);
                }
                Assert.That(attackIds, Is.EquivalentTo(new[] { "character.flor", "character.startraveler", "character.firefly", "character.nora" }));
                Assert.That(defendIds, Is.EquivalentTo(new[] { "character.mornye", "character.rover", "character.lily", "character.michele" }));
            }
            // Attack pistol has a visible flash. Shoot away from other actors so the driver remains alive.
            yield return null; yield return null;
            drivers[0].pitch=72;drivers[1].pitch=72;
            bool flashed=false, observed=false;
            int start=match.Public.Tick;
            while(match.Public.Tick-start<90 && !(flashed&&observed))
            {
                drivers[0].fire=drivers[1].fire=(match.Public.Tick-start)%18<7;
                yield return new WaitForEndOfFrame();
                var flash=actors[0].Visuals.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="枪口火光");
                var worldFlash=actors[1].Visuals.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="枪口火光");
                observed |= worldFlash && Vector3.Distance(worldFlash.position,viewers[1].Muzzle().GetValueOrDefault())<.002f;
                flashed |= flash && flash.gameObject.layer!=LayerMask.NameToLayer(CharacterCamera.HiddenLayer) &&
                    Vector3.Distance(flash.position,viewers[0].Muzzle().GetValueOrDefault())<.002f;
            }
            Assert.That(flashed,Is.True,"First-person flash is anchored at the shared world weapon");
            Assert.That(observed,Is.True,"Third-person fire is anchored at the world gun");
            var remoteGun=catalog.Find<WeaponDefinition>(match.BindSeat(1).Read().WeaponId);
            var remote=actors[1].Visuals.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="枪口火光");
            Assert.That((remote.localPosition-remoteGun.Muzzle(false)).sqrMagnitude,Is.LessThan(.000001));
            Assert.That(remote.gameObject.layer,Is.Not.EqualTo(LayerMask.NameToLayer(CharacterCamera.HiddenLayer)));
            game.ReturnToLobby();
        }
    }
}

