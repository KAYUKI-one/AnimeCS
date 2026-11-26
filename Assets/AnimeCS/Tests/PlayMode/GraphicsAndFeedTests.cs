using System.Collections;
using System.Linq;
using AnimeCS.Play;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AnimeCS.Tests
{
    public sealed class GraphicsAndFeedTests
    {
        private const string Scene = "Assets/AnimeCS/Scenes/LocalMatch.unity";
        private static void Click(VisualElement root, string name)
        {
            var button = root.Q<Button>(name);
            Assert.That(button, Is.Not.Null, name);
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        [UnityTest] public IEnumerator SettingsApplyToTheRenderPipelineAndSurviveSceneReload()
        {
            yield return SceneManager.LoadSceneAsync(Scene);
            var game = Object.FindAnyObjectByType<LocalGame>();
            while (!game.InLobby) yield return null;
            var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            var keys = new[]{"AnimeCS.Video.Scale", "AnimeCS.Video.MSAA", "AnimeCS.Video.Shadows"};
            var saved = keys.Select(k => PlayerPrefs.HasKey(k) ? (int?) PlayerPrefs.GetInt(k) : null).ToArray();
            var previous = game.GraphicsOptions.Current;
            try
            {
                Click(root,"settings"); Click(root,"quality-ultra");
                Assert.That(root.Q<SliderInt>("render-scale").value, Is.EqualTo(150));
                Click(root,"quality-high"); Click(root,"apply-graphics");
                var active = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
                Assert.That(active.renderScale, Is.EqualTo(1.25f).Within(.0001));
                Assert.That(active.msaaSampleCount, Is.EqualTo(Mathf.Min(4,game.GraphicsOptions.MaxSamples)));
                Assert.That(active.mainLightShadowmapResolution, Is.EqualTo(Mathf.Min(4096,game.GraphicsOptions.MaxShadow)));
                Assert.That(root.Q<Label>("render-dimensions").text, Does.Contain("已应用"));
                yield return SceneManager.LoadSceneAsync(Scene);
                game = Object.FindAnyObjectByType<LocalGame>();
                while (!game.InLobby) yield return null;
                Assert.That(game.GraphicsOptions.Current.Scale, Is.EqualTo(125));
                Assert.That(((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).renderScale,Is.EqualTo(1.25f).Within(.0001));
            }
            finally
            {
                for (int i=0; i<keys.Length; i++)
                    if (saved[i].HasValue) PlayerPrefs.SetInt(keys[i], saved[i].Value); else PlayerPrefs.DeleteKey(keys[i]);
                PlayerPrefs.Save();
                if (game && game.GraphicsOptions!=null) game.GraphicsOptions.Apply(previous,false);
            }
        }

        [UnityTest] public IEnumerator AspectModesKeepTheAimCenteredAndSurviveSceneReload()
        {
            const string aspectKey = "AnimeCS.Video.Aspect";
            yield return SceneManager.LoadSceneAsync(Scene);
            var game = Object.FindAnyObjectByType<LocalGame>();
            while (!game.InLobby) yield return null;
            PlayAspect previous = game.AspectMode;
            int? saved = PlayerPrefs.HasKey(aspectKey) ? PlayerPrefs.GetInt(aspectKey) : null;
            try
            {
                var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
                Click(root, "settings");
                var aspect = root.Q<DropdownField>("video-aspect");
                Assert.That(aspect, Is.Not.Null);
                Assert.That(aspect.choices.Count, Is.EqualTo(3));
                foreach (var mode in new[] { PlayAspect.Native, PlayAspect.Widescreen, PlayAspect.Stretched })
                {
                    aspect = root.Q<DropdownField>("video-aspect");
                    aspect.index = (int)mode;
                    Assert.That(game.AspectMode, Is.EqualTo(mode));
                }
                Click(root, "close");
                game.Restart();
                while (!game.Ready) yield return null;
                var world = Camera.main;
                var hands = world.GetComponentsInChildren<Camera>(true).Single(camera => camera != world);
                var center = new Vector3(.5f, .5f, 0f);
                void AssertCenteredAim(Camera camera)
                {
                    var projection = camera.projectionMatrix;
                    var localCenter = projection.inverse * new Vector4(0f, 0f, -1f, 1f);
                    var localRay = new Vector3(localCenter.x, localCenter.y, localCenter.z) / localCenter.w;
                    Assert.That(Vector3.Distance(localRay.normalized, Vector3.back), Is.LessThan(.000001f), camera.name);

                    // A world-space ray can lose precision when the near plane is small
                    // relative to the camera's distance from the world origin.
                    var aim = camera.transform.InverseTransformDirection(camera.ViewportPointToRay(center).direction);
                    var clip = projection * new Vector4(aim.x, aim.y, -aim.z, 0f);
                    var pixelOffset = new Vector2(clip.x / clip.w * camera.pixelWidth, clip.y / clip.w * camera.pixelHeight) * .5f;
                    Assert.That(pixelOffset.magnitude, Is.LessThan(.25f), camera.name + " center ray offset in output pixels");
                }

                game.AspectMode = PlayAspect.Native;
                yield return new WaitForEndOfFrame();
                var nativeWorld = world.projectionMatrix;
                var nativeHands = hands.projectionMatrix;
                var nativeRect = world.rect;
                Assert.That(nativeWorld.m11 / nativeWorld.m00,
                    Is.EqualTo((float)world.pixelWidth / world.pixelHeight).Within(.002f));
                Assert.That(nativeHands.m11 / nativeHands.m00,
                    Is.EqualTo((float)hands.pixelWidth / hands.pixelHeight).Within(.002f));
                AssertCenteredAim(world);
                AssertCenteredAim(hands);

                game.AspectMode = PlayAspect.Widescreen;
                yield return new WaitForEndOfFrame();
                var wideWorld = world.projectionMatrix;
                var wideHands = hands.projectionMatrix;
                Assert.That(wideWorld.m11 / wideWorld.m00, Is.EqualTo(16f / 9f).Within(.001f));
                Assert.That(wideHands.m11 / wideHands.m00, Is.EqualTo(16f / 9f).Within(.001f));
                AssertCenteredAim(world);
                AssertCenteredAim(hands);

                game.AspectMode = PlayAspect.Stretched;
                yield return new WaitForEndOfFrame();
                Assert.That(world.projectionMatrix.m00 / wideWorld.m00, Is.EqualTo(4f / 3f).Within(.001f));
                Assert.That(hands.projectionMatrix.m00 / wideHands.m00, Is.EqualTo(4f / 3f).Within(.001f));
                Assert.That(world.projectionMatrix.m11, Is.EqualTo(wideWorld.m11).Within(.001f));
                Assert.That(hands.projectionMatrix.m11, Is.EqualTo(wideHands.m11).Within(.001f));
                AssertCenteredAim(world);
                AssertCenteredAim(hands);

                game.AspectMode = PlayAspect.Native;
                yield return new WaitForEndOfFrame();
                Assert.That(world.rect, Is.EqualTo(nativeRect));
                Assert.That(world.projectionMatrix.m00, Is.EqualTo(nativeWorld.m00).Within(.001f));
                Assert.That(hands.projectionMatrix.m00, Is.EqualTo(nativeHands.m00).Within(.001f));

                game.AspectMode = PlayAspect.Stretched;
                yield return SceneManager.LoadSceneAsync(Scene);
                game = Object.FindAnyObjectByType<LocalGame>();
                while (!game.InLobby) yield return null;
                Assert.That(game.AspectMode, Is.EqualTo(PlayAspect.Stretched));
            }
            finally
            {
                if (game)
                {
                    game.AspectMode = previous;
                    if (game.Ready) game.ReturnToLobby();
                }
                if (saved.HasValue) PlayerPrefs.SetInt(aspectKey, saved.Value);
                else PlayerPrefs.DeleteKey(aspectKey);
                PlayerPrefs.Save();
            }
        }

        [UnityTest] public IEnumerator RightMouseCyclesBothSniperLensesAndLeavesShopLookIndependent()
        {
            yield return SceneManager.LoadSceneAsync(Scene);
            var game = Object.FindAnyObjectByType<LocalGame>();
            while (!game.InLobby) yield return null;
            var root=Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            Click(root,"mode"); Click(root,"mode-practice"); Click(root,"start");
            while (!game.Ready) yield return null;
            var camera=Camera.main;
            var hands=camera.GetComponentsInChildren<Camera>(true).Single(c=>c!=camera);
            float baseFov=camera.fieldOfView;
            var mouse=InputSystem.AddDevice<Mouse>();
            IEnumerator Ticks(int n){int end=game.MatchState.Tick+n;while(game.MatchState.Tick<end) yield return null;}
            IEnumerator Right()
            {
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right));
                yield return Ticks(2); InputSystem.QueueStateEvent(mouse,new MouseState()); yield return Ticks(9);
            }
            try
            {
                foreach(string weapon in new[]{"weapon.feather08","weapon.longglass96"})
                {
                    Click(root,"open-shop"); yield return null; Click(root,weapon); yield return Ticks(25);
                    yield return Right(); Assert.That(game.Player.Read().ScopeLevel,Is.Zero);
                    Click(root,"close-shop"); yield return Ticks(3); yield return Right();
                    Assert.That(game.Player.Read().ScopeLevel,Is.EqualTo(1),weapon);
                    var lens=root.Q("scope-lens");
                    Assert.That(lens.resolvedStyle.display,Is.EqualTo(DisplayStyle.Flex));
                    Assert.That(root.Q("reticle").resolvedStyle.display,Is.EqualTo(DisplayStyle.None));
                    Assert.That(hands.enabled,Is.False); float first=camera.fieldOfView;
                    Assert.That(first,Is.LessThan(baseFov));
                    yield return Right();Assert.That(camera.fieldOfView,Is.LessThan(first));
                    Assert.That(game.Player.Read().ScopeLevel,Is.EqualTo(2));
                    yield return Right();Assert.That(game.Player.Read().ScopeLevel,Is.Zero);
                    Assert.That(camera.fieldOfView,Is.EqualTo(baseFov).Within(.01));Assert.That(hands.enabled,Is.True);
                    Assert.That(lens.resolvedStyle.display,Is.EqualTo(DisplayStyle.None));
                }
                Click(root,"open-shop");yield return null;Click(root,"weapon.ember47");yield return Ticks(23);
                Click(root,"close-shop");yield return Right();
                Assert.That(game.Player.Read().ScopeLevel,Is.Zero);
                Assert.That(root.Q("reticle").resolvedStyle.display,Is.EqualTo(DisplayStyle.Flex));
            }
            finally {InputSystem.RemoveDevice(mouse);game.ReturnToLobby();}
        }

        [UnityTest] public IEnumerator RangeHeadshotLightsTheCenteredMedalAndExpiresInTheFeed()
        {
            yield return SceneManager.LoadSceneAsync(Scene);
            var game = Object.FindAnyObjectByType<LocalGame>();
            while (!game.InLobby) yield return null;
            var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            Click(root,"mode"); Click(root,"mode-practice"); Click(root,"start");
            while (!game.Ready) yield return null;
            IEnumerator Ticks(int n)
            {
                int end = game.MatchState.Tick+n;
                while (game.MatchState.Tick < end) yield return null;
            }
            yield return Ticks(30); Click(root,"open-shop"); yield return null;
            Click(root,"weapon.ember47"); yield return Ticks(25); Click(root,"close-shop");
            yield return Ticks(22);
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                var target=Object.FindObjectsByType<ArenaPawn>().Single(a=>a.name=="Seat 8");
                for (int burst=0; burst<5 && game.MatchState.PracticeHeadshots==0; burst++)
                {
                    if (!target.Alive) yield return Ticks(40);
                    for(int aimFrame=0;aimFrame<6;aimFrame++)
                    {
                        var hitbox=target.HeadHitbox;
                        var ray=hitbox.transform.TransformPoint(hitbox.center)-Camera.main.transform.position;
                        float yaw=Mathf.Atan2(ray.x,ray.z)*Mathf.Rad2Deg;
                        float pitch=Mathf.Atan2(ray.y,new Vector2(ray.x,ray.z).magnitude)*Mathf.Rad2Deg;
                        var view=game.Player.Read();
                        var correction=new Vector2(Mathf.DeltaAngle(view.Yaw,yaw),pitch-view.Pitch);
                        if(correction.sqrMagnitude<.0025f) break;
                        InputSystem.QueueDeltaStateEvent(mouse.delta,correction/game.Sensitivity);
                        yield return Ticks(3);
                    }
                    InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
                    yield return Ticks(4); InputSystem.QueueStateEvent(mouse, new MouseState()); yield return Ticks(12);
                }
                var fired=game.Player.Read();
                Assert.That(game.MatchState.PracticeHeadshots,Is.Positive,
                    $"gun={fired.WeaponId}, rounds={fired.Magazine}, yaw={fired.Yaw}, pitch={fired.Pitch}, kills={game.MatchState.PracticeKills}");
                var medal = root.Q("kill-medal");
                var row = root.Q("kill-feed").Children().First(r => r.resolvedStyle.display == DisplayStyle.Flex);
                Assert.That(row.Q<Label>(className:"kill-tags").text,Does.Contain("爆头"));
                Assert.That(row.Q<Image>().image,Is.Not.Null);
                Assert.That(row.Q<Label>(className:"kill-tool").text,Is.Not.Empty);
                Assert.That(row.ClassListContains("own-kill"),Is.True);
                Assert.That(medal.Children().First().ClassListContains("confirmed"),Is.True);
                Assert.That(medal.worldBound.center.x,Is.EqualTo(root.worldBound.center.x).Within(2));
                yield return Ticks(305);
                Assert.That(root.Q("kill-feed").Children().All(r => r.resolvedStyle.display == DisplayStyle.None),Is.True);
                Assert.That(medal.Children().First().ClassListContains("confirmed"),Is.False);
            }
            finally { InputSystem.RemoveDevice(mouse); game.ReturnToLobby(); }
        }
    }
}

