using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimeCS.Content;
using AnimeCS.Match;
using AnimeCS.Play;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AnimeCS.Tests
{
    public sealed class MatchSimulationTimingTests
    {
        [UnityTest] public IEnumerator DustIITenBotSimulation() => Profile("map.dust2");
        [UnityTest] public IEnumerator BroadcastTenBotSimulation() => Profile("map.broadcast-station");

        static IEnumerator Profile(string mapId)
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Gameplay/LocalMatch.unity");
            var game=Object.FindAnyObjectByType<LocalGame>();
            double deadline=Time.realtimeSinceStartupAsDouble+80;
            while(!game.InLobby && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
            Assert.That(game.InLobby,Is.True);
            var map=Resources.FindObjectsOfTypeAll<MapDefinition>().First(m=>m.Id==mapId);
            game.SelectMap(map);
            game.Restart();
            while(!game.Ready && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
            Assert.That(game.Ready,Is.True,mapId);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var match=(LocalMatch)typeof(LocalGame).GetField("_match",flags).GetValue(game);
            var catalog=(ContentCatalog)typeof(LocalGame).GetField("_catalog",flags).GetValue(game);
            var drivers=new ObjectiveBot[LocalMatch.Seats];
            var ports=new ISeatPort[LocalMatch.Seats];
            for(int i=0;i<ports.Length;i++)
            {
                drivers[i]=new ObjectiveBot(catalog, 731+i*7919);
                ports[i]=match.BindSeat(i);
            }
            var times=new double[1200];
            var alloc=new long[times.Length];
            var trace=new string[times.Length];
            int warm=0,count=0;
            for(int attempts=0;attempts<18000 && count<times.Length;attempts++)
            {
                var before=match.Public;
                bool contest=before.Phase==RoundPhase.Contest;
                if(before.Phase==RoundPhase.Finished) break;
                long bytes=GC.GetAllocatedBytesForCurrentThread();
                long start=Stopwatch.GetTimestamp();
                for(int i=0;i<ports.Length;i++)
                {
                    var view=ports[i].Read();
                    ports[i].Submit(view.Match.Tick+1,drivers[i].Decide(view));
                }
                match.Step();
                long elapsed=Stopwatch.GetTimestamp()-start;
                long delta=GC.GetAllocatedBytesForCurrentThread()-bytes;
                if(!contest || ++warm<=200) continue;
                times[count]=1000d*elapsed/Stopwatch.Frequency;
                alloc[count]=delta;
                trace[count]=string.Format(CultureInfo.InvariantCulture,"{0},{1},{2:F6},{3}",before.Round,before.Tick,times[count],alloc[count]);
                count++;
            }
            Assert.That(count,Is.EqualTo(times.Length),mapId);
            var folder=Environment.GetEnvironmentVariable("ANIMECS_AUDIT_EVIDENCE");
            if(!string.IsNullOrEmpty(folder))
            {
                File.WriteAllLines(Path.Combine(folder,"sim-"+mapId+".csv"),new[]{"round,tick,ms,managedBytesOnThread"}.Concat(trace));
            }
            game.ReturnToLobby();
        }
    }
}
