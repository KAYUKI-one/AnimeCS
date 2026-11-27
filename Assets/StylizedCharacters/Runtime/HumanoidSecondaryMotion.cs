using System;
using System.Collections.Generic;
using Unity.Animations.SpringBones;
using UnityEngine;

namespace StylizedCharacters
{
    /// <summary>Runs cosmetic hair and garment constraints after the character's final animated pose.</summary>
    [RequireComponent(typeof(Animator)), DisallowMultipleComponent, DefaultExecutionOrder(1000)]
    public sealed partial class HumanoidSecondaryMotion : MonoBehaviour
    {
        public const float BodyClearance = .015f;
        [Tooltip("Optional roots of skinned chains with arbitrary bone names. Each terminal child is the segment tip.")]
        [SerializeField] private Transform[] hairRoots = Array.Empty<Transform>();
        [SerializeField] private Transform[] clothRoots = Array.Empty<Transform>();
        [SerializeField, HideInInspector] private Transform[] hairJoints = Array.Empty<Transform>();
        [SerializeField, HideInInspector] private Transform[] clothJoints = Array.Empty<Transform>();
        [SerializeField, Range(0, 1)] private float hairMotion = .62f;
        [SerializeField, Range(0, 1)] private float clothMotion = .48f;
        [SerializeField, Range(0, 80)] private float hairSwingDegrees = 17f;
        [SerializeField, Range(0, 60)] private float clothSwingDegrees = 10f;
        [SerializeField] private Vector3 gravity = new Vector3(0, -.75f, 0);
        [SerializeField] private SecondaryMotionProfile profile;

        private readonly List<SecondaryMotionSolver.Joint> _joints = new();
        private Animator _animator;
        private SecondaryMotionSolver _solver;
        private bool _reset = true;
        private bool _preservePose;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        public int JointCount => _joints.Count;

        /// <summary>Presentation-only components suitable for an isolated imported visual.</summary>
        public static bool IsVisualBehaviour(MonoBehaviour component) =>
            component is HumanoidSecondaryMotion || component is SpringBone || component is SpringManager ||
            component is SpringCapsuleCollider || component is SpringSphereCollider || component is SpringPanelCollider;

        public static HumanoidSecondaryMotion Attach(GameObject model) => Attach(model, null);

        public static HumanoidSecondaryMotion Attach(GameObject model, SecondaryMotionProfile fallbackProfile)
        {
            if (!model) return null;
            var animator = model.GetComponentInChildren<Animator>(true);
            if (!animator || !animator.avatar || !animator.isHuman) return null;
            var existing = animator.GetComponent<HumanoidSecondaryMotion>();
            if (existing)
            {
                if (existing._solver == null && !existing.profile) existing.profile = fallbackProfile;
                existing.enabled = true;
                return existing;
            }
            if (animator.GetComponentInChildren<SpringManager>(true)) return null;
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
                if (IsExportedJoint(bone))
                {
                    var motion = animator.gameObject.AddComponent<HumanoidSecondaryMotion>();
                    motion.profile = fallbackProfile;
                    return motion;
                }
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
            AddCapsule(chest, HumanBodyBones.Neck, .35f, upper, BodyClearance);
            AddCapsule(HumanBodyBones.Hips, chest, .39f, lower, BodyClearance);
            upper.AddRange(lower);
            AddCapsule(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, .15f, upper);
            AddCapsule(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, .15f, upper);
            int thighStart = lower.Count;
            AddCapsule(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, .22f, lower, BodyClearance);
            AddCapsule(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, .22f, lower, BodyClearance);
            for (int i = thighStart; i < lower.Count; i++) upper.Add(lower[i]);
            AddCapsule(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, .14f, lower);
            AddCapsule(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, .14f, lower);
            AddCapsule(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, .14f, upper);
            AddCapsule(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, .14f, upper);
            AddCapsule(chest, HumanBodyBones.Neck, .28f, upper, name: "Back", forwardOffset: -.23f);
            var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            var leftHip = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var rightHip = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (hips && leftHip && rightHip)
            {
                var left = hips.InverseTransformPoint(leftHip.position);
                var right = hips.InverseTransformPoint(rightHip.position);
                var offset = hips.InverseTransformVector(-transform.forward *
                    (Vector3.Distance(leftHip.position, rightHip.position) * .15f));
                AddCapsule("Pelvis", hips, left + offset, right + offset, .42f, lower, BodyClearance);
            }
            var upperColliders = upper.ToArray();
            var lowerColliders = lower.ToArray();
            var torsoAndLegs = new List<SpringCapsuleCollider>(upper);
            foreach (var collider in lower)
                if (!torsoAndLegs.Contains(collider)) torsoAndLegs.Add(collider);
            var coatColliders = torsoAndLegs.ToArray();
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
            var automaticHair = new HashSet<Transform>(hairJoints);
            var automaticCloth = new HashSet<Transform>(clothJoints);
            var indices = new Dictionary<Transform, int>();
            var contacts = new List<SecondaryMotionSolver.Contact>();
            var capsuleContacts = new Dictionary<SpringCapsuleCollider, SecondaryMotionSolver.Contact>();
            foreach (var collider in coatColliders)
            {
                var contact = new SecondaryMotionSolver.Contact(collider);
                capsuleContacts.Add(collider, contact);
                contacts.Add(contact);
            }
            var headContact = heads.Count > 0 ? new SecondaryMotionSolver.Contact(heads[0]) : null;
            if (headContact != null) contacts.Add(headContact);
            foreach (var node in palette)
            {
                if (node.childCount == 0 || node.name.EndsWith("_End", StringComparison.Ordinal)) continue;
                bool hair = node.name.StartsWith("SpringHair_", StringComparison.Ordinal) || BelongsTo(node, hairRoots) || automaticHair.Contains(node);
                if (!hair && !node.name.StartsWith("SpringCloth_", StringComparison.Ordinal) &&
                    !BelongsTo(node, clothRoots) && !automaticCloth.Contains(node)) continue;
                var settings = SettingsFor(node, hair);
                var spring = node.GetComponent<SpringBone>();
                if (!spring) spring = node.gameObject.AddComponent<SpringBone>();
                spring.radius = Mathf.Clamp(Vector3.Distance(node.position, node.GetChild(0).position) * settings.radiusScale, .003f, .014f);
                spring.stiffnessForce = settings.stiffness;
                spring.dragForce = settings.dampingAt60Hz;
                spring.springForce = Vector3.zero;
                // A cape can reach the shoulders and thighs during the same turn.
                bool aboveWaist = hips && Vector3.Dot(node.position - hips.position, transform.up) > 0;
                spring.capsuleColliders = hair ? upperColliders : aboveWaist ? coatColliders : lowerColliders;
                spring.sphereColliders = hair ? headColliders : Array.Empty<SpringSphereCollider>();
                spring.panelColliders = Array.Empty<SpringPanelCollider>();
                var joint = new SecondaryMotionSolver.Joint
                {
                    Spring = spring,
                    Rest = node.localRotation,
                    LocalTip = node.InverseTransformPoint(spring.ComputeChildPosition()),
                    Settings = settings,
                    Root = _joints.Count,
                    Contacts = new SecondaryMotionSolver.Contact[spring.capsuleColliders.Length + spring.sphereColliders.Length]
                };
                if (node.parent && indices.TryGetValue(node.parent, out int parentIndex))
                {
                    joint.Parent = parentIndex;
                    joint.Root = _joints[parentIndex].Root;
                }
                for (int i = 0; i < spring.capsuleColliders.Length; i++)
                    joint.Contacts[i] = capsuleContacts[spring.capsuleColliders[i]];
                if (hair && headContact != null) joint.Contacts[^1] = headContact;
                indices.Add(node, _joints.Count);
                _joints.Add(joint);
            }
            if (_joints.Count == 0) { enabled = false; return; }
            var manager = gameObject.AddComponent<SpringManager>();
            manager.automaticUpdates = false;
            manager.enabled = false;
            manager.collideWithGround = false;
            manager.enableLengthLimits = manager.enableAngleLimits = false;
            MeasureChainReach();
            BindGarmentNeighbors(hips);
            _solver = new SecondaryMotionSolver(_joints, contacts);
        }

        private void MeasureChainReach()
        {
            for (int i = _joints.Count - 1; i >= 0; i--)
            {
                var joint = _joints[i];
                joint.Reach += Vector3.Distance(joint.Spring.transform.position, joint.DisplayedTip);
                if (joint.Parent < 0) continue;
                var parent = _joints[joint.Parent];
                parent.Reach = Mathf.Max(parent.Reach, joint.Reach);
                parent.ChainDepth = Mathf.Max(parent.ChainDepth, joint.ChainDepth + 1);
            }
            foreach (var joint in _joints)
                joint.Reach *= _joints[joint.Root].ChainDepth;
        }

        private SecondaryMotionProfile.SpringSettings SettingsFor(Transform node, bool hair)
        {
            var part = hair ? SecondaryMotionProfile.Part.Hair : SecondaryMotionProfile.Part.Skirt;
            if (!hair)
                for (var branch = node; branch && branch != transform; branch = branch.parent)
                {
                    if (branch.name.IndexOf("sleeve", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        branch.name.Contains("袖"))
                    { part = SecondaryMotionProfile.Part.Sleeve; break; }
                    if (branch.name.IndexOf("ribbon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        branch.name.IndexOf("scarf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        branch.name.IndexOf("piaodai", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        branch.name.Contains("飘带") || branch.name.Contains("飄帶"))
                    { part = SecondaryMotionProfile.Part.Ribbon; break; }
                }
            if (profile) return profile.For(part);
            var settings = part switch
            {
                SecondaryMotionProfile.Part.Hair => SecondaryMotionProfile.Hair,
                SecondaryMotionProfile.Part.Sleeve => SecondaryMotionProfile.Sleeve,
                SecondaryMotionProfile.Part.Ribbon => SecondaryMotionProfile.Ribbon,
                _ => SecondaryMotionProfile.Skirt
            };
            settings.inertia = hair ? hairMotion : settings.inertia * clothMotion / .48f;
            settings.swingDegrees = Mathf.Min(75f, settings.swingDegrees * (hair ? hairSwingDegrees / 17f : clothSwingDegrees / 10f));
            return settings;
        }

        private void BindGarmentNeighbors(Transform hips)
        {
            if (!hips) return;
            // Export indices do not encode adjacency. Link only nearby parallel segments
            // with comparable lengths from different chains rooted on the pelvis.
            foreach (var joint in _joints)
            {
                var root = _joints[joint.Root].Spring.transform;
                if (root.parent != hips || !root.name.StartsWith("SpringCloth_", StringComparison.Ordinal)) continue;
                float nearest = .15f;
                SecondaryMotionSolver.Joint neighbor = null;
                var direction = joint.DisplayedTip - joint.Spring.transform.position;
                foreach (var candidate in _joints)
                {
                    var otherRoot = _joints[candidate.Root].Spring.transform;
                    if (candidate.Root == joint.Root || otherRoot.parent != hips ||
                        !otherRoot.name.StartsWith("SpringCloth_", StringComparison.Ordinal)) continue;
                    var otherDirection = candidate.DisplayedTip - candidate.Spring.transform.position;
                    if (Vector3.Dot(direction.normalized, otherDirection.normalized) < .85f ||
                        Mathf.Abs(direction.magnitude - otherDirection.magnitude) > direction.magnitude * .35f) continue;
                    float spacing = Vector3.Distance(joint.DisplayedTip, candidate.DisplayedTip);
                    if (spacing < .025f || spacing >= nearest) continue;
                    nearest = spacing;
                    neighbor = candidate;
                }
                if (neighbor != null)
                    joint.Neighbors.Add((neighbor, hips.InverseTransformVector(joint.DisplayedTip - neighbor.DisplayedTip)));
            }
        }

        private static bool BelongsTo(Transform joint, Transform[] roots)
        {
            foreach (var root in roots) if (root && joint.IsChildOf(root)) return true;
            return false;
        }

        private void AddCapsule(HumanBodyBones from, HumanBodyBones to, float relativeRadius,
            List<SpringCapsuleCollider> group, float clearance = 0, string name = null, float forwardOffset = 0)
        {
            var start = _animator.GetBoneTransform(from);
            var end = _animator.GetBoneTransform(to);
            if (!start || !end) return;
            var axis = start.InverseTransformPoint(end.position);
            var offset = start.InverseTransformVector(transform.forward *
                (Vector3.Distance(start.position, end.position) * forwardOffset));
            AddCapsule(name ?? from.ToString(), start, axis * .12f + offset, axis * .86f + offset,
                relativeRadius / .74f, group, clearance);
        }

        private static void AddCapsule(string name, Transform parent, Vector3 start, Vector3 end,
            float relativeRadius, List<SpringCapsuleCollider> group, float clearance)
        {
            var axis = end - start;
            if (axis.sqrMagnitude < 1e-10f) return;
            var capsule = new GameObject(name + " spring collider").AddComponent<SpringCapsuleCollider>();
            capsule.transform.SetParent(parent, false);
            capsule.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            capsule.transform.localPosition = start;
            capsule.height = axis.magnitude;
            capsule.radius = axis.magnitude * relativeRadius +
                clearance * capsule.transform.InverseTransformVector(Vector3.right).magnitude;
            group.Add(capsule);
        }

        /// <summary>Clears spring velocity on the next LateUpdate. Camera changes can keep the displayed pose.</summary>
        public void ResetMotion(bool preservePose = false)
        {
            _preservePose = preservePose && (!_reset || _preservePose);
            _reset = true;
        }

        private void OnEnable() => ResetMotion();

        private void OnDisable()
        {
            foreach (var joint in _joints)
                if (joint.Spring) joint.Spring.transform.localRotation = joint.Rest;
        }

        private void LateUpdate()
        {
            if (_solver == null) return;
            var position = transform.position;
            var rotation = transform.rotation;
            float elapsed = Time.deltaTime;
            bool discontinuity = elapsed > .12f || (position - _previousPosition).sqrMagnitude > 1f ||
                Quaternion.Angle(rotation, _previousRotation) > 95f;
            bool resetting = _reset || discontinuity;
            bool preservePose = _preservePose && !discontinuity;
            if (resetting)
            {
                _solver.Reset(preservePose);
                _reset = false;
                _preservePose = false;
            }
            else if (elapsed > 0 && (!_animator.runtimeAnimatorController || _animator.speed > 0))
                _solver.Advance(elapsed, profile ? profile.Step : 1f / 120f, profile ? profile.Iterations : 4,
                    profile ? profile.FadeIn : .12f, profile ? profile.Gravity : gravity);
#if UNITY_EDITOR
            SampleDebug(resetting && !preservePose);
#endif
            _previousPosition = position;
            _previousRotation = rotation;
        }
    }
}
