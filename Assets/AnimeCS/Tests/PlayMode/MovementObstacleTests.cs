using System.Collections;
using AnimeCS.Content;
using AnimeCS.Match;
using AnimeCS.Play;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.TestTools;

namespace AnimeCS.Tests
{
    public sealed class MovementObstacleTests
    {
        private const float BoxTop = .96f;
        private const float ApproachSpeed = 3.2f;

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator RifleMovementCanCrouchJumpOrTuckAfterRunningOntoTheBox()
        {
            var rifle = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/AnimeCS/Content/Weapons/ember47.asset");
            var rules = AssetDatabase.LoadAssetAtPath<MatchRules>("Assets/AnimeCS/Content/MatchRules.asset");
            Assert.That(rifle, Is.Not.Null);
            Assert.That(rules, Is.Not.Null);
            float runSpeed = rifle.RunSpeed;
            var tuning = rules.movement;
            var room = new GameObject("Rifle crouch jump integration");
            room.transform.position = new Vector3(1190f, 104f, 1180f);
            try
            {
                AddBlock(room.transform, new Vector3(0f, -.25f, 0f), new Vector3(16f, .5f, 8f));
                AddBlock(room.transform, new Vector3(2.5f, BoxTop * .5f, 0f), new Vector3(5f, BoxTop, 5f));
                var pawn = AddPawn(room.transform);
                pawn.Tuning = tuning;
                foreach (bool crouchBeforeJump in new[] { true, false })
                {
                    float spawnX = crouchBeforeJump ? -1.6f : -3.2f;
                    pawn.Spawn(room.transform.position + new Vector3(spawnX, .035f, 0f), 90f);
                    Physics.SyncTransforms();
                    var contact = MotionContact.Standing;
                    bool settled = false;
                    for (int tick = 0; tick < 12; tick++) Advance(Vector2.zero, false, crouchBeforeJump);

                    float takeoffDistance = crouchBeforeJump ? .66f : 1.16f;
                    float Gap() => room.transform.position.x - pawn.transform.position.x;
                    for (int tick = 0; tick < 65 && Gap() > takeoffDistance; tick++)
                        Advance(Vector2.right, false, crouchBeforeJump);
                    Assert.That(Gap(), Is.InRange(takeoffDistance - runSpeed * SeatMovement.StepTime, takeoffDistance));
                    Assert.That(contact.Grounded, Is.True);
                    float takeoffSpeed = crouchBeforeJump ? runSpeed * tuning.duckFraction : runSpeed;
                    Assert.That(contact.Velocity.x, Is.EqualTo(takeoffSpeed).Within(.02f));

                    bool leftGround = false;
                    bool landedOnBox = false;
                    for (int tick = 0; tick < 55; tick++)
                    {
                        var direction = leftGround && contact.Grounded ? Vector2.zero : Vector2.right;
                        Advance(direction, tick == 0, crouchBeforeJump || tick >= 4);
                        leftGround |= !contact.Grounded;
                        landedOnBox |= leftGround && contact.Grounded && pawn.transform.position.y - room.transform.position.y > .9f;
                    }
                    string sequence = crouchBeforeJump ? "Crouch, then jump" : "Run, jump, then crouch";
                    Assert.That(leftGround && contact.Grounded && landedOnBox, Is.True, sequence);
                    Vector3 feet = pawn.transform.position - room.transform.position;
                    Assert.That(feet.y, Is.EqualTo(BoxTop).Within(.055f), sequence);
                    Assert.That(feet.x, Is.InRange(.20f, 4.7f), sequence);
                    Assert.That(contact.Velocity.magnitude, Is.LessThan(.02f), sequence);

                    void Advance(Vector2 move, bool jump, bool crouch)
                    {
                        var intent = new SeatIntent { Move = move, Yaw = 90f, Jump = jump, Crouch = crouch };
                        var velocity = SeatMovement.Advance(contact, intent, runSpeed, ref settled, tuning);
                        contact = pawn.Move(velocity, intent.Yaw, intent.Pitch, intent.Jump && contact.Grounded, intent.Crouch);
                        Physics.SyncTransforms();
                    }
                }
            }
            finally { Object.Destroy(room); }
            yield return null;
        }
#endif

        [UnityTest]
        public IEnumerator BothCrouchJumpOrdersLandOnABoxAboveTheRegularJump()
        {
            var room = new GameObject("Crouch jump box test");
            room.transform.position = new Vector3(1130f, 103f, 1180f);
            try
            {
                AddBlock(room.transform, new Vector3(0f, -.25f, 0f), new Vector3(12f, .5f, 8f));
                AddBlock(room.transform, new Vector3(1.82f, BoxTop * .5f, 0f), new Vector3(2f, BoxTop, 5f));
                var pawn = AddPawn(room.transform);

                for (int order = 0; order < 3; order++)
                {
                    pawn.Spawn(room.transform.position + Vector3.up * .035f, 90f);
                    Physics.SyncTransforms();
                    MotionContact state = default;
                    for (int tick = 0; tick < 12; tick++)
                        state = Step(pawn, 0f, false, order == 1);
                    Assert.That(state.Grounded, Is.True);

                    bool leftGround = false;
                    for (int tick = 0; tick < 55; tick++)
                    {
                        bool crouch = order == 1 || order == 2 && tick >= 4;
                        state = Step(pawn, tick < 23 ? ApproachSpeed : 0f, tick == 0, crouch);
                        leftGround |= !state.Grounded;
                    }

                    Assert.That(leftGround && state.Grounded, Is.True, $"Order {order} should take off and finish grounded.");
                    var feet = pawn.transform.position - room.transform.position;
                    if (order == 0)
                    {
                        Assert.That(feet.y, Is.LessThan(.10f));
                        Assert.That(feet.x, Is.LessThan(.82f));
                    }
                    else
                    {
                        Assert.That(feet.y, Is.EqualTo(BoxTop).Within(.055f), $"Order {order} must land on the box.");
                        Assert.That(feet.x, Is.InRange(.90f, 2.65f));
                    }
                }
            }
            finally { Object.Destroy(room); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReleasingTheTuckUnderARestrictedCeilingWaitsForClearance()
        {
            var room = new GameObject("Crouch jump clearance test");
            room.transform.position = new Vector3(1150f, 104f, 1180f);
            try
            {
                AddBlock(room.transform, new Vector3(0f, -.25f, 0f), new Vector3(12f, .5f, 8f));
                AddBlock(room.transform, new Vector3(3.31f, BoxTop * .5f, 0f), new Vector3(4.98f, BoxTop, 5f));
                const float clearance = 1.45f;
                AddBlock(room.transform, new Vector3(2.51f, BoxTop + clearance + .15f, 0f),
                    new Vector3(1.2f, .30f, 3f));
                var pawn = AddPawn(room.transform);
                pawn.Spawn(room.transform.position + Vector3.up * .035f, 90f);
                Physics.SyncTransforms();

                for (int tick = 0; tick < 10; tick++) Step(pawn, 0f, false, true);
                for (int tick = 0; tick < 52; tick++)
                    Step(pawn, tick < 23 ? ApproachSpeed : 0f, tick == 0, true);
                MotionContact state = default;
                for (int tick = 0; tick < 18; tick++)
                    state = Step(pawn, ApproachSpeed, false, true);
                Assert.That(state.Grounded, Is.True);
                Assert.That(pawn.transform.position.x - room.transform.position.x, Is.InRange(2f, 2.9f));

                for (int tick = 0; tick < 18; tick++)
                {
                    state = Step(pawn, 0f, false, false);
                    float feet = pawn.transform.position.y - room.transform.position.y;
                    Assert.That(feet + pawn.Height, Is.LessThanOrEqualTo(BoxTop + clearance + .035f));
                }
                Assert.That(state.Grounded, Is.True);
                Assert.That(pawn.Height, Is.LessThan(SeatMovement.StandingHeight - .15f));

                for (int tick = 0; tick < 30; tick++)
                    state = Step(pawn, ApproachSpeed, false, false);
                Assert.That(state.Grounded, Is.True);
                Assert.That(pawn.transform.position.y - room.transform.position.y, Is.EqualTo(BoxTop).Within(.055f));
                Assert.That(pawn.Height, Is.EqualTo(SeatMovement.StandingHeight).Within(.02f));
            }
            finally { Object.Destroy(room); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator RetuckingDuringOneJumpDoesNotRaiseItsPeakAgain()
        {
            var room = new GameObject("Repeated tuck test");
            room.transform.position = new Vector3(1170f, 104f, 1180f);
            try
            {
                AddBlock(room.transform, new Vector3(0f, -.25f, 0f), new Vector3(12f, .5f, 8f));
                var pawn = AddPawn(room.transform);
                float singlePeak = Peak(false);
                float repeatedPeak = Peak(true);
                Assert.That(repeatedPeak, Is.LessThanOrEqualTo(singlePeak + .04f));

                float Peak(bool repeated)
                {
                    pawn.Spawn(room.transform.position + Vector3.up * .035f, 90f);
                    Physics.SyncTransforms();
                    for (int tick = 0; tick < 6; tick++) Step(pawn, 0f, false, false);
                    float startingHeight = pawn.transform.position.y;
                    float peak = 0f;
                    MotionContact contact = default;
                    for (int tick = 0; tick < 65; tick++)
                    {
                        bool crouch = tick >= 3 && (!repeated || (tick - 3) % 6 < 3);
                        contact = Step(pawn, 0f, tick == 0, crouch);
                        peak = Mathf.Max(peak, pawn.transform.position.y - startingHeight);
                    }
                    Assert.That(contact.Grounded, Is.True);
                    Assert.That(pawn.transform.position.y - startingHeight, Is.EqualTo(0f).Within(.045f));
                    return peak;
                }
            }
            finally { Object.Destroy(room); }
            yield return null;
        }

        [Test]
        public void OneReversePulseEntersTheSteadyBandEarlierWithoutReverseTravel()
        {
            const float speed = 6f;
            var coast = MotionContact.Standing;
            coast.Velocity = Vector2.right * speed;
            var tap = coast;
            bool stoppedCoasting = false;
            bool stoppedTapping = false;
            int coastSteady = 0;
            int tapSteady = 0;

            for (int tick = 1; tick <= 24; tick++)
            {
                var pressed = tick == 1 ? new SeatIntent { BrakeTap = Vector2.left } : default;
                coast.Velocity = SeatMovement.Advance(coast, default, speed, ref stoppedCoasting);
                tap.Velocity = SeatMovement.Advance(tap, pressed, speed, ref stoppedTapping);
                Assert.That(tap.Velocity.x, Is.GreaterThanOrEqualTo(0f));
                if (coastSteady == 0 && coast.Velocity.x <= speed * .34f) coastSteady = tick;
                if (tapSteady == 0 && tap.Velocity.x <= speed * .34f) tapSteady = tick;
            }

            Assert.That(tapSteady, Is.GreaterThan(0));
            Assert.That(coastSteady, Is.GreaterThan(tapSteady));
            Assert.That(tap.Velocity, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void SustainedAirTurnsChangeDirectionWithoutExceedingTheConfiguredCap()
        {
            const float runSpeed = 6f;
            var tuning = new MovementTuning();
            var state = new MotionContact { Velocity = Vector2.up * runSpeed };
            bool stopped = false;
            float angleTravel = 0f;
            for (int tick = 0; tick < 240; tick++)
            {
                var before = state.Velocity;
                var wish = new Vector2(before.y, -before.x).normalized;
                state.Velocity = SeatMovement.Advance(state, new SeatIntent { Move = wish, Yaw = tick * 4f },
                    runSpeed, ref stopped, tuning);
                angleTravel += Vector2.Angle(before, state.Velocity);
                Assert.That(state.Velocity.magnitude, Is.LessThanOrEqualTo(runSpeed * tuning.airCap + .001f));
            }

            Assert.That(angleTravel, Is.GreaterThan(45f));
        }

        private static MotionContact Step(ArenaPawn pawn, float speed, bool jump, bool crouch)
        {
            var contact = pawn.Move(new Vector2(speed, 0f), 90f, 0f, jump, crouch);
            Physics.SyncTransforms();
            return contact;
        }

        private static ArenaPawn AddPawn(Transform room)
        {
            var player = new GameObject("Test seat");
            player.transform.SetParent(room, false);
            return player.AddComponent<ArenaPawn>();
        }

        private static void AddBlock(Transform room, Vector3 position, Vector3 size)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.transform.SetParent(room, false);
            block.transform.localPosition = position;
            block.transform.localScale = size;
        }
    }
}
