using System.Collections;
using AnimeCS.Match;
using AnimeCS.Play;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Cursor=UnityEngine.Cursor;
using MouseButton=UnityEngine.InputSystem.LowLevel.MouseButton;

namespace AnimeCS.Tests
{
    public sealed class KeyboardReplayTests
    {
        [UnityTest] public IEnumerator CounterTapAtThreeRenderRates()
        {
            yield return SceneManager.LoadSceneAsync("Assets/AnimeCS/Scenes/LocalMatch.unity");
            var game=Object.FindAnyObjectByType<LocalGame>();
            foreach(int fps in new[]{30,60,144}) Replay(game.InputActions,fps);
        }

        [UnityTest] public IEnumerator ShortUtilitySlotTapReachesTheNextTickOnce()
        {
            yield return SceneManager.LoadSceneAsync("Assets/AnimeCS/Scenes/LocalMatch.unity");
            var game = Object.FindAnyObjectByType<LocalGame>();
            var device = InputSystem.AddDevice<Keyboard>();
            try
            {
                using var keyboard = new KeyboardSeat(game.InputActions);
                var view = new SeatView { Health = 100, Match = new PublicMatchView { Round = 1, Phase = RoundPhase.Contest } };
                keyboard.Sample(true);
                InputSystem.QueueStateEvent(device, new KeyboardState(Key.Digit4));
                InputSystem.QueueStateEvent(device, new KeyboardState());
                InputSystem.Update();
                keyboard.Sample(true);
                Assert.That(keyboard.Decide(view).Select, Is.EqualTo(HeldSlot.Utility));
                Assert.That(keyboard.Decide(view).Select, Is.EqualTo(HeldSlot.Current));
            }
            finally { InputSystem.RemoveDevice(device); }
        }

        [UnityTest] public IEnumerator ShortWeaponAndPauseTapsReachTheLocalPlayer()
        {
            yield return SceneManager.LoadSceneAsync("Assets/AnimeCS/Scenes/LocalMatch.unity");
            var game = Object.FindAnyObjectByType<LocalGame>();
            while (!game.InLobby) yield return null;

            var field = typeof(LocalGame).GetField("_rules", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var original = (MatchRules)field.GetValue(game);
            var rules = Object.Instantiate(original);
            var device = InputSystem.AddDevice<Keyboard>();
            try
            {
                rules.buySeconds = .08f;
                field.SetValue(game, rules);
                game.Restart();
                for (int i = 0; i < 180 && game.MatchState.Phase == RoundPhase.Buy; i++) yield return null;
                Assert.That(game.MatchState.Phase, Is.EqualTo(RoundPhase.Contest));
                string pistol = game.Player.Read().WeaponId;
                Assert.That(pistol, Is.Not.EqualTo("weapon.rubyedge"));

                foreach (var tap in new[] { (Key.Digit3, "weapon.rubyedge"), (Key.Digit2, pistol), (Key.Q, "weapon.rubyedge") })
                {
                    // A fast tap can press and release entirely between two simulation steps.
                    InputSystem.QueueStateEvent(device, new KeyboardState(tap.Item1));
                    InputSystem.QueueStateEvent(device, new KeyboardState());
                    for (int i = 0; i < 90 && game.Player.Read().WeaponId != tap.Item2; i++) yield return null;
                    Assert.That(game.Player.Read().WeaponId, Is.EqualTo(tap.Item2), tap.Item1.ToString());
                }

                foreach (bool paused in new[] { true, false })
                {
                    InputSystem.QueueStateEvent(device, new KeyboardState(Key.Escape));
                    InputSystem.QueueStateEvent(device, new KeyboardState());
                    for (int i = 0; i < 90 && game.MatchState.Paused != paused; i++) yield return null;
                    Assert.That(game.MatchState.Paused, Is.EqualTo(paused));
                }
            }
            finally
            {
                InputSystem.RemoveDevice(device);
                field.SetValue(game, original);
                Object.Destroy(rules);
            }
        }

        [UnityTest] public IEnumerator LockedMouseScrollInspectAndDropWorkDuringFreeze()
        {
            yield return SceneManager.LoadSceneAsync("Assets/AnimeCS/Scenes/LocalMatch.unity");
            var game=Object.FindAnyObjectByType<LocalGame>();while(!game.InLobby) yield return null;
            var keys=InputSystem.AddDevice<Keyboard>();var mouse=InputSystem.AddDevice<Mouse>();
            try
            {
                game.Restart();yield return null;yield return null;
                if(Application.isFocused) { Assert.That(Cursor.lockState,Is.EqualTo(CursorLockMode.Locked));Assert.That(Cursor.visible,Is.False); }
                var start=game.Player.Read();
                InputSystem.QueueDeltaStateEvent(mouse.delta,new Vector2(380,-80));
                InputSystem.QueueStateEvent(keys,new KeyboardState(Key.W,Key.LeftCtrl));
                for(int i=0;i<15 && Mathf.Abs(Mathf.DeltaAngle(start.Yaw,game.Player.Read().Yaw))<5;i++)yield return null;
                var aimed=game.Player.Read(); Assert.That(Mathf.Abs(Mathf.DeltaAngle(start.Yaw,aimed.Yaw)),Is.GreaterThan(5));
                Assert.That((aimed.Position-start.Position).sqrMagnitude,Is.LessThan(.055f)); Assert.That(aimed.Motion.Crouch,Is.Zero);
                InputSystem.QueueStateEvent(keys,new KeyboardState(Key.B)); yield return null;
                InputSystem.QueueStateEvent(keys,new KeyboardState()); yield return null;
                var shop=Object.FindAnyObjectByType<UIDocument>().rootVisualElement.Q("shop");
                for(int i=0;i<12 && shop.resolvedStyle.display!=DisplayStyle.Flex;i++)yield return null;
                Assert.That(shop.resolvedStyle.display,Is.EqualTo(DisplayStyle.Flex));
                Assert.That(Cursor.lockState,Is.EqualTo(CursorLockMode.None)); Assert.That(Cursor.visible,Is.True);
                var before=game.Player.Read();
                InputSystem.QueueDeltaStateEvent(mouse.delta,new Vector2(210,70));
                yield return null;yield return null;
                Assert.That(Mathf.DeltaAngle(before.Yaw,game.Player.Read().Yaw),Is.EqualTo(0).Within(.01));
                var offer=shop.Q<Button>("weapon.heavy50");Assert.That(offer.enabledSelf,Is.True);
                var panel=shop.panel.visualTree.worldBound;var point=offer.worldBound.center;
                var screenPoint=new Vector2(point.x*Screen.width/panel.width,Screen.height-point.y*Screen.height/panel.height);
                InputSystem.QueueStateEvent(mouse,new MouseState{position=screenPoint});yield return null;
                InputSystem.QueueStateEvent(mouse,new MouseState{position=screenPoint}.WithButton(MouseButton.Left));yield return null;
                InputSystem.QueueStateEvent(mouse,new MouseState{position=screenPoint});yield return null;
                for(int i=0;i<20 && game.Player.Read().SidearmId!="weapon.heavy50";i++)yield return null;
                Assert.That(game.Player.Read().SidearmId,Is.EqualTo("weapon.heavy50"),"A real pointer click issues the order.");
                Assert.That(game.Player.Read().Credits,Is.EqualTo(200));
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Right));yield return null;yield return null;
                InputSystem.QueueDeltaStateEvent(mouse.delta,new Vector2(210,70));
                for(int i=0;i<12 && Mathf.Abs(Mathf.DeltaAngle(before.Yaw,game.Player.Read().Yaw))<4;i++)yield return null;
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(before.Yaw,game.Player.Read().Yaw)),Is.GreaterThan(4));
                InputSystem.QueueStateEvent(mouse,new MouseState());yield return null;
                Assert.That(Cursor.visible,Is.True);
                InputSystem.QueueStateEvent(keys,new KeyboardState(Key.Escape));yield return null;
                InputSystem.QueueStateEvent(keys,new KeyboardState());yield return null;
                Assert.That(shop.resolvedStyle.display,Is.EqualTo(DisplayStyle.None));
                Assert.That(game.MatchState.Paused,Is.False);
                if(Application.isFocused) Assert.That(Cursor.lockState,Is.EqualTo(CursorLockMode.Locked));
                InputSystem.QueueStateEvent(keys,new KeyboardState(Key.G));
                for(int i=0;i<14 && game.Player.Read().SidearmId!=null;i++)yield return null;
                Assert.That(game.Player.Read().SidearmId,Is.Null);Assert.That(game.Player.Read().Held,Is.EqualTo(HeldSlot.Knife));
                InputSystem.QueueStateEvent(keys,new KeyboardState());
                InputSystem.QueueDeltaStateEvent(mouse.scroll,new Vector2(0,-120));
                for(int i=0;i<15 && game.Player.Read().Held!=HeldSlot.Objective;i++)yield return null;
                Assert.That(game.Player.Read().Held,Is.EqualTo(HeldSlot.Objective));
                InputSystem.QueueDeltaStateEvent(mouse.scroll,new Vector2(0,120));
                for(int i=0;i<100 && (game.Player.Read().Held!=HeldSlot.Knife || game.MatchState.Tick-game.Player.Read().EquipTick<20);i++)yield return null;
                InputSystem.QueueStateEvent(keys,new KeyboardState(Key.F));
                for(int i=0;i<15 && game.Player.Read().InspectTick<0;i++)yield return null;
                Assert.That(game.Player.Read().InspectTick,Is.GreaterThan(0));
                game.TogglePause();Assert.That(Cursor.visible,Is.True);Assert.That(Cursor.lockState,Is.EqualTo(CursorLockMode.None));
            }
            finally{InputSystem.RemoveDevice(keys);InputSystem.RemoveDevice(mouse);game.ReturnToLobby();}
        }

        private static void Replay(InputActionAsset actions,int fps)
        {
            var device=InputSystem.AddDevice<Keyboard>();
            try
            {
                using var input=new KeyboardSeat(actions);
                var motion=MotionContact.Standing;
                bool stopped=false;
                var view=new SeatView { Health=100, Match=new PublicMatchView{Round=1,Phase=RoundPhase.Contest} };
                int next=1, previous=-1;
                float rightDistance=0;
                for (int frame=0;frame<=fps;frame++)
                {
                    double time=(double)frame/fps;
                    int phase=time<.42?0:time<.52?1:2;
                    if (phase!=previous)
                    {
                        var state=phase==0?new KeyboardState(Key.A):phase==1?new KeyboardState(Key.D):new KeyboardState();
                        InputSystem.QueueStateEvent(device,state); previous=phase;
                    }
                    InputSystem.Update();
                    input.Sample(true);
                    while (next/(double)MatchRules.TickRate<=time+.000001)
                    {
                        view.Match.Tick=next++;
                        motion.Velocity=SeatMovement.Advance(motion,input.Decide(view),6,ref stopped);
                        if (phase==0 && time>=.25) Assert.That(motion.Velocity.x, Is.EqualTo(-6).Within(.001));
                        if (phase>=1) rightDistance+=Mathf.Max(0,motion.Velocity.x)*SeatMovement.StepTime;
                    }
                }
                Assert.That(rightDistance,Is.Zero); Assert.That(motion.Velocity,Is.EqualTo(Vector2.zero));
            }
            finally { InputSystem.RemoveDevice(device); }
        }

    }
}
