using System.Collections;
using System.Linq;
using AnimeCS.Content;
using AnimeCS.Match;
using AnimeCS.Play;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AnimeCS.Tests
{
    public sealed class StartupTests
    {
        [UnityTest] public IEnumerator StartupWiresRealMapSeatsHudAndPause()
        {
            yield return SceneManager.LoadSceneAsync("Assets/AnimeCS/Scenes/LocalMatch.unity");
            var game = Object.FindAnyObjectByType<LocalGame>();
            for (int i = 0; i < 60 && !game.InLobby; i++) yield return null;
            Assert.That(game.InLobby, Is.True);
            var doc = Object.FindAnyObjectByType<UIDocument>();
            var root=doc.rootVisualElement;
            void Press(string name)
            {
                var button=root.Q<Button>(name);
                using var submit=NavigationSubmitEvent.GetPooled();
                submit.target=button; button.SendEvent(submit);
            }
            Assert.That(root.Q<Button>("start").text, Is.EqualTo("开始游戏"));
            var party = game.transform.Find("队伍大厅/Party");
            Assert.That(game.LobbyPartyCount, Is.EqualTo(1));
            Assert.That(party.GetComponentsInChildren<Animator>().Length, Is.EqualTo(1));
            Assert.That(game.transform.Find("队伍大厅").GetComponentInChildren<MeshFilter>().sharedMesh,Is.Not.Null);
            Assert.That(root.Q("lineup"), Is.Null);
            game.SetLobbyTeammates(new[]{new LobbyMember("outfit.flor","weapon.fold44")});
            Assert.That(game.LobbyPartyCount,Is.EqualTo(2));
            Assert.That(party.GetComponentsInChildren<Animator>().Length,Is.EqualTo(2));
            game.SetLobbyTeammates(Enumerable.Repeat(new LobbyMember("outfit.lily"),5).ToArray());
            Assert.That(game.LobbyPartyCount,Is.EqualTo(5));
            game.SetLobbyTeammates(null);
            Assert.That(party.GetComponentsInChildren<Animator>().Length,Is.EqualTo(1));
            yield return null;
            Press("inventory"); yield return null;
            var page=root.Q("sheet");var rail=root.Q(className:"nav-rail");
            Assert.That(page.worldBound.xMin,Is.EqualTo(rail.worldBound.xMax).Within(2));
            Assert.That(page.Q(className:"wardrobe-tabs"),Is.Null);
            Assert.That(page.Query<Button>(className:"item-tile").ToList().Count,Is.GreaterThan(15));
            Press("weapon.street20"); yield return null;
            Assert.That(page.ClassListContains("inspect-page"),Is.True);
            Assert.That(Camera.main.transform.Find("库存检视"),Is.Not.Null);
            Press("close");yield return null;
            Assert.That(page.Q("weapon.street20"),Is.Not.Null);
            Press("equipment");yield return null;
            Assert.That(page.ClassListContains("equipment-page"),Is.True);
            var mounts=page.Q("loadout-slots");var stage=page.Q("inspection-stage");var bag=page.Q("sheet-content");
            Assert.That(mounts.Query<Button>(className:"mount-slot").ToList().Count,Is.EqualTo(10));
            Assert.That(mounts.worldBound.xMax,Is.LessThanOrEqualTo(stage.worldBound.xMin+2));
            Assert.That(bag.worldBound.xMin,Is.GreaterThanOrEqualTo(stage.worldBound.xMax-2));
            Assert.That(rail.worldBound.width/root.worldBound.width,Is.LessThan(.13f));
            Press("equip-slot-1"); yield return null;
            Assert.That(page.Q("backpack-items").Q<Button>("weapon.ember47"),Is.Not.Null);
            Press("weapon.ember47");yield return null;
            Assert.That(page.Q<Button>("equip-slot-1").Query<Label>().ToList().Last().text,Is.Not.Empty);
            Press("equip-slot-6");yield return null;
            Assert.That(page.Q("backpack-items").Query<Button>().ToList().Count,Is.GreaterThan(0));
            Press("settings");
            yield return null;
            Assert.That(root.Q("sheet-content").Query<Slider>(className:"setting-field").ToList().Count,Is.EqualTo(2));
            Press("close"); Press("start");
            yield return null;
            Assert.That(game.Ready, Is.True);
            Assert.That(party.gameObject.activeInHierarchy,Is.False);
            Assert.That(Object.FindObjectsByType<ArenaPawn>().Length, Is.EqualTo(10));
            Assert.That(game.Player.Read().Credits, Is.EqualTo(900));
            Assert.That(game.Player.Read().Side, Is.EqualTo(Side.Attack));
            Assert.That(doc.rootVisualElement.Query<Button>().ToList().Any(b => b.text != null && b.text.Contains("胸甲")), Is.True);
            Assert.That(doc.rootVisualElement.Query<Button>().ToList().Any(b => b.text != null && b.text.Contains("头盔")), Is.True);
            Assert.That(game.MatchState.Phase, Is.EqualTo(RoundPhase.Buy));
            Press("open-shop");
            yield return null; yield return null;
            Assert.That(root.Q("shop").resolvedStyle.display,Is.EqualTo(DisplayStyle.Flex));
            Press("close-shop");
            game.TogglePause();
            int tick = game.MatchState.Tick;
            yield return null; yield return null;
            Assert.That(game.MatchState.Tick, Is.EqualTo(tick));
            game.TogglePause();
            for (int i = 0; i < 60 && game.MatchState.Tick == tick; i++) yield return null;
            Assert.That(game.MatchState.Tick, Is.GreaterThan(tick));
            game.TogglePause();Press("to-lobby");
            yield return null;
            Assert.That(game.InLobby,Is.True);
            Assert.That(party.GetComponentsInChildren<Animator>().Length,Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<Animator>().Any(a=>a.transform.IsChildOf(Camera.main.transform)),Is.False);
        }
    }
}
