using System;
using System.Net;
using System.Threading;
using AnimeCS.Match;
using NUnit.Framework;
using UnityEngine;

namespace AnimeCS.Tests
{
    public class NetworkTimingTests
    {
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
