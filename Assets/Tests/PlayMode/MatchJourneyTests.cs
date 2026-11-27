using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimeCS.Match;
using AnimeCS.Play;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimeCS.Tests
{
    public sealed class MatchJourneyTests
    {
        [UnityTest]
        public IEnumerator RegisteredPlayerFinishesRegulationRestartsAndReturnsThroughHud()
        {
            using var services = new LobbyTestServices();
            yield return services.Ready();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Gameplay/LocalMatch.unity");
            var game = Object.FindAnyObjectByType<LocalGame>();
            var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            yield return Until(() => game.InLobby, 60, "lobby");
            yield return services.Register(root);
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var field=typeof(LocalGame).GetField("_rules",flags);
            var previous=(MatchRules)field.GetValue(game);
            var rules=Object.Instantiate(previous);
            field.SetValue(game,rules);
            Assert.That(rules.winsRequired, Is.EqualTo(13));

            try
            {
                rules.buySeconds = .50f;
                rules.contestSeconds = .50f;
                rules.settlementSeconds = .40f;
                Press(root, "start");
                yield return Until(() => game.Ready, 75, "playable match");
                var seen = new HashSet<RoundPhase>();
                var rounds = new HashSet<int>();
                double until = Time.realtimeSinceStartupAsDouble + 100;
                while (game.MatchState.Phase != RoundPhase.Finished && Time.realtimeSinceStartupAsDouble < until)
                {
                    rounds.Add(game.MatchState.Round);
                    seen.Add(game.MatchState.Phase);
                    yield return null;
                }
                Assert.That(game.MatchState.Phase, Is.EqualTo(RoundPhase.Finished));
                Assert.That(seen, Is.SupersetOf(new[]{RoundPhase.Buy, RoundPhase.Contest, RoundPhase.Settlement}));
                int a=game.MatchState.Score0, b=game.MatchState.Score1, reports=game.PostMatchReports.Count;
                Assert.That(Math.Max(a,b), Is.EqualTo(13));
                Assert.That(Math.Min(a,b), Is.EqualTo(12));
                Assert.That(reports, Is.EqualTo(a+b));
                Assert.That(rounds.Count, Is.EqualTo(a+b));
                yield return null;
                Assert.That(root.Q("menu").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                string title = root.Q<Label>("pause-title").text;
                Assert.That(title, Is.EqualTo(a>b ? "我方获胜" : "对方获胜"));
                yield return LobbyTestServices.Capture("OfflineFinal");
                Press(root,"restart");
                yield return Until(() => game.Ready && game.MatchState.Round==1, 75, "restart from result");
                Assert.That(game.MatchState.Score0+game.MatchState.Score1, Is.Zero);
                game.TogglePause();
                Press(root,"to-lobby");
                yield return Until(() => game.InLobby, 20, "return from pause");
                Assert.That(root.Q<Button>("start").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                var evidence=Environment.GetEnvironmentVariable("ANIMECS_AUDIT_EVIDENCE");
                if(!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(evidence,"offline-journey.txt"),
                    $"map={game.SelectedMap.Id}\nscore={a}-{b}\nrounds={rounds.Count}\nreports={reports}\nresultTitle={title}\nrestartScore=0-0\nreturnedToLobby=true\n");
            }
            finally
            {
                if(game) field.SetValue(game,previous);
                Object.Destroy(rules);

            }
        }

        private static IEnumerator Until(Func<bool> done, int seconds, string stage)
        {
            double until = Time.realtimeSinceStartupAsDouble + seconds;
            while (!done() && Time.realtimeSinceStartupAsDouble < until) yield return null;
            Assert.That(done(), Is.True, stage);
        }

        private static void Press(VisualElement root,string name)
        {
            var button=root.Q<Button>(name);
            Assert.That(button, Is.Not.Null,name);
            Assert.That(button.enabledInHierarchy, Is.True,name);
            using var click=NavigationSubmitEvent.GetPooled();
            click.target=button;
            button.SendEvent(click);
        }
    }
}
