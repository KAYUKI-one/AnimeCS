using System;
using System.Collections.Generic;
using Unity.Animations.SpringBones;
using UnityEngine;

namespace StylizedCharacters
{
    /// <summary>Runs cosmetic hair and garment constraints after the character's final animated pose.</summary>
    [RequireComponent(typeof(Animator)), DisallowMultipleComponent, DefaultExecutionOrder(1000)]
    public sealed class HumanoidSecondaryMotion : MonoBehaviour
    {
        [Tooltip("Optional roots of skinned chains with arbitrary bone names. Each terminal child is the segment tip.")]
        [SerializeField] private Transform[] hairRoots = Array.Empty<Transform>();
        [SerializeField] private Transform[] clothRoots = Array.Empty<Transform>();
        [SerializeField, Range(0, 1)] private float hairMotion = .62f;
        [SerializeField, Range(0, 1)] private float clothMotion = .48f;
        [SerializeField, Range(0, 80)] private float hairSwingDegrees = 17f;
        [SerializeField, Range(0, 60)] private float clothSwingDegrees = 10f;
        [SerializeField] private Vector3 gravity = new Vector3(0, -.75f, 0);

        private struct Joint
        {
            public SpringBone spring;
            public Quaternion rest;
            public bool hair;
        }

        private readonly List<Joint> _joints = new();
        private Animator _animator;
        private SpringManager _solver;
        private bool _reset = true;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        public int JointCount => _joints.Count;

        /// <summary>Presentation-only components suitable for an isolated imported visual.</summary>
        public static bool IsVisualBehaviour(MonoBehaviour component) =>
            component is HumanoidSecondaryMotion || component is SpringBone || component is SpringManager ||
            component is SpringCapsuleCollider || component is SpringSphereCollider || component is SpringPanelCollider;

        public static HumanoidSecondaryMotion Attach(GameObject model)
        {
            if (!model) return null;
            var animator = model.GetComponentInChildren<Animator>(true);
            if (!animator || !animator.avatar || !animator.isHuman) return null;
            var existing = animator.GetComponent<HumanoidSecondaryMotion>();
            if (existing) { existing.enabled = true; return existing; }
            if (animator.GetComponentInChildren<SpringManager>(true)) return null;
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
                if (IsExportedJoint(bone))
                    return animator.gameObject.AddComponent<HumanoidSecondaryMotion>();
            return null;
        }

        private static bool IsExportedJoint(Transform node) => node.childCount > 0 &&
            (node.name.StartsWith("SpringHair_", StringComparison.Ordinal) ||
             node.name.StartsWith("SpringCloth_", StringComparison.Ordinal));

        private void Start()
        {
            _animator = GetComponent<Animator>();
            if (!_animator || !_animator.isHuman) { enabled = false; return; }
            // An imported rig with its own authored solver has one owner.
            if (GetComponentInChildren<SpringManager>(true)) { enabled = false; return; }
            var palette = GetComponentsInChildren<Transform>(true);
            var upper = new List<SpringCapsuleCollider>();
            var lower = new List<SpringCapsuleCollider>();
            var chest = _animator.GetBoneTransform(HumanBodyBones.Chest) ? HumanBodyBones.Chest : HumanBodyBones.Spine;
            AddCapsule(chest, HumanBodyBones.Neck, .35f, upper);
            AddCapsule(HumanBodyBones.Hips, chest, .39f, lower);
            upper.AddRange(lower);
            AddCapsule(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, .15f, upper);
            AddCapsule(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, .15f, upper);
            AddCapsule(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, .22f, lower);
            AddCapsule(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, .22f, lower);
            AddCapsule(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, .14f, lower);
            AddCapsule(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, .14f, lower);
            var upperColliders = upper.ToArray();
            var lowerColliders = lower.ToArray();
            var torsoAndLegs = new List<SpringCapsuleCollider>(upper);
            foreach (var collider in lower)
                if (!torsoAndLegs.Contains(collider)) torsoAndLegs.Add(collider);
            var coatColliders = torsoAndLegs.ToArray();
            var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            var heads = new List<SpringSphereCollider>();
            var head = _animator.GetBoneTransform(HumanBodyBones.Head);
            var neck = _animator.GetBoneTransform(HumanBodyBones.Neck);
            if (head && neck)
            {
                var sphere = new GameObject("Head spring collider").AddComponent<SpringSphereCollider>();
                sphere.transform.SetParent(head, false);
                var up = head.position - neck.position;
                sphere.transform.position = head.position + up * .46f;
                sphere.radius = Mathf.Max(.001f, sphere.transform.InverseTransformVector(up).magnitude * .68f);
                heads.Add(sphere);
            }
            var headColliders = heads.ToArray();
            foreach (var node in palette)
            {
                if (node.childCount == 0 || node.name.EndsWith("_End", StringComparison.Ordinal)) continue;
                bool hair = node.name.StartsWith("SpringHair_", StringComparison.Ordinal) || BelongsTo(node, hairRoots);
                if (!hair && !node.name.StartsWith("SpringCloth_", StringComparison.Ordinal) && !BelongsTo(node, clothRoots)) continue;
                var spring = node.gameObject.AddComponent<SpringBone>();
                spring.radius = Mathf.Clamp(Vector3.Distance(node.position, node.GetChild(0).position) * .13f, .003f, .014f);
                spring.stiffnessForce = hair ? 260f : 370f;
                spring.springForce = Vector3.zero;
                // A cape can reach the shoulders and thighs during the same turn.
                bool aboveWaist = hips && Vector3.Dot(node.position - hips.position, transform.up) > 0;
                spring.capsuleColliders = hair ? upperColliders : aboveWaist ? coatColliders : lowerColliders;
                spring.sphereColliders = hair ? headColliders : Array.Empty<SpringSphereCollider>();
                spring.panelColliders = Array.Empty<SpringPanelCollider>();
                _joints.Add(new Joint { spring = spring, rest = node.localRotation, hair = hair });
            }
            if (_joints.Count == 0) { enabled = false; return; }
            _solver = gameObject.AddComponent<SpringManager>();
            _solver.automaticUpdates = false;
            _solver.enabled = false;
            _solver.collideWithGround = false;
            _solver.enableLengthLimits = _solver.enableAngleLimits = false;
            _solver.bounce = 0;
            _solver.friction = .65f;
        }

        private static bool BelongsTo(Transform joint, Transform[] roots)
        {
            foreach (var root in roots) if (root && joint.IsChildOf(root)) return true;
            return false;
        }

        private void AddCapsule(HumanBodyBones from, HumanBodyBones to, float relativeRadius, List<SpringCapsuleCollider> group)
        {
            var start = _animator.GetBoneTransform(from);
            var end = _animator.GetBoneTransform(to);
            if (!start || !end) return;
            var axis = start.InverseTransformPoint(end.position);
            if (axis.sqrMagnitude < 1e-10f) return;
            var capsule = new GameObject(from + " spring collider").AddComponent<SpringCapsuleCollider>();
            capsule.transform.SetParent(start, false);
            capsule.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            capsule.transform.localPosition = axis * .12f;
            capsule.height = axis.magnitude * .74f;
            capsule.radius = axis.magnitude * relativeRadius;
            group.Add(capsule);
        }

        private void OnEnable() => _reset = true;

        private void OnDisable()
        {
            foreach (var joint in _joints)
                if (joint.spring) joint.spring.transform.localRotation = joint.rest;
        }

        private void LateUpdate()
        {
            if (!_solver) return;
            var position = transform.position;
            var rotation = transform.rotation;
            float elapsed = Time.deltaTime;
            if (_reset || elapsed > .12f || (position - _previousPosition).sqrMagnitude > 1f ||
                Quaternion.Angle(rotation, _previousRotation) > 95f)
            {
                foreach (var joint in _joints) joint.spring.transform.localRotation = joint.rest;
                foreach (var joint in _joints) joint.spring.Initialize(_solver);
                _reset = false;
            }
            else if (elapsed > 0 && (!_animator.runtimeAnimatorController || _animator.speed > 0))
            {
                int steps = Mathf.Clamp(Mathf.CeilToInt(elapsed * 60f - .001f), 1, 8);
                float dt = elapsed / steps;
                float hairDrag = 1f - Mathf.Pow(.67f, dt * 60f);
                float clothDrag = 1f - Mathf.Pow(.53f, dt * 60f);
                for (int step = 0; step < steps; step++)
                    foreach (var joint in _joints)
                    {
                        var spring = joint.spring;
                        spring.transform.localRotation = joint.rest;
                        spring.dragForce = joint.hair ? hairDrag : clothDrag;
                        spring.UpdateSpring(dt, gravity);
                        spring.SatisfyConstraintsAndComputeRotation(dt, joint.hair ? hairMotion : clothMotion);
                        // A length-preserving spring can turn through 180 degrees during a sharp reversal.
                        // Limit swing in each imported joint's own rest frame, independent of its FBX axes.
                        spring.transform.localRotation = Quaternion.RotateTowards(joint.rest,
                            spring.transform.localRotation, joint.hair ? hairSwingDegrees : clothSwingDegrees);
                    }
            }
            _previousPosition = position;
            _previousRotation = rotation;
        }
    }
}
