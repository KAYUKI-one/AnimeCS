using System.Collections;
using System.Linq;
using AnimeCS.Content;
using AnimeCS.Play;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AnimeCS.Tests
{
    public sealed class BattleLoadingTests
    {
        [UnityTest]
        public IEnumerator MapEntryShowsTheSelectedMapAndTenTwoDimensionalPortraits()
        {
            using var services=new LobbyTestServices();
            yield return services.Ready();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Gameplay/LocalMatch.unity");
            var game=Object.FindAnyObjectByType<LocalGame>();
            var root=Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            yield return new WaitUntil(()=>game.InLobby);
            yield return services.Register(root);
            var map=Resources.FindObjectsOfTypeAll<MapDefinition>().First(m=>m.Id=="map.broadcast-station");
            game.SelectMap(map);
            game.Restart();
            var screen=root.Q("match-loading");
            Assert.That(screen.style.display.value,Is.EqualTo(DisplayStyle.Flex));
            Assert.That(screen.Q<Label>(className:"load-title").text,Is.EqualTo(map.DisplayName));
            var cards=screen.Query<VisualElement>(className:"load-portrait").ToList();
            Assert.That(cards.Count,Is.EqualTo(10));
            Assert.That(cards.All(c=>c.style.backgroundImage.value.texture),Is.True);
            Assert.That(screen.Q(className:"load-art").style.backgroundImage.value.texture,Is.EqualTo(map.LoadingArt));
            yield return LobbyTestServices.Capture("BattleLoading");
            float last=0;
            double deadline=Time.realtimeSinceStartupAsDouble+75;
            while (!game.Ready && Time.realtimeSinceStartupAsDouble<deadline)
            {
                float progress=screen.Q(className:"load-fill").style.width.value.value;
                Assert.That(progress,Is.GreaterThanOrEqualTo(last));
                last=progress;
                yield return null;
            }
            Assert.That(game.Ready,Is.True);
            Assert.That(screen.style.display.value,Is.EqualTo(DisplayStyle.None));
            game.ReturnToLobby();
        }
    }
}
