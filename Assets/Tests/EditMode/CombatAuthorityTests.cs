using System.Net;
using System.Threading;
using AnimeCS.Match;
using NUnit.Framework;
using UnityEngine;

namespace AnimeCS.Tests
{
    public sealed class CombatAuthorityTests
    {
        private const int TickRate = 50;
        private static readonly Ray HeadRay = new Ray(new Vector3(0f, 1.6f, 0f), Vector3.forward);
        private static readonly Ray BodyRay = new Ray(new Vector3(0f, .7f, 0f), Vector3.forward);

        private static void Record(HitboxHistory history, int tick, int seat, float x, float z, bool alive = true)
        {
            history.Record(tick, seat, new Vector3(x, 1.6f, z), new Vector3(x, .75f, z), 1.5f, alive);
        }

        [Test]
        public void TracePicksTheNearestSeatAndMeasuresTheHitboxSurface()
        {
            var history = new HitboxHistory(TickRate, 4);
            Record(history, 50, 0, 0f, 1f);
            Record(history, 50, 1, 0f, 8f);
            Record(history, 50, 2, 0f, 4f);

            Assert.That(history.Trace(50, 50, 0, HeadRay, 20f, out int target, out bool headshot, out float distance), Is.True);
            Assert.That(target, Is.EqualTo(2));
            Assert.That(headshot, Is.True);
            Assert.That(distance, Is.EqualTo(3.85f).Within(.002f));

            Assert.That(history.Trace(50, 50, 0, BodyRay, 20f, out target, out headshot, out distance), Is.True);
            Assert.That(target, Is.EqualTo(2));
            Assert.That(headshot, Is.False);
            Assert.That(distance, Is.EqualTo(3.74f).Within(.002f));
            Assert.That(history.Trace(50, 50, 0, BodyRay, 3.7f, out _, out _, out _), Is.False);
        }

        [Test]
        public void TraceClampsTheRequestedTickAtBothEndsOfTheRewindWindow()
        {
            var history = new HitboxHistory(TickRate, 3);
            Record(history, 89, 1, 0f, 2f);
            Record(history, 90, 1, 0f, 4f);
            Record(history, 100, 1, 2f, 4f);
            Record(history, 100, 2, 0f, 8f);

            Assert.That(history.Trace(100, 20, 0, HeadRay, 20f, out int target, out _, out float distance), Is.True);
            Assert.That(target, Is.EqualTo(1));
            Assert.That(distance, Is.EqualTo(3.85f).Within(.002f));

            Assert.That(history.Trace(100, 120, 0, HeadRay, 20f, out target, out _, out distance), Is.True);
            Assert.That(target, Is.EqualTo(2));
            Assert.That(distance, Is.EqualTo(7.85f).Within(.002f));

            Assert.That(history.Trace(100, 99, 0, HeadRay, 20f, out _, out _, out _), Is.False);
        }

        [Test]
        public void TraceRejectsEarlierLivesAfterDeathAndRespawn()
        {
            var history = new HitboxHistory(TickRate, 2);
            Record(history, 40, 1, 0f, 4f);
            Assert.That(history.Trace(40, 40, 0, HeadRay, 20f, out _, out _, out _), Is.True);

            Record(history, 41, 1, 0f, 4f, alive: false);
            Assert.That(history.Trace(41, 40, 0, HeadRay, 20f, out _, out _, out _), Is.False);

            Record(history, 42, 1, 0f, 8f);
            Assert.That(history.Trace(42, 40, 0, HeadRay, 20f, out _, out _, out _), Is.False);
            Assert.That(history.Trace(42, 41, 0, HeadRay, 20f, out _, out _, out _), Is.False);
            Assert.That(history.Trace(42, 42, 0, HeadRay, 20f, out int target, out _, out float distance), Is.True);
            Assert.That(target, Is.EqualTo(1));
            Assert.That(distance, Is.EqualTo(7.85f).Within(.002f));
        }

        [Test]
        public void ShotPatternDependsOnlyOnTheShotIdentityAndSpread()
        {
            const int matchSeed = 731;
            var first = ShotPattern.Sample(matchSeed, 0, 5, 3f);
            Assert.That(ShotPattern.Sample(matchSeed, 1, 5, 3f), Is.Not.EqualTo(first));
            Assert.That(ShotPattern.Sample(matchSeed, 0, 6, 3f), Is.Not.EqualTo(first));
            Assert.That(ShotPattern.Sample(matchSeed + 1, 0, 5, 3f), Is.Not.EqualTo(first));
            Assert.That(ShotPattern.Sample(matchSeed, 0, 5, 3f), Is.EqualTo(first));
            Assert.That(ShotPattern.Sample(matchSeed, 0, 5, 0f), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void ShotPatternSamplesReachAllQuadrantsWithinTheSpreadDisk()
        {
            const float spread = 3.25f;
            int quadrants = 0;
            float maxRadius = 0f;
            for (int sequence = 1; sequence <= 256; sequence++)
            {
                Vector2 offset = ShotPattern.Sample(409, 3, sequence, spread);
                float radius = offset.magnitude;
                Assert.That(radius, Is.InRange(0f, spread + .0001f));
                maxRadius = Mathf.Max(maxRadius, radius);
                int quadrant = (offset.x < 0f ? 1 : 0) + (offset.y < 0f ? 2 : 0);
                quadrants |= 1 << quadrant;
            }

            Assert.That(quadrants, Is.EqualTo(0b1111));
            Assert.That(maxRadius, Is.GreaterThan(spread * .85f));
        }

        [Test]
        public void SnapshotSamplingInterpolatesAcrossGapsAndWrapsYaw()
        {
            var buffer = new SeatSnapshotBuffer();
            Assert.That(buffer.Sample(10f, out _, out _, out _), Is.False);
            Assert.That(buffer.Push(10, Vector3.zero, 350f, -10f), Is.True);
            Assert.That(buffer.Push(14, new Vector3(4f, 0f, 0f), 10f, 6f), Is.True);
            Assert.That(buffer.Push(14, Vector3.one, 99f, 99f), Is.False);
            Assert.That(buffer.Push(12, Vector3.one, 99f, 99f), Is.False);

            Assert.That(buffer.Sample(12f, out Vector3 position, out float yaw, out float pitch), Is.True);
            Assert.That(position, Is.EqualTo(new Vector3(2f, 0f, 0f)));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)), Is.LessThan(.001f));
            Assert.That(pitch, Is.EqualTo(-2f).Within(.001f));

            Assert.That(buffer.Sample(14f, out position, out yaw, out pitch), Is.True);
            Assert.That(position, Is.EqualTo(new Vector3(4f, 0f, 0f)));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(yaw, 10f)), Is.LessThan(.001f));
            Assert.That(pitch, Is.EqualTo(6f).Within(.001f));
        }

        [Test]
        public void SnapshotSamplingLimitsExtrapolationToTwoTicks()
        {
            var buffer = new SeatSnapshotBuffer();
            Assert.That(buffer.Push(10, Vector3.zero, 0f, 0f), Is.True);
            Assert.That(buffer.Push(14, new Vector3(8f, 0f, 0f), 0f, 0f), Is.True);

            Assert.That(buffer.Sample(15f, out Vector3 next, out _, out _), Is.True);
            Assert.That(next.x, Is.EqualTo(10f).Within(.001f));
            Assert.That(buffer.Sample(16f, out Vector3 limit, out _, out _), Is.True);
            Assert.That(limit.x, Is.EqualTo(12f).Within(.001f));
            Assert.That(buffer.Sample(40f, out Vector3 late, out _, out _), Is.True);
            Assert.That(late, Is.EqualTo(limit));
        }

        [Test]
        public void HostInputConsumesEdgesOnceAndClearsHeldActionsAfterLoss()
        {
            var driver = new HostSeatInput();
            driver.ObserveRound(3, 100, .12f);
            var input = new SeatIntent
            {
                Move = Vector2.right,
                Yaw = 25f,
                Fire = true,
                FirePressed = true,
                Reload = true,
                Zoom = true
            };
            Assert.That(driver.Receive(7, 98, input), Is.True);
            Assert.That(driver.Receive(7, 98, default), Is.False);
            Assert.That(driver.Receive(6, 99, default), Is.False);

            var first = driver.Decide(ViewAt(3, 100));
            Assert.That(first.Move, Is.EqualTo(Vector2.right));
            Assert.That(first.Fire && first.FirePressed && first.Reload && first.Zoom, Is.True);

            driver.ObserveRound(3, 101, .12f);
            var afterLoss = driver.Decide(ViewAt(3, 101));
            Assert.That(afterLoss.Move, Is.EqualTo(Vector2.right));
            Assert.That(afterLoss.Fire, Is.True);
            Assert.That(afterLoss.FirePressed || afterLoss.Reload || afterLoss.Zoom, Is.False);

            for (int tick = 102; tick <= 120; tick++)
            {
                driver.ObserveRound(3, tick, .12f);
                afterLoss = driver.Decide(ViewAt(3, tick));
            }

            Assert.That(afterLoss.Move, Is.EqualTo(Vector2.zero));
            Assert.That(afterLoss.Fire, Is.False);
        }

        [Test]
        public void HostInputClampsAcknowledgedTicksUsingAuthorityTime()
        {
            var driver = new HostSeatInput();
            driver.ObserveRound(3, 100, 1f);
            var input = new SeatIntent { Fire = true, FirePressed = true, ObservedTick = 10 };
            Assert.That(driver.Receive(1, 99, input), Is.True);
            Assert.That(driver.Decide(ViewAt(3, 100)).ObservedTick, Is.EqualTo(99));

            driver.ObserveRound(3, 101, 1f);
            Assert.That(driver.Receive(2, 5, input), Is.True);
            Assert.That(driver.Decide(ViewAt(3, 101)).ObservedTick, Is.EqualTo(92));

            driver.ObserveRound(3, 102, 0f);
            Assert.That(driver.Receive(3, 999, input), Is.False);
            Assert.That(driver.Receive(3, 5, input), Is.True);
            Assert.That(driver.Decide(ViewAt(3, 102)).ObservedTick, Is.EqualTo(101));
        }

        [Test]
        public void DatagramRoundTripCarriesIntentToTheHostAndAuthorityBackToTheClient()
        {
            using var host = new SeatDatagramHost(new IPEndPoint(IPAddress.Loopback, 0));
            using var client = new SeatDatagramClient(host.LocalEndpoint);
            var authoritative = ViewAt(3, 100);
            authoritative.Position = new Vector3(-2.34f, 1.15f, 8.67f);
            authoritative.Yaw = 257.42f;
            authoritative.Pitch = -12.17f;
            authoritative.Magazine = 21;
            authoritative.Health = 72;

            Assert.That(SpinWait.SpinUntil(() =>
            {
                client.Poll();
                host.Poll(authoritative);
                host.Publish(authoritative);
                client.Poll();
                return client.Connected && client.View.Match.Tick == authoritative.Match.Tick;
            }, 2000), Is.True, "Loopback client did not receive the first authority snapshot.");
            Assert.That(Vector3.Distance(client.View.Position, authoritative.Position), Is.LessThanOrEqualTo(.015f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(client.View.Yaw, authoritative.Yaw)), Is.LessThanOrEqualTo(.02f));
            Assert.That(client.View.Pitch, Is.EqualTo(authoritative.Pitch).Within(.02f));
            Assert.That(client.View.Magazine, Is.EqualTo(21));
            Assert.That(client.View.Health, Is.EqualTo(72));

            Assert.That(client.Submit(new SeatIntent
            {
                Move = Vector2.right,
                Yaw = 158.34f,
                Pitch = 14.16f,
                Fire = true,
                FirePressed = true,
                ObservedTick = 1
            }), Is.True);
            SeatIntent received = default;
            Assert.That(SpinWait.SpinUntil(() =>
            {
                client.Poll();
                host.Poll(authoritative);
                received = host.Input.Decide(authoritative);
                return received.FirePressed;
            }, 2000), Is.True, "Loopback host did not receive the client's fire edge.");
            Assert.That(received.Move.x, Is.EqualTo(1f).Within(.01f));
            Assert.That(received.Move.y, Is.EqualTo(0f).Within(.01f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(received.Yaw, 158.34f)), Is.LessThanOrEqualTo(.02f));
            Assert.That(received.Pitch, Is.EqualTo(14.16f).Within(.02f));
            Assert.That(received.ObservedTick, Is.EqualTo(100));
            Assert.That(Vector3.Distance(client.View.Position, authoritative.Position), Is.LessThanOrEqualTo(.015f));
            Assert.That(client.View.Health, Is.EqualTo(72));
            Assert.That(client.View.Magazine, Is.EqualTo(21));

            var updated = authoritative;
            updated.Match.Tick = 101;
            updated.Position.z += .15f;
            updated.Magazine = 20;
            host.Publish(updated);
            host.Publish(authoritative);
            Assert.That(SpinWait.SpinUntil(() =>
            {
                client.Poll();
                return client.View.Match.Tick == 101;
            }, 2000), Is.True, "Loopback client did not advance to the new authority snapshot.");
            Assert.That(Vector3.Distance(client.View.Position, updated.Position), Is.LessThanOrEqualTo(.015f));
            Assert.That(client.View.Magazine, Is.EqualTo(20));
        }

        private static SeatView ViewAt(int round, int tick) => new SeatView
        {
            Seat = 1,
            Health = 100,
            Match = new PublicMatchView { Round = round, Tick = tick, Phase = RoundPhase.Contest }
        };
    }
}
