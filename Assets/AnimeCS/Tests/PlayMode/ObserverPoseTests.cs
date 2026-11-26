using System.Collections;
using System.Reflection;
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
    public sealed class ObserverPoseTests
    {
        // A deterministic input provider can also be used to review the actual remote presentation in the editor.
        public sealed class PoseDriver : ISeatDriver
        {
            public float yaw,pitch;
            public bool crouch,fire;
            public HeldSlot slot;
            public string purchase;
            public SeatIntent Decide(in SeatView view)=>new SeatIntent{Yaw=yaw,Pitch=pitch,Crouch=crouch,Fire=fire,Select=slot,
                Buy=view.Match.Phase==RoundPhase.Buy && !string.IsNullOrEmpty(purchase)?BuySlot.Primary:BuySlot.None,ItemId=purchase};
        }

        [UnityTest] public IEnumerator CarrierBotPlantsOnDustWhileTheLocalDefenderRemainsAtSpawn()
        {
            yield return SceneManager.LoadSceneAsync("Assets/AnimeCS/Scenes/LocalMatch.unity");
            var game=Object.FindAnyObjectByType<LocalGame>();while(!game.InLobby)yield return null;
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var rulesField=typeof(LocalGame).GetField("_rules",flags);
            var original=(MatchRules)rulesField.GetValue(game);var rules=Object.Instantiate(original);
            try
            {
                rules.buySeconds=.08f;rulesField.SetValue(game,rules);game.OpeningSide=FirstSide.Defend;game.Restart();
                var match=(LocalMatch)typeof(LocalGame).GetField("_match",flags).GetValue(game);
                var catalog=(ContentCatalog)typeof(LocalGame).GetField("_catalog",flags).GetValue(game);
                var carrier=match.BindSeat(5);var bot=new ObjectiveBot(catalog,471);
                var start=game.Player.Read().Position;
                for(int i=0;i<MatchRules.Ticks(rules.contestSeconds+1) && match.Public.Phase!=RoundPhase.Planted;i++)
                {
                    var v=carrier.Read(); v.VisibleEnemy=null; // Isolate navigation and objective decisions from firefights.
                    carrier.Submit(match.Public.Tick+1,bot.Decide(v));match.Step();
                }
                Assert.That(match.Public.Phase,Is.EqualTo(RoundPhase.Planted),"bot at "+carrier.Read().Position);
                Assert.That(match.Public.Score0+match.Public.Score1,Is.Zero);
                Assert.That(Vector3.Distance(match.WorldObjective.Value,catalog.Map.SitePosition(match.Public.PlantedSite)),Is.LessThan(catalog.Map.Radius));
                Assert.That(Vector3.Distance(start,game.Player.Read().Position),Is.LessThan(.26f));
                Assert.That(match.Public.RemainingTicks,Is.EqualTo(40*MatchRules.TickRate));
                int remaining=match.Public.RemainingTicks;
                for(int i=0;i<remaining-1;i++)match.Step();
                Assert.That(match.Public.Phase,Is.EqualTo(RoundPhase.Planted));match.Step();
                Assert.That(match.Public.Reason,Is.EqualTo(WinReason.DeviceExpired)); Assert.That(game.Player.Read().Health,Is.GreaterThan(0));
                var before=game.Player.Read().Position;
                for(int i=0;i<12;i++){game.Player.Submit(match.Public.Tick+1,new SeatIntent{Move=Vector2.right,Yaw=40});match.Step();}
                Assert.That(Vector3.Distance(game.Player.Read().Position,before),Is.GreaterThan(.1f));
                Assert.That(match.Public.Score0+match.Public.Score1,Is.EqualTo(1));
            }
            finally{rulesField.SetValue(game,original);Object.Destroy(rules);game.ReturnToLobby();}
        }

        [UnityTest] public IEnumerator OnlyVisibleLivingTeammatesCarryIdsFromEveryFacingAndSpectatorView()
        {
            yield return SceneManager.LoadSceneAsync("Assets/AnimeCS/Scenes/LocalMatch.unity");
            var game=Object.FindAnyObjectByType<LocalGame>();
            while(!game.InLobby) yield return null;
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var rulesField=typeof(LocalGame).GetField("_rules",flags);
            var original=(MatchRules)rulesField.GetValue(game);
            var rules=Object.Instantiate(original);
            rules.buySeconds=.08f;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var origin=new Vector3(1460,203,1320);
            floor.transform.SetPositionAndRotation(origin-Vector3.up*.25f,Quaternion.identity);
            floor.transform.localScale=new Vector3(28,.5f,28);
            wall.transform.position=origin+new Vector3(0,1.4f,2);
            wall.transform.localScale=new Vector3(2.4f,3,.3f);
            wall.SetActive(false);
            IEnumerator Advance(int ticks)
            {
                int until=game.MatchState.Tick+ticks;
                while(game.MatchState.Tick<until) yield return null;
                yield return new WaitForEndOfFrame();
            }
            try
            {
                rulesField.SetValue(game,rules);
                game.SetLobbyTeammates(new[]{new LobbyMember("outfit.flor",displayId:"  小枫  ")});
                game.Restart();
                var match=(LocalMatch)typeof(LocalGame).GetField("_match",flags).GetValue(game);
                var actors=(ArenaPawn[])typeof(LocalGame).GetField("_actors",flags).GetValue(game);
                var spawn=typeof(ArenaPawn).GetMethod("Spawn",flags);
                var facing=new PoseDriver();
                for(int i=0;i<LocalMatch.Seats;i++)
                {
                    Vector3 offset=i==0?Vector3.zero:i==1?Vector3.forward*4:
                        i==2?new Vector3(-1.4f,0,7):i==5?new Vector3(1.4f,0,5):new Vector3(i-4,0,-5);
                    game.SetSeatDriver(i,i==1?facing:new PoseDriver());
                    spawn.Invoke(actors[i],new object[]{origin+offset+Vector3.up*.04f,0f});
                }
                game.SetSeatDisplayId(5,"敌方测试");
                Physics.SyncTransforms();
                var layer=Object.FindAnyObjectByType<UIDocument>().rootVisualElement.Q("team-ids");
                var ally=layer.Q<Label>("team-id-1");
                foreach(float yaw in new[]{0f,90f,180f})
                {
                    facing.yaw=yaw;
                    yield return Advance(10);
                    Assert.That(ally.resolvedStyle.display,Is.EqualTo(DisplayStyle.Flex),"facing "+yaw);
                    Assert.That(ally.text,Is.EqualTo("小枫"));
                    Assert.That(layer.Q<Label>("team-id-0").resolvedStyle.display,Is.EqualTo(DisplayStyle.None));
                    Assert.That(layer.Q<Label>("team-id-5"),Is.Null,"Enemy IDs must never be bound.");
                }
                float standing=ally.resolvedStyle.top;
                facing.crouch=true; yield return Advance(22);
                Assert.That(ally.resolvedStyle.top,Is.GreaterThan(standing+8),"Move the ID with the actual crouching head.");
                wall.SetActive(true); Physics.SyncTransforms(); yield return Advance(3);
                Assert.That(ally.resolvedStyle.display,Is.EqualTo(DisplayStyle.None),"Solid cover");
                wall.SetActive(false); Physics.SyncTransforms(); facing.yaw=0; facing.crouch=false; yield return Advance(16);
                Assert.That(ally.resolvedStyle.display,Is.EqualTo(DisplayStyle.Flex));
                game.TogglePause(); yield return null; yield return new WaitForEndOfFrame();
                Assert.That(ally.resolvedStyle.display,Is.EqualTo(DisplayStyle.None));
                game.TogglePause(); yield return Advance(4);
                int[] damage=(int[])typeof(LocalMatch).GetField("_damage",flags).GetValue(match);
                var apply=typeof(LocalMatch).GetMethod("ApplyDamage",flags);
                damage[0]=500; apply.Invoke(match,null);
                yield return Advance(6);
                Assert.That(game.Player.WatchTeam().Seat,Is.EqualTo(1));
                Assert.That(ally.resolvedStyle.display,Is.EqualTo(DisplayStyle.None),"The new camera owner");
                var other=layer.Q<Label>("team-id-2");
                Assert.That(other.resolvedStyle.display,Is.EqualTo(DisplayStyle.Flex));
                damage[2]=500; apply.Invoke(match,null); yield return Advance(4);
                Assert.That(other.resolvedStyle.display,Is.EqualTo(DisplayStyle.None),"Eliminated teammate");
            }
            finally
            {
                game.ReturnToLobby(); rulesField.SetValue(game,original);
                Object.Destroy(floor); Object.Destroy(wall); Object.Destroy(rules);
            }
        }

        [UnityTest] public IEnumerator ATeammatesAimCrouchAndKnifeRenderFromTheSeatState()
        {
            yield return SceneManager.LoadSceneAsync("Assets/AnimeCS/Scenes/LocalMatch.unity");
            var game=Object.FindAnyObjectByType<LocalGame>();
            while(!game.InLobby)yield return null;
            var field=typeof(LocalGame).GetField("_rules",BindingFlags.NonPublic|BindingFlags.Instance);
            var original=(MatchRules)field.GetValue(game);
            var rules=Object.Instantiate(original);rules.buySeconds=.08f;field.SetValue(game,rules);
            try
            {
                game.Restart();
                var driver=new PoseDriver();
                for(int seat=0;seat<LocalMatch.Seats;seat++)game.SetSeatDriver(seat,seat==1?driver:new PoseDriver());
                var body=game.transform.Find("Seat 2/Appearance/Third person");
                for(int i=0;i<180 && game.MatchState.Phase!=RoundPhase.Contest;i++)yield return null;
                Assert.That(game.MatchState.Phase,Is.EqualTo(RoundPhase.Contest));
                var head=body.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Head).Find("Head fittings");
                Assert.That(head,Is.Not.Null);
                yield return new WaitForEndOfFrame();
                float standing=head.position.y,startPitch=head.forward.y;
                driver.crouch=true;driver.pitch=42;driver.yaw=25;driver.slot=HeldSlot.Knife;
                int before=game.MatchState.Tick;
                for(int i=0;i<240 && game.MatchState.Tick<before+28;i++)yield return null;
                yield return new WaitForEndOfFrame();
                var match=(LocalMatch)typeof(LocalGame).GetField("_match",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(game);
                var view=match.BindSeat(1).Read();
                Assert.That(view.WeaponId,Is.EqualTo("weapon.rubyedge"));
                Assert.That(view.Motion.Crouch,Is.GreaterThan(.9f));Assert.That(standing-head.position.y,Is.GreaterThan(.25f));
                Assert.That(view.Pitch,Is.EqualTo(42));Assert.That(head.forward.y-startPitch,Is.GreaterThan(.3f));
                Assert.That(body.Find("Weapon/RubyEdge(Clone)"),Is.Not.Null);
                driver.slot=HeldSlot.Sidearm;driver.crouch=false;driver.pitch=-26;driver.yaw=-32;
                before=game.MatchState.Tick;
                for(int i=0;i<240 && game.MatchState.Tick<before+28;i++)yield return null;
                yield return new WaitForEndOfFrame();view=match.BindSeat(1).Read();
                Assert.That(view.WeaponId,Is.EqualTo(view.SidearmId));Assert.That(view.Magazine,Is.GreaterThan(0));
                Assert.That(head.forward.y-startPitch,Is.LessThan(-.18f));Assert.That(Mathf.Abs(head.position.y-standing),Is.LessThan(.12f));
            }
            finally{field.SetValue(game,original);Object.Destroy(rules);game.ReturnToLobby();}
        }
    }
}
