using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using AnimeCS.Match;
using NUnit.Framework;
using UnityEngine;

namespace AnimeCS.Tests
{
    public class NetworkTimingTests
    {
        private sealed class PurchaseDriver : ISeatDriver
        {
            internal int Decisions;
            public SeatIntent Decide(in SeatView view) { Decisions++; return new SeatIntent{Buy=BuySlot.Armor}; }
        }

        [Test]
        public void OpeningInputGapKeepsSpawnLookUntilContiguousCommandsAreApplied()
        {
            var host=new HostSeatInput();
            var view=new SeatView { Yaw=359, Pitch=-12, Match=new PublicMatchView { Round=1, Tick=100 } };
            host.ObserveRound(1,100,.08f);
            var second=new SeatIntent { Yaw=1, Pitch=20 };
            Assert.IsTrue(host.ReceiveOrdered(2,100,99,0,second));
            for(int tick=100;tick<105;tick++)
            {
                view.Match.Tick=tick;
                host.ObserveRound(1,tick,.08f);
                var waiting=host.Decide(view);
                Assert.AreEqual(view.Yaw,waiting.Yaw);
                Assert.AreEqual(view.Pitch,waiting.Pitch);
                Assert.Less(host.AppliedSequence,1);
            }
            Assert.IsTrue(host.ReceiveOrdered(1,104,98,0,new SeatIntent { Yaw=0, Pitch=9 }));
            var caughtUp=host.Decide(view);
            Assert.AreEqual(2,host.AppliedSequence);
            Assert.AreEqual(second.Yaw,caughtUp.Yaw);
            Assert.AreEqual(second.Pitch,caughtUp.Pitch);
            Assert.IsFalse(host.ReceiveOrdered(1,104,98,0,new SeatIntent {Yaw=180}));
            view.Match.Tick=120;
            var stopped=host.Decide(view);
            Assert.AreEqual(second.Yaw,stopped.Yaw);
            Assert.AreEqual(second.Pitch,stopped.Pitch);
        }

        [Test]
        public void LegacyInputKeepsItsLastLookAfterHeldMovementTimesOut()
        {
            var host=new HostSeatInput();
            var view=new SeatView {Yaw=280,Pitch=11,Match=new PublicMatchView {Round=1,Tick=8}};
            host.ObserveRound(1,8,.05f);
            var beforeInput=host.Decide(view);
            Assert.AreEqual(280,beforeInput.Yaw);
            Assert.AreEqual(11,beforeInput.Pitch);
            var input=new SeatIntent {Yaw=359,Pitch=-18,Move=Vector2.right};
            Assert.IsTrue(host.Receive(0,8,input));
            var applied=host.Decide(view);
            Assert.AreEqual(input.Yaw,applied.Yaw);
            Assert.AreEqual(input.Pitch,applied.Pitch);
            Assert.AreEqual(0,host.AppliedSequence);
            view.Match.Tick=20;
            host.ObserveRound(1,20,.05f);
            Assert.IsFalse(host.Receive(0,20,new SeatIntent {Yaw=172,Pitch=23}));
            var stopped=host.Decide(view);
            Assert.AreEqual(Vector2.zero,stopped.Move);
            Assert.AreEqual(input.Yaw,stopped.Yaw);
            Assert.AreEqual(input.Pitch,stopped.Pitch);
        }

        [Test]
        public void LateSnapshotsHoldConfirmedLookWhilePositionContinuesAcrossTheGap()
        {
            var frames=new SeatSnapshotBuffer();
            Assert.IsTrue(frames.Push(10,Vector3.zero,359,-10));
            Assert.IsTrue(frames.Push(12,Vector3.right*2,1,10));
            Assert.IsTrue(frames.Sample(11,out var position,out float yaw,out float pitch));
            Assert.That(position.x,Is.EqualTo(1).Within(.001f));
            Assert.That(Mathf.DeltaAngle(yaw,0),Is.EqualTo(0).Within(.001f));
            Assert.That(pitch,Is.EqualTo(0).Within(.001f));
            Assert.IsTrue(frames.Sample(20,out position,out yaw,out pitch));
            Assert.That(position.x,Is.EqualTo(4).Within(.001f));
            Assert.That(yaw,Is.EqualTo(1).Within(.001f));
            Assert.That(pitch,Is.EqualTo(10).Within(.001f));
            Assert.IsFalse(frames.Push(11,Vector3.right,0,0));
            Assert.IsTrue(frames.Push(14,Vector3.right*4,1,10));
            Assert.IsTrue(frames.Sample(15,out _,out yaw,out pitch));
            Assert.That(yaw,Is.EqualTo(1).Within(.001f));
            Assert.That(pitch,Is.EqualTo(10).Within(.001f));
        }

        [Test] public void FullWindowKeepsPurchasesInTheDriverUntilAnAckFreesCapacity()
        {
            using var host=new SeatDatagramHost(new IPEndPoint(IPAddress.Loopback,0));
            using var client=new SeatDatagramClient(host.LocalEndpoint);
            var view=new SeatView{Health=100,Match=new PublicMatchView{Round=1,Tick=8}};
            Assert.IsTrue(SpinWait.SpinUntil(()=>{host.Poll(view);client.Poll();return client.Connected;},2000));
            for(int i=0;i<SeatWire.InputWindow;i++) Assert.IsTrue(client.Submit(default));
            var driver=new PurchaseDriver();
            Assert.IsFalse(client.SubmitFrom(driver,view)); Assert.AreEqual(0,driver.Decisions);
            Assert.IsTrue(SpinWait.SpinUntil(()=>
            {
                host.Poll(view); host.Input.Decide(view); view.Match.Tick++;
                host.Publish(view); client.Poll(); return client.CanQueueInput;
            },2000));
            Assert.IsTrue(client.SubmitFrom(driver,view)); Assert.AreEqual(1,driver.Decisions);
        }

        [Test]
        public void ReorderedPurchasesRunOnSeparateTicksAndAckOnlyConsumedInputs()
        {
            var input = new HostSeatInput();
            var view = new SeatView { Match=new PublicMatchView { Round=1, Tick=50 } };
            input.ObserveRound(1, 50, .08f);
            var first = new SeatIntent { Buy=BuySlot.Primary, ItemId="weapon.ember47", Reload=true };
            var second = new SeatIntent { Buy=BuySlot.Armor };
            Assert.IsTrue(input.ReceiveOrdered(2, 50, 49, 0, second));
            Assert.AreEqual(BuySlot.None, input.Decide(view).Buy);
            Assert.Less(input.AppliedSequence, 1);
            Assert.IsTrue(input.ReceiveOrdered(1, 50, 48, 0, first));
            Assert.IsFalse(input.ReceiveOrdered(1, 50, 48, 0, first));
            var applied = input.Decide(view);
            Assert.AreEqual(first.ItemId, applied.ItemId);
            Assert.IsTrue(applied.Reload);
            Assert.AreEqual(1, input.AppliedSequence);
            view.Match.Tick++;
            applied = input.Decide(view);
            Assert.AreEqual(BuySlot.Armor, applied.Buy);
            Assert.IsFalse(applied.Reload);
            Assert.AreEqual(2, input.AppliedSequence);
            Assert.IsFalse(input.ReceiveOrdered(2, 50, 49, 0, second));
            view.Match.Tick++;
            Assert.AreEqual(BuySlot.None, input.Decide(view).Buy);
        }

        [Test]
        public void MissingOldestInputRecoversWithinBoundedWindowAndPendingActionsExpireBetweenRounds()
        {
            var input = new HostSeatInput();
            var view = new SeatView { Match=new PublicMatchView { Round=3, Tick=100 } };
            input.ObserveRound(3, 100, .08f);
            var move = new SeatIntent { Move=Vector2.right };
            for (int i=2; i<=SeatWire.InputWindow; i++) Assert.IsTrue(input.ReceiveOrdered(i, 100, 98, 0, move));
            Assert.IsFalse(input.ReceiveOrdered(SeatWire.InputWindow+1, 100, 98, 0, move));
            Assert.AreEqual(Vector2.zero, input.Decide(view).Move);
            Assert.IsTrue(input.ReceiveOrdered(1, 100, 98, 0, new SeatIntent { Jump=true }));
            Assert.IsTrue(input.Decide(view).Jump);
            view.Match.Tick++;
            Assert.AreEqual(Vector2.right, input.Decide(view).Move);
            Assert.AreEqual(SeatWire.InputWindow, input.AppliedSequence);
            Assert.IsTrue(input.ReceiveOrdered(SeatWire.InputWindow+1, 100, 98, 0, new SeatIntent { Reload=true }));
            view.Match.Round++;
            Assert.IsFalse(input.Decide(view).Reload);
            Assert.Less(input.AppliedSequence, 1);
            input.ObserveRound(view.Match.Round, view.Match.Tick, .08f);
            Assert.IsTrue(input.ReceiveOrdered(1, 101, 100, 0, move));
            Assert.AreEqual(Vector2.right, input.Decide(view).Move);
            Assert.AreEqual(1, input.AppliedSequence);
        }

        [Test]
        public void ReconnectToSameSeatPreservesOrderedCommandsAndAcknowledgements()
        {
            var view=new SeatView { Health=100, Match=new PublicMatchView { Round=1,Tick=30 } };
            using var host=new SeatDatagramHost(new IPEndPoint(IPAddress.Loopback,0));
            using var client=new SeatDatagramClient(host.LocalEndpoint);
            Assert.IsTrue(SpinWait.SpinUntil(()=>{host.Poll(view);client.Poll();return client.Connected;},2000));
            Assert.IsTrue(client.Submit(new SeatIntent { Buy=BuySlot.Armor }));
            host.Poll(view);
            Assert.AreEqual(BuySlot.Armor,host.Input.Decide(view).Buy);
            view.Match.Tick++;
            host.Publish(view);
            Assert.IsTrue(SpinWait.SpinUntil(()=>{client.Poll();return client.View.Match.Tick==view.Match.Tick;},2000));
            Assert.IsTrue(client.Submit(new SeatIntent { Buy=BuySlot.Helmet }));

            double recovery=SnapshotClock.Now+SeatWire.PeerTimeoutSeconds+.1;
            client.Poll(recovery);
            Assert.IsTrue(client.Reconnecting);
            Assert.IsFalse(client.Submit(new SeatIntent { Reload=true }));
            host.Poll(view); // The server still owns this token and may already have applied the second command.
            Assert.AreEqual(BuySlot.Helmet,host.Input.Decide(view).Buy);
            view.Match.Tick++;
            host.Publish(view);
            Assert.IsTrue(SpinWait.SpinUntil(()=>{client.Poll(recovery+.1);return client.Connected;},2000));
            Assert.IsTrue(client.Submit(new SeatIntent { Reload=true }));
            host.Poll(view);
            var next=host.Input.Decide(view);
            Assert.AreEqual(3,host.Input.AppliedSequence);
            Assert.IsTrue(next.Reload);
            Assert.AreEqual(BuySlot.None,next.Buy);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ReconnectWithNewTokenOrRoundDropsOldPurchaseAndRestartsSequence(bool replaceBinding)
        {
            var view=new SeatView { Health=100, Match=new PublicMatchView { Round=1,Tick=30 } };
            var host=new SeatDatagramHost(new IPEndPoint(IPAddress.Loopback,0));
            try
            {
                var endpoint=host.LocalEndpoint;
                using var client=new SeatDatagramClient(endpoint);
                Assert.IsTrue(SpinWait.SpinUntil(()=>{host.Poll(view);client.Poll();return client.Connected;},2000));
                Assert.IsTrue(client.Submit(new SeatIntent { Buy=BuySlot.Armor }));
                // The next authority never applied this outstanding order.
                view.Match.Tick++;
                if (replaceBinding) { host.Dispose();host=new SeatDatagramHost(endpoint); }
                else view.Match.Round++;
                double recovery=SnapshotClock.Now+SeatWire.PeerTimeoutSeconds+.1;
                client.Poll(recovery);
                Assert.IsTrue(client.Reconnecting);
                host.Poll(view);
                Assert.IsTrue(SpinWait.SpinUntil(()=>{client.Poll(recovery+.1);return client.Connected;},2000));
                host.Poll(view);
                Assert.AreEqual(BuySlot.None,host.Input.Decide(view).Buy);
                Assert.IsTrue(client.Submit(new SeatIntent { Buy=BuySlot.Helmet }));
                host.Poll(view);
                Assert.AreEqual(BuySlot.Helmet,host.Input.Decide(view).Buy);
                Assert.AreEqual(1,host.Input.AppliedSequence);
            }
            finally { host.Dispose(); }
        }

        [Test]
        public void CurrentRoomCanRebindWithRenewedTicketAfterEntryTicketExpires()
        {
            string secret=Guid.NewGuid().ToString("N"),room=Guid.NewGuid().ToString("N");
            long expiry=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+1;
            string Ticket(long until) => BattleTicket.Issue(secret,room,1,until,0,"local-seat","map.dust2","1");
            var view=new SeatView { Health=100,Match=new PublicMatchView { Round=1,Tick=30 } };
            var host=new SeatDatagramHost(new IPEndPoint(IPAddress.Loopback,0),1,secret);
            try
            {
                var endpoint=host.LocalEndpoint;
                host.SetContent("map.dust2","1");
                using var client=new SeatDatagramClient(endpoint,null,Ticket(expiry));
                Assert.IsTrue(SpinWait.SpinUntil(()=>{host.Poll(view);client.Poll();return client.Connected;},2000));
                Assert.IsTrue(SpinWait.SpinUntil(()=>DateTimeOffset.UtcNow.ToUnixTimeSeconds()>expiry,3000));
                host.Dispose();
                host=new SeatDatagramHost(endpoint,1,secret);
                host.SetContent("map.dust2","1");
                view.Match.Tick++;
                double recovery=SnapshotClock.Now+SeatWire.PeerTimeoutSeconds+.1;
                client.Poll(recovery);
                host.Poll(view);
                Assert.IsFalse(host.Connected);
                Assert.IsTrue(client.Reconnecting);
                client.RenewTicket(Ticket(DateTimeOffset.UtcNow.ToUnixTimeSeconds()+60));
                client.Poll(recovery+1);
                Assert.IsTrue(SpinWait.SpinUntil(()=>{host.Poll(view);client.Poll(recovery+1.1);return client.Connected;},2000));
                Assert.AreEqual(room,host.Room);
                Assert.IsTrue(client.Submit(new SeatIntent { Reload=true }));
                host.Poll(view);
                Assert.IsTrue(host.Input.Decide(view).Reload);
                Assert.AreEqual(1,host.Input.AppliedSequence);
            }
            finally { host.Dispose(); }
        }

        [Test]
        public void DuplicateLiveSnapshotCannotCancelReconnectOrExtendItsWindow()
        {
            var view=new SeatView { Health=100, Match=new PublicMatchView { Round=1,Tick=30 } };
            using var host=new SeatDatagramHost(new IPEndPoint(IPAddress.Loopback,0));
            using var client=new SeatDatagramClient(host.LocalEndpoint);
            Assert.IsTrue(SpinWait.SpinUntil(()=>{host.Poll(view);client.Poll();return client.Connected;},2000));
            double recovery=SnapshotClock.Now+SeatWire.PeerTimeoutSeconds+.1;
            client.Poll(recovery);
            host.Poll(view); // The HELLO reply has exactly the last live simulation tick.
            host.Publish(view);
            client.Poll(recovery+SeatDatagramClient.ReconnectWindowSeconds-.1);
            Assert.IsTrue(client.Reconnecting);
            Assert.IsFalse(client.Connected);
            host.Publish(view);
            client.Poll(recovery+SeatDatagramClient.ReconnectWindowSeconds+.1);
            Assert.IsFalse(client.Reconnecting);
            Assert.IsFalse(client.Connected);
        }

        [Test]
        public void MissingAuthorityExhaustsReconnectWindowAndKeepsLastConfirmedView()
        {
            var view=new SeatView { Health=100, Match=new PublicMatchView { Round=1,Tick=30 } };
            using var host=new SeatDatagramHost(new IPEndPoint(IPAddress.Loopback,0));
            using var client=new SeatDatagramClient(host.LocalEndpoint);
            Assert.IsTrue(SpinWait.SpinUntil(()=>{host.Poll(view);client.Poll();return client.Connected;},2000));
            double recovery=SnapshotClock.Now+SeatWire.PeerTimeoutSeconds+.1;
            client.Poll(recovery);
            Assert.IsTrue(client.Reconnecting);
            Assert.IsFalse(client.Connected);
            client.Poll(recovery+SeatDatagramClient.ReconnectWindowSeconds+.1);
            Assert.IsFalse(client.Reconnecting);
            Assert.IsFalse(client.Connected);
            host.Poll(view);
            client.Poll(recovery+SeatDatagramClient.ReconnectWindowSeconds+1);
            Assert.IsFalse(client.Connected);
            Assert.AreEqual(view.Match.Tick,client.View.Match.Tick);
            Assert.IsFalse(client.Finished);
        }

        [Test]
        public void FinalAtSameTickKeepsConnectionAliveAndSurvivesRoomRemoval()
        {
            using var host = new SeatDatagramHost(new IPEndPoint(IPAddress.Loopback,0));
            using var client = new SeatDatagramClient(host.LocalEndpoint);
            var view = new SeatView { Match=new PublicMatchView { Round=25, Tick=420, Phase=RoundPhase.Settlement } };
            Assert.IsTrue(SpinWait.SpinUntil(() => { host.Poll(view); client.Poll(); return client.Connected; },2000));
            view.Match.Phase = RoundPhase.Finished;
            view.Match.Score0 = 13; view.Match.Score1 = 12;
            host.Publish(view);
            Assert.IsTrue(SpinWait.SpinUntil(() => { client.Poll(); return client.Finished; },2000));
            double now = SnapshotClock.Now;
            host.Publish(view);
            client.Poll(now+4);
            client.Poll(now+7);
            Assert.IsTrue(client.Connected);
            client.Poll(now+10);
            Assert.IsFalse(client.Connected);
            Assert.IsTrue(client.Finished);
            Assert.AreEqual(13,client.View.Match.Score0);
            Assert.AreEqual(12,client.View.Match.Score1);
            Assert.IsFalse(client.Submit(new SeatIntent { Jump=true }));
        }

        [Test]
        public void ClientRetriesUnconfirmedPurchaseAgainstLatestSnapshotWithoutNewInput()
        {
            using var host = new SeatDatagramHost(new IPEndPoint(IPAddress.Loopback,0));
            using var relay = new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);
            relay.Bind(new IPEndPoint(IPAddress.Loopback,0));
            using var client = new SeatDatagramClient((IPEndPoint)relay.LocalEndPoint);
            var view = new SeatView { Match=new PublicMatchView { Round=1, Tick=8 } };
            var packet = new byte[2048];
            EndPoint clientAddress = null;
            bool dropFirst = true;
            void Pump()
            {
                for (int i=0; i<64 && relay.Poll(0,SelectMode.SelectRead); i++)
                {
                    EndPoint sender = new IPEndPoint(IPAddress.Any,0);
                    int size = relay.ReceiveFrom(packet,ref sender);
                    if (sender.Equals(host.LocalEndpoint))
                        relay.SendTo(packet,0,size,SocketFlags.None,clientAddress);
                    else
                    {
                        clientAddress = sender;
                        if (dropFirst && packet[4] == SeatWire.OrderedInput && SeatWire.I32(packet,21)==1) continue;
                        relay.SendTo(packet,0,size,SocketFlags.None,host.LocalEndpoint);
                    }
                }
            }
            Assert.IsTrue(SpinWait.SpinUntil(() =>
            {
                Pump(); host.Poll(view); Pump(); client.Poll();
                return client.Connected;
            },2000));
            Assert.IsTrue(client.Submit(new SeatIntent { Buy=BuySlot.Primary, ItemId="weapon.ember47" }));
            Assert.IsTrue(client.Submit(new SeatIntent { Buy=BuySlot.Armor }));
            Assert.IsTrue(SpinWait.SpinUntil(() => { Pump(); host.Poll(view); return host.Input.AcceptedSequence==2; },2000));
            Assert.AreEqual(BuySlot.None,host.Input.Decide(view).Buy);
            // The original ACK is no longer in the authority's 64-snapshot ring.
            view.Match.Tick += 64;
            host.Poll(view); host.Publish(view);
            Assert.IsTrue(SpinWait.SpinUntil(() => { Pump(); client.Poll(); return client.View.Match.Tick==view.Match.Tick; },2000));
            dropFirst = false;
            client.Poll(SnapshotClock.Now+.25);
            Assert.IsTrue(SpinWait.SpinUntil(() =>
            {
                Pump(); host.Poll(view);
                return host.Input.Decide(view).Buy==BuySlot.Primary;
            },2000));
            Assert.AreEqual(1,host.Input.AppliedSequence);
            view.Match.Tick++;
            Assert.AreEqual(BuySlot.Armor,host.Input.Decide(view).Buy);
            Assert.AreEqual(2,host.Input.AppliedSequence);
        }

        [Test]
        public void RewindUsesSubtickWithoutCrossingARespawn()
        {
            var h = new HitboxHistory(50,2);
            void Record(int tick, float x)
            {
                h.Record(tick,1,new Vector3(x,2,5),new Vector3(x,1,5),1.5f,true);
            }
            Record(100,-1); Record(101,1);
            var ray = new Ray(Vector3.up * 2,Vector3.forward);
            Assert.IsFalse(h.Trace(101,100,0,0,ray,20,out _,out _,out _));
            Assert.IsTrue(h.Trace(101,100,32768,0,ray,20,out int seat,out bool head,out _));
            Assert.AreEqual(1,seat); Assert.IsTrue(head);
            h.Invalidate(1); Record(101,1);
            Assert.IsFalse(h.Trace(101,100,32768,0,ray,20,out _,out _,out _));
        }

        [Test]
        public void PlaybackClockIsMonotonicAndFreezesAtThePredictionLimit()
        {
            var clock = new SnapshotClock();
            clock.Observe(100,0);
            Assert.AreEqual(100,clock.Sample(0));
            clock.Observe(102,.04);
            double previous = clock.Sample(.07);
            int predicted = clock.InputTick(.13);
            clock.Observe(104,.14);
            Assert.GreaterOrEqual(clock.Sample(.14),previous);
            Assert.GreaterOrEqual(clock.InputTick(.14),predicted);
            Assert.AreEqual(106d,clock.Sample(10));
            clock.Reset(); clock.Observe(1,12);
            Assert.AreEqual(1d,clock.Sample(12));
            Assert.AreEqual(2,clock.InputTick(12));
        }

        [Test]
        public void AuthoritativePositionReplaysOnlyUnconfirmedMovement()
        {
            var prediction = new SeatMotionPrediction(null);
            var state = new SeatView { Health = 100, Motion = MotionContact.Standing, PredictionSpeed = 6 };
            prediction.Reconcile(state,0);
            var move = new SeatIntent { Move = Vector2.right };
            prediction.Record(11,1,move);
            float oneStep = prediction.Position.x;
            Assert.Greater(oneStep,0);
            prediction.Record(11,2,move);
            Assert.AreEqual(oneStep,prediction.Position.x);
            prediction.Record(12,3,move);
            Assert.Greater(prediction.Position.x,oneStep);
            state.Position = new Vector3(5,2,3);
            prediction.Reconcile(state,2);
            Assert.That(prediction.Position.x,Is.EqualTo(5 + oneStep).Within(.0001f));
            prediction.Reconcile(state,3);
            Assert.AreEqual(state.Position,prediction.Position);
            state.PredictionSpeed=0;
            prediction.Reconcile(state,3);
            prediction.Record(13,4,move);
            Assert.AreEqual(state.Position,prediction.Position);
        }

        [Test]
        public void TwoClientsReceiveTheirOwnSeatAndSubmitIndependently()
        {
            using var host = new SeatDatagramHost(new IPEndPoint(IPAddress.Loopback,0),2);
            using var a = new SeatDatagramClient(host.LocalEndpoint);
            using var b = new SeatDatagramClient(host.LocalEndpoint);
            var views = new SeatView[2];
            for (int i=0; i<2; i++)
                views[i] = new SeatView { Seat=i, Credits=900+i, Health=100, Match=new PublicMatchView { Round=1,Tick=8 } };
            Assert.IsTrue(SpinWait.SpinUntil(() => { host.Poll(views); a.Poll(); b.Poll(); return a.Connected && b.Connected; },2000));
            Assert.AreNotEqual(a.View.Seat,b.View.Seat);
            Assert.AreEqual(900+a.View.Seat,a.View.Credits);
            a.Submit(new SeatIntent { Reload=true }); b.Submit(new SeatIntent { Jump=true });
            Assert.IsTrue(SpinWait.SpinUntil(() =>
            {
                host.Poll(views);
                return host.Driver(a.View.Seat).AcceptedSequence == 1 && host.Driver(b.View.Seat).AcceptedSequence == 1;
            },2000));
            Assert.IsTrue(host.Driver(a.View.Seat).Decide(a.View).Reload);
            Assert.IsTrue(host.Driver(b.View.Seat).Decide(b.View).Jump);
        }
    }
}
