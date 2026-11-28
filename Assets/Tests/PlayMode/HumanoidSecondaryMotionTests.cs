using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimeCS.Content;
using AnimeCS.Play;
using NUnit.Framework;
using StylizedCharacters;
using Unity.Animations.SpringBones;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AnimeCS.Tests
{
    public sealed class HumanoidSecondaryMotionTests
    {
        private const string GameplayScene = "Assets/Scenes/Gameplay/LocalMatch.unity";

        [UnityTest]
        public IEnumerator LiveRosterBindsSkinnedSpringChainsToReachableBodyColliders()
        {
            yield return SceneManager.LoadSceneAsync(GameplayScene);
            var roster = ReadSceneCatalog().SeatOutfits.Select(outfit => outfit.Character).Distinct().ToArray();
            Assert.That(roster.Select(character => character.name), Is.EquivalentTo(new[]
                { "Rover", "Lily", "Mornye", "Michele", "Flor", "StarTraveler", "Firefly", "Nora" }));
            var unsupported = new GameObject("No humanoid");
            try { Assert.That(HumanoidSecondaryMotion.Attach(unsupported), Is.Null); }
            finally { Object.Destroy(unsupported); }

            int castWithReachableCapsules = 0;
            foreach (var character in roster)
            {
                var model = Object.Instantiate(character.Model);
                try
                {
                    var animator = StaticAnimator(model);
                    var motion = HumanoidSecondaryMotion.Attach(model);
                    Assert.That(motion, Is.Not.Null, character.name);
                    Assert.That(HumanoidSecondaryMotion.Attach(model), Is.SameAs(motion), character.name);
                    yield return null;
                    var springs = animator.GetComponentsInChildren<SpringBone>(true);
                    Assert.That(springs, Is.Not.Empty, character.name);
                    Assert.That(motion.JointCount, Is.EqualTo(springs.Length), character.name);
                    Assert.That(animator.GetComponentsInChildren<SpringManager>(true), Has.Length.EqualTo(1), character.name);
                    var skinBones = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .Where(renderer => renderer.sharedMesh).SelectMany(renderer => renderer.bones).ToHashSet();
                    Assert.That(springs.Any(spring => IsHair(spring) && skinBones.Contains(spring.transform)), Is.True, character.name);
                    Assert.That(springs.Any(spring => !IsHair(spring) && skinBones.Contains(spring.transform)), Is.True, character.name);
                    foreach (var spring in springs)
                    {
                        Assert.That(spring.transform.childCount, Is.GreaterThan(0), spring.name);
                        Assert.That(spring.capsuleColliders, Is.Not.Empty, spring.name);
                        Assert.That(spring.capsuleColliders.All(collider =>
                            collider.radius > 0 && collider.height > 0 && collider.transform.IsChildOf(animator.transform)), Is.True, spring.name);
                        Assert.That(spring.sphereColliders.Length, Is.EqualTo(IsHair(spring) ? 1 : 0), spring.name);
                        AssertFinite(spring.CurrentTipPosition);
                    }
                    if (springs.Any(VerifyReachableCapsule))
                        castWithReachableCapsules++;
                }
                finally { Object.Destroy(model); }
            }
            // Some exported tips cannot reach the body's collider even at full bend.
            Assert.That(castWithReachableCapsules, Is.GreaterThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator MovingSpringSkinDiffersFromControlAndResetsAfterReuseAndTeleport()
        {
            yield return SceneManager.LoadSceneAsync(GameplayScene);
            var character = ReadSceneCatalog().SeatOutfits.Select(outfit => outfit.Character)
                .First(definition => definition.name == "Mornye");
            var driven = Object.Instantiate(character.Model);
            var control = Object.Instantiate(character.Model);
            var drivenMesh = new Mesh();
            var controlMesh = new Mesh();
            SpringCapture capture = null;
            float previousCapture = Time.captureDeltaTime;
            float previousScale = Time.timeScale;
            try
            {
                Time.timeScale = 1;
                Time.captureDeltaTime = 1f / 60;
                var animator = StaticAnimator(driven);
                var reference = StaticAnimator(control);
                var motion = HumanoidSecondaryMotion.Attach(driven);
                var disabledMotion = HumanoidSecondaryMotion.Attach(control);
                Assert.That(motion, Is.Not.Null);
                Assert.That(disabledMotion, Is.Not.Null);
                disabledMotion.enabled = false;
                yield return null;
                Assert.That(motion.JointCount, Is.GreaterThan(0));
                Assert.That(disabledMotion.JointCount, Is.Zero);

                var drivenRenderers = driven.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var controlRenderers = control.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var springNodes = driven.GetComponentsInChildren<SpringBone>(true).Select(spring => spring.transform).ToHashSet();
                int index = Array.FindIndex(drivenRenderers, renderer =>
                    renderer.sharedMesh && renderer.bones.Any(springNodes.Contains));
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                var renderer = drivenRenderers[index];
                var referenceRenderer = controlRenderers[index];
                Assert.That(renderer.sharedMesh, Is.SameAs(referenceRenderer.sharedMesh));
                var dynamicBones = renderer.bones.Select(springNodes.Contains).ToArray();
                var movingVertices = renderer.sharedMesh.boneWeights.Select((weight, vertex) => (weight, vertex))
                    .Where(entry => IsSpringWeighted(entry.weight, dynamicBones)).Select(entry => entry.vertex).ToArray();
                Assert.That(movingVertices, Is.Not.Empty, "actual skin weights");
                var active = new List<Vector3>();
                var inactive = new List<Vector3>();
                var chest = animator.GetBoneTransform(HumanBodyBones.Spine);
                var referenceChest = reference.GetBoneTransform(HumanBodyBones.Spine);
                var restChest = chest.localRotation;
                var referenceRestChest = referenceChest.localRotation;
                float Difference() => MeshDifference(renderer, referenceRenderer, drivenMesh, controlMesh, active, inactive, movingVertices);
                AssertRestDifference(Difference(), "initial reset");
                string capturePath = Environment.GetEnvironmentVariable("ANIMECS_SPRING_CAPTURES");
                if (!string.IsNullOrWhiteSpace(capturePath))
                    capture = new SpringCapture(capturePath, driven, control);

                foreach (float frameDelta in new[] { 1f / 60, 1f / 30 })
                {
                    Time.captureDeltaTime = frameDelta;
                    float peak = 0;
                    for (int frame = 0; frame < 12; frame++)
                    {
                        float travel = Mathf.Sin(frame * .60f);
                        var position = new Vector3(travel * .30f, -.22f * Mathf.Abs(travel), frame * .04f);
                        var rotation = Quaternion.Euler(0, travel * 48, 0);
                        driven.transform.SetPositionAndRotation(position, rotation);
                        control.transform.SetPositionAndRotation(position, rotation);
                        var bend = Quaternion.Euler(Mathf.Abs(travel) * 25, 0, travel * 10);
                        chest.localRotation = restChest * bend;
                        referenceChest.localRotation = referenceRestChest * bend;
                        yield return null;
                        float distance = Difference();
                        peak = Mathf.Max(peak, distance);
                        if (capture != null && frameDelta < .02f)
                            capture.WriteFrame(frame, distance);
                    }
                    Assert.That(Time.deltaTime, Is.EqualTo(frameDelta).Within(.002f));
                    TestContext.Out.WriteLine($"Mornye {1f / frameDelta:F0} Hz peak mesh difference: {peak:F6} m");
                    Assert.That(peak, Is.InRange(.001f, .50f), $"baked spring vertices at {1f / frameDelta:F0} Hz");
                }

                var relocated = new Vector3(5, 1.5f, -3);
                driven.transform.SetPositionAndRotation(relocated, Quaternion.Euler(0, 145, 0));
                control.transform.SetPositionAndRotation(relocated, Quaternion.Euler(0, 145, 0));
                chest.localRotation = restChest;
                referenceChest.localRotation = referenceRestChest;
                yield return null;
                AssertRestDifference(Difference(), "teleport reset");

                for (int frame = 0; frame < 5; frame++)
                {
                    driven.transform.position += Vector3.right * .16f;
                    control.transform.position = driven.transform.position;
                    yield return null;
                }
                float resumedDifference = Difference();
                TestContext.Out.WriteLine($"Mornye resumed after teleport: {resumedDifference:F6} m");
                Assert.That(resumedDifference, Is.GreaterThan(.001f), "motion resumes after teleport");
                motion.enabled = false;
                yield return null;
                Assert.That(HumanoidSecondaryMotion.Attach(driven), Is.SameAs(motion));
                yield return null;
                AssertRestDifference(Difference(), "re-enabled on the same rig");
                Assert.That(driven.GetComponentsInChildren<SpringManager>(true), Has.Length.EqualTo(1));
            }
            finally
            {
                capture?.Dispose();
                Time.captureDeltaTime = previousCapture;
                Time.timeScale = previousScale;
                Object.Destroy(drivenMesh);
                Object.Destroy(controlMesh);
                Object.Destroy(driven);
                Object.Destroy(control);
            }
        }

        [UnityTest]
        public IEnumerator NonuniformScaleAndSharpTurnsKeepTipsOnTheirSkinnedSegments()
        {
            yield return SceneManager.LoadSceneAsync(GameplayScene);
            var character = ReadSceneCatalog().SeatOutfits.Select(outfit => outfit.Character)
                .First(definition => definition.name == "Flor");
            var rig = Object.Instantiate(character.Model);
            var originalStep = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                StaticAnimator(rig);
                var motion = HumanoidSecondaryMotion.Attach(rig);
                yield return null;
                var bones = rig.GetComponentsInChildren<SpringBone>();
                Assert.That(bones, Is.Not.Empty);
                var rest = bones.Select(bone => bone.transform.localRotation).ToArray();
                var segments = bones.Select(bone => bone.transform.InverseTransformPoint(bone.ComputeChildPosition())).ToArray();
                var previous = (Quaternion[])rest.Clone();
                for (int frame = 0; frame < 18; frame++)
                {
                    rig.transform.localScale = new Vector3(.96f, frame % 2 == 0 ? 1.16f : .79f, 1.07f);
                    rig.transform.SetPositionAndRotation(new Vector3(frame * .4f, 0, 0),
                        Quaternion.Euler(0, frame % 2 == 0 ? 35f : -35f, 0));
                    yield return null;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        var bone = bones[i];
                        var node = bone.transform;
                        AssertFinite(bone.CurrentTipPosition);
                        float error = Vector3.Distance(bone.CurrentTipPosition, node.TransformPoint(segments[i]));
                        Assert.That(error, Is.LessThan(.0001f), bone.name);
                        Assert.That(Quaternion.Angle(previous[i], node.localRotation), Is.LessThan(7.1f), bone.name);
                        previous[i] = node.localRotation;
                    }
                }
                motion.ResetMotion(preservePose: true);
                yield return null;
                for (int i = 0; i < bones.Length; i++)
                    Assert.That(Quaternion.Angle(previous[i], bones[i].transform.localRotation), Is.LessThan(.06f), bones[i].name);

                motion.ResetMotion();
                yield return null;
                for (int i = 0; i < bones.Length; i++)
                    Assert.That(Quaternion.Angle(rest[i], bones[i].transform.localRotation), Is.LessThan(.06f), bones[i].name);
            }
            finally
            {
                Time.captureDeltaTime = originalStep;
                Object.Destroy(rig);
            }
        }

        private sealed class SpringCapture : IDisposable
        {
            private const int Width = 680;
            private const int Height = 840;
            private const int DrivenLayer = 28;
            private const int ControlLayer = 29;
            private readonly string _directory;
            private readonly Camera _camera;
            private readonly Light _light;
            private readonly RenderTexture _target;
            private readonly Texture2D _pixels;

            public SpringCapture(string directory, GameObject driven, GameObject control)
            {
                _directory = Path.GetFullPath(directory);
                Directory.CreateDirectory(_directory);
                SetCaptureLayer(driven, DrivenLayer);
                SetCaptureLayer(control, ControlLayer);

                _light = new GameObject("Spring comparison key").AddComponent<Light>();
                _light.type = LightType.Directional;
                _light.intensity = 1.35f;
                _light.transform.rotation = Quaternion.Euler(32, -38, 0);
                _light.cullingMask = 1 << DrivenLayer | 1 << ControlLayer;
                _camera = new GameObject("Spring comparison camera").AddComponent<Camera>();
                _camera.enabled = false;
                _camera.orthographic = true;
                _camera.orthographicSize = 1.19f;
                _camera.nearClipPlane = .05f;
                _camera.farClipPlane = 12f;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(.23f, .29f, .39f);
                _camera.transform.position = new Vector3(2.7f, 1.64f, -3.7f);
                _camera.transform.LookAt(new Vector3(0, .84f, .23f));
                _target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                _camera.targetTexture = _target;
                _pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                File.WriteAllText(Path.Combine(_directory, "motion-meters.csv"), "frame,peak_vertex_difference_m\n");
            }

            public void WriteFrame(int frame, float distance)
            {
                WriteImage(DrivenLayer, $"spring-on-{frame:D2}.png");
                WriteImage(ControlLayer, $"spring-off-{frame:D2}.png");
                File.AppendAllText(Path.Combine(_directory, "motion-meters.csv"),
                    FormattableString.Invariant($"{frame},{distance:F6}\n"));
            }

            private static void SetCaptureLayer(GameObject model, int layer)
            {
                foreach (var child in model.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = layer;
            }

            private void WriteImage(int layer, string fileName)
            {
                var previous = RenderTexture.active;
                try
                {
                    _camera.cullingMask = 1 << layer;
                    _camera.Render();
                    RenderTexture.active = _target;
                    _pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                    File.WriteAllBytes(Path.Combine(_directory, fileName), _pixels.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; }
            }

            public void Dispose()
            {
                _camera.targetTexture = null;
                _target.Release();
                Object.Destroy(_target);
                Object.Destroy(_pixels);
                Object.Destroy(_camera.gameObject);
                Object.Destroy(_light.gameObject);
            }
        }

        private static ContentCatalog ReadSceneCatalog()
        {
            var game = Object.FindAnyObjectByType<LocalGame>();
            Assert.That(game, Is.Not.Null, GameplayScene);
            var field = typeof(LocalGame).GetField("_catalog", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "serialized runtime catalog field");
            var catalog = (ContentCatalog)field.GetValue(game);
            Assert.That(catalog, Is.Not.Null, GameplayScene);
            return catalog;
        }

        private static void AssertRestDifference(float difference, string trigger)
        {
            TestContext.Out.WriteLine($"Mornye {trigger}: {difference:F6} m");
            Assert.That(difference, Is.LessThan(.002f), trigger);
        }

        private static Animator StaticAnimator(GameObject model)
        {
            var animator = model.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null, model.name);
            Assert.That(animator.isHuman, Is.True, model.name);
            animator.runtimeAnimatorController = null;
            return animator;
        }

        private static bool IsHair(SpringBone spring) => spring.name.StartsWith("SpringHair_", StringComparison.Ordinal);

        private static bool IsSpringWeighted(BoneWeight weight, bool[] palette) =>
            weight.weight0 > .02f && palette[weight.boneIndex0] || weight.weight1 > .02f && palette[weight.boneIndex1] ||
            weight.weight2 > .02f && palette[weight.boneIndex2] || weight.weight3 > .02f && palette[weight.boneIndex3];

        private static float MeshDifference(SkinnedMeshRenderer driven, SkinnedMeshRenderer control, Mesh drivenMesh,
            Mesh controlMesh, List<Vector3> active, List<Vector3> inactive, int[] indices)
        {
            driven.BakeMesh(drivenMesh);
            control.BakeMesh(controlMesh);
            drivenMesh.GetVertices(active);
            controlMesh.GetVertices(inactive);
            Assert.That(active.Count, Is.EqualTo(inactive.Count));
            float maxSquared = 0;
            foreach (int index in indices)
            {
                Vector3 delta = driven.transform.TransformPoint(active[index]) -
                    control.transform.TransformPoint(inactive[index]);
                if (!float.IsFinite(delta.sqrMagnitude))
                    Assert.Fail($"Non-finite baked vertex {index}");
                maxSquared = Mathf.Max(maxSquared, delta.sqrMagnitude);
            }
            return Mathf.Sqrt(maxSquared);
        }

        private static bool VerifyReachableCapsule(SpringBone spring)
        {
            Vector3 joint = spring.transform.position;
            float length = Vector3.Distance(joint, spring.CurrentTipPosition);
            float worldRadius = spring.transform.TransformDirection(Vector3.right * spring.radius).magnitude;
            foreach (var capsule in spring.capsuleColliders)
            {
                var space = capsule.transform;
                float radius = capsule.radius + space.InverseTransformVector(Vector3.right * worldRadius).magnitude;
                for (int slice = 1; slice <= 7; slice++)
                for (int direction = 0; direction < 12; direction++)
                {
                    float angle = direction * Mathf.PI / 6;
                    var seed = new Vector3(Mathf.Cos(angle) * radius * .72f, capsule.height * slice / 8,
                        Mathf.Sin(angle) * radius * .72f);
                    // Project onto the actual bone's reach before checking penetration.
                    Vector3 tip = joint + (space.TransformPoint(seed) - joint).normalized * length;
                    Vector3 local = space.InverseTransformPoint(tip);
                    float overlap = radius - new Vector2(local.x, local.z).magnitude;
                    if (local.y <= 0 || local.y >= capsule.height || overlap < .001f) continue;
                    var corrected = tip;
                    Vector3 normal = Vector3.zero;
                    var result = capsule.CheckForCollisionAndReact(joint, ref corrected, worldRadius, ref normal);
                    Assert.That(result, Is.Not.EqualTo(SpringBone.CollisionStatus.NoCollision), spring.name);
                    AssertFinite(corrected);
                    float correction = Vector3.Distance(corrected, tip);
                    Assert.That(correction, Is.GreaterThan(0), spring.name);
                    local = space.InverseTransformPoint(corrected);
                    Assert.That(new Vector2(local.x, local.z).magnitude, Is.EqualTo(radius).Within(.0001f), spring.name);
                    TestContext.Out.WriteLine($"{spring.name} / {capsule.name}: {length:F6} m spring length, {correction:F6} m capsule correction");
                    return true;
                }
            }
            return false;
        }

        private static void AssertFinite(Vector3 value) => Assert.That(float.IsFinite(value.sqrMagnitude), Is.True, value.ToString());
    }
}
