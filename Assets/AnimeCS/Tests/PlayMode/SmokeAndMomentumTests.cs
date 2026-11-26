using System.Collections;
using AnimeCS.Content;
using AnimeCS.Match;
using AnimeCS.Play;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AnimeCS.Tests
{
    public sealed class SmokeAndMomentumTests
    {
        [UnityTest]
        public IEnumerator SmokeBulletChannelClearsOnlyItsPathAndClosesAgain()
        {
            const float radius = 3f;
            var root = new GameObject("Smoke channel test");
            root.transform.position = new Vector3(1250f, 102f, 1170f);
            var smoke = new SmokeField(root.transform.position, radius, 159, root.transform, null);
            try
            {
                Vector3 center = root.transform.position + Vector3.up * (radius * .40f);
                Vector3 entry = center - Vector3.forward * 5f;
                Vector3 exit = center + Vector3.forward * 5f;
                Vector3 adjacent = Vector3.right;
                smoke.Advance(1f, 6f);
                Assert.That(smoke.Occludes(entry, exit), Is.True);
                Assert.That(smoke.Occludes(entry + adjacent, exit + adjacent), Is.True);

                smoke.Punch(entry, exit);
                Assert.That(smoke.Occludes(entry, exit), Is.False);
                Assert.That(smoke.Occludes(entry + adjacent, exit + adjacent), Is.True);

                smoke.Advance(1.6f, 5.4f);
                Assert.That(smoke.Occludes(entry, exit), Is.True);
            }
            finally
            {
                smoke.Dispose();
                Object.Destroy(root);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThrownAndDroppedUtilitiesInheritMovementAndRisingMomentum()
        {
            var root = new GameObject("Utility momentum test");
            root.transform.position = new Vector3(1290f, 102f, 1170f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = Vector3.down * .25f;
            floor.transform.localScale = new Vector3(40f, .5f, 45f);
            var actor = new GameObject("Test seat");
            actor.transform.SetParent(root.transform, false);
            var pawn = actor.AddComponent<ArenaPawn>();
            var effects = new GameObject("Flight effects").transform;
            effects.SetParent(root.transform, false);
            var utility = ScriptableObject.CreateInstance<UtilityDefinition>();
            utility.name = "Test utility";
            var map = ScriptableObject.CreateInstance<MapDefinition>();
            var world = new ArenaWorld(new[] { pawn }, map, effects);
            try
            {
                var stationary = Observe(Vector2.zero, false);
                var walking = Observe(Vector2.right * 4f, false);
                var jumping = Observe(Vector2.right * 4f, true);
                Assert.That(walking.landing.x - stationary.landing.x, Is.GreaterThan(1f));
                Assert.That(walking.landing.z, Is.EqualTo(stationary.landing.z).Within(.15f));
                Assert.That(jumping.landing.x - walking.landing.x, Is.GreaterThan(1f));
                Assert.That(jumping.landing.z - walking.landing.z, Is.GreaterThan(1f));
                Assert.That(walking.dropOffset.x - stationary.dropOffset.x, Is.GreaterThan(.5f));
                Assert.That(walking.dropOffset.z, Is.EqualTo(stationary.dropOffset.z).Within(.12f));
                Assert.That(jumping.dropHeight - walking.dropHeight, Is.GreaterThan(.3f));

                (Vector3 landing, Vector3 dropOffset, float dropHeight) Observe(Vector2 velocity, bool jump)
                {
                    world.ResetRound();
                    world.Spawn(0, root.transform.position + Vector3.up * .035f, 0f);
                    world.SyncMovement();
                    for (int tick = 0; tick < 8; tick++) world.Move(0, velocity, 0f, 0f, false, false);
                    var motion = world.Move(0, velocity, 0f, 0f, jump, false);
                    world.SyncMovement();
                    Assert.That(motion.Grounded, Is.EqualTo(!jump));
                    var feet = pawn.transform.position;
                    var launch = pawn.Eye;
                    Assert.That(world.TryRestingPose(0, utility, true, null, out Pose rest), Is.True);
                    world.DropPath(0, utility, rest, out _, out Vector3 apex);
                    world.Advance(0);
                    world.Throw(0, utility);
                    var projectile = effects.GetChild(effects.childCount - 1);
                    var previous = projectile.position;
                    int stillTicks = 0;
                    for (int tick = 1; tick < 140 && stillTicks < 3; tick++)
                    {
                        world.Advance(tick);
                        stillTicks = (projectile.position - previous).sqrMagnitude < .000001f ? stillTicks + 1 : 0;
                        previous = projectile.position;
                    }
                    Assert.That(stillTicks, Is.EqualTo(3), "The projectile must reach and settle on the scene floor.");
                    Assert.That(projectile.position.y - root.transform.position.y, Is.InRange(0f, .12f));
                    return (projectile.position - launch, rest.position - feet, apex.y - rest.position.y);
                }
            }
            finally
            {
                world.Dispose();
                Object.Destroy(map);
                Object.Destroy(utility);
                Object.Destroy(root);
            }
            yield return null;
        }
    }
}
