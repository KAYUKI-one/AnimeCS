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

