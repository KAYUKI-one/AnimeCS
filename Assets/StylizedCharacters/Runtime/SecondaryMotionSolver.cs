using System.Collections.Generic;
using Unity.Animations.SpringBones;
using UnityEngine;

namespace StylizedCharacters
{
    // The generated rigs use their skinned segment's actual affine frame. The package's
    // standalone manager still owns rigs that ship with an authored spring setup.
    internal sealed class SecondaryMotionSolver
    {
        internal sealed class Joint
        {
            internal SpringBone Spring;
            internal SecondaryMotionProfile.SpringSettings Settings;
            internal Quaternion Rest;
            internal Vector3 LocalTip;
            internal int Parent = -1, Root;
            internal int ChainDepth = 1;
            internal float Reach;
            internal Matrix4x4 OldParent, NewParent, Frame;
            internal Vector3 OldPosition, NewPosition, OldScale, NewScale;
            internal Vector3 Tip, Velocity;
            internal Quaternion Previous, Solved, Displayed;
            internal Contact[] Contacts;
            internal readonly List<(Joint joint, Vector3 separation)> Neighbors = new();
            internal Vector3 DisplayedTip => Spring.transform.TransformPoint(LocalTip);
        }

        internal sealed class Contact
        {
            private readonly Behaviour _collider;
            private readonly float _height, _radius;
            private Matrix4x4 _previous, _current, _frame, _inverse;
            private bool _active;

            internal Contact(SpringCapsuleCollider capsule) : this(capsule, capsule.height, capsule.radius) { }
            internal Contact(SpringSphereCollider sphere) : this(sphere, 0, sphere.radius) { }

            private Contact(Behaviour collider, float height, float radius)
            {
                _collider = collider;
                _height = height;
                _radius = radius;
            }

            internal void Capture(bool reset)
            {
                _previous = reset ? _collider.transform.localToWorldMatrix : _current;
                _current = _collider.transform.localToWorldMatrix;
                _active = _collider.isActiveAndEnabled;
            }

            internal void Sample(float fraction)
            {
                _frame = Blend(_previous, _current, fraction);
                _inverse = _frame.inverse;
            }

            internal Vector3 Push(Vector3 point, float radius, Vector3 fallback)
            {
                if (!_active) return Vector3.zero;
                var local = _inverse.MultiplyPoint3x4(point);
                var center = new Vector3(0, Mathf.Clamp(local.y, 0, _height), 0);
                var offset = local - center;
                float distance = offset.magnitude;
                var normal = distance > 1e-5f ? offset / distance : _inverse.MultiplyVector(fallback).normalized;
                if (normal == Vector3.zero) normal = Vector3.right;
                var outward = _frame.MultiplyVector(normal);
                float metersPerUnit = outward.magnitude;
                float depth = (_radius - distance) * metersPerUnit + radius;
                return depth > 0 && metersPerUnit > 1e-6f ? outward * (depth / metersPerUnit) : Vector3.zero;
            }
        }

        private readonly List<Joint> _joints;
        private readonly List<Contact> _contacts;
        private float _accumulator, _age;

        internal SecondaryMotionSolver(List<Joint> joints, List<Contact> contacts)
        {
            _joints = joints;
            _contacts = contacts;
        }

        internal void Reset(bool preservePose)
        {
            _age = _accumulator = 0;
            foreach (var joint in _joints)
            {
                if (!preservePose) joint.Displayed = Quaternion.identity;
                joint.Spring.transform.localRotation = joint.Rest * joint.Displayed;
            }
            Capture(true);
            foreach (var joint in _joints)
            {
                joint.Tip = joint.DisplayedTip;
                joint.Velocity = Vector3.zero;
                joint.Previous = joint.Solved = joint.Displayed;
                joint.Spring.SetExternalPoseTip(joint.Tip);
            }
        }

        internal void Advance(float elapsed, float step, int iterations, float fadeSeconds, Vector3 gravity)
        {
            Capture(false);
            float timeToStep = step - _accumulator;
            _accumulator += elapsed;
            while (_accumulator + 1e-6f >= step)
            {
                _age += step;
                float weight = fadeSeconds > 0 ? Mathf.Clamp01(_age / fadeSeconds) : 1f;
                Simulate(step, Mathf.Clamp01(timeToStep / elapsed), iterations, gravity, weight);
                timeToStep += step;
                _accumulator = Mathf.Max(0, _accumulator - step);
            }

            // Interpolate the local spring rotations, then project them against this render pose.
            foreach (var contact in _contacts) contact.Sample(1);
            float alpha = _accumulator / step;
            float renderWeight = fadeSeconds > 0 ? Mathf.Clamp01((_age + _accumulator) / fadeSeconds) : 1f;
            foreach (var joint in _joints)
            {
                var bone = joint.Spring.transform;
                var parent = bone.parent ? bone.parent.localToWorldMatrix : Matrix4x4.identity;
                var restFrame = parent * Matrix4x4.TRS(bone.localPosition, joint.Rest, Vector3.one);
                var rotation = Quaternion.Slerp(joint.Previous, joint.Solved, alpha);
                joint.Displayed = Constrain(joint, restFrame, Vector3.Scale(bone.localScale, joint.LocalTip),
                    rotation, joint.Displayed, elapsed, iterations, renderWeight);
                bone.localRotation = joint.Rest * joint.Displayed;
                joint.Spring.SetExternalPoseTip(joint.DisplayedTip);
            }
        }

        private void Capture(bool reset)
        {
            foreach (var contact in _contacts) contact.Capture(reset);
            foreach (var joint in _joints)
            {
                var bone = joint.Spring.transform;
                var parent = bone.parent ? bone.parent.localToWorldMatrix : Matrix4x4.identity;
                joint.OldParent = reset ? parent : joint.NewParent;
                joint.NewParent = parent;
                joint.OldPosition = reset ? bone.localPosition : joint.NewPosition;
                joint.NewPosition = bone.localPosition;
                joint.OldScale = reset ? bone.localScale : joint.NewScale;
                joint.NewScale = bone.localScale;
            }
        }

        private void Simulate(float step, float fraction, int iterations, Vector3 gravity, float weight)
        {
            foreach (var contact in _contacts) contact.Sample(fraction);
            foreach (var joint in _joints)
            {
                var parent = joint.Parent >= 0 ? _joints[joint.Parent].Frame : Blend(joint.OldParent, joint.NewParent, fraction);
                var position = Vector3.LerpUnclamped(joint.OldPosition, joint.NewPosition, fraction);
                var scale = Vector3.LerpUnclamped(joint.OldScale, joint.NewScale, fraction);
                var tip = Vector3.Scale(scale, joint.LocalTip);
                var unscaledFrame = parent * Matrix4x4.TRS(position, joint.Rest, Vector3.one);
                var carried = unscaledFrame.MultiplyPoint3x4(joint.Solved * tip);
                var restTip = unscaledFrame.MultiplyPoint3x4(tip);
                var spring = joint.Settings;
                float travel = spring.travelSpeed * step;
                float retention = Mathf.Pow(1 - spring.dampingAt60Hz, step * 60f);
                var inertial = Vector3.ClampMagnitude(joint.Tip - carried, travel) * spring.inertia;
                var forces = (gravity * weight + spring.stiffness * (restTip - carried)) * (step * step);
                var predicted = carried + Vector3.ClampMagnitude(joint.Velocity * retention + inertial * weight + forces, travel);
                foreach (var (neighbor, separation) in joint.Neighbors)
                {
                    var root = _joints[joint.Root];
                    float spacing = Blend(root.OldParent, root.NewParent, fraction).MultiplyVector(separation).magnitude;
                    var offset = predicted - neighbor.Tip;
                    float distance = offset.magnitude;
                    if (distance > 1e-5f)
                        predicted -= offset * (.4f * (distance - Mathf.Clamp(distance, spacing * .78f, spacing * 1.22f)) / distance) * weight;
                }
                var aim = Aim(unscaledFrame.inverse, tip, predicted, joint.Solved);
                joint.Previous = joint.Solved;
                joint.Solved = Constrain(joint, unscaledFrame, tip, aim, joint.Solved, step, iterations, weight);
                joint.Frame = parent * Matrix4x4.TRS(position, joint.Rest * joint.Solved, scale);
                joint.Tip = joint.Frame.MultiplyPoint3x4(joint.LocalTip);
                joint.Velocity = Quaternion.Angle(aim, joint.Solved) > .25f
                    ? Vector3.zero : Vector3.ClampMagnitude(joint.Tip - carried, travel);
            }
        }

        private static Quaternion Constrain(Joint joint, Matrix4x4 frame, Vector3 localTip, Quaternion aim,
            Quaternion previous, float elapsed, int iterations, float weight)
        {
            var inverse = frame.inverse;
            float maxTurn = Mathf.Min(12f, joint.Settings.angularSpeed * elapsed);
            // Each joint can move the tips of every segment downstream. Share the
            // displacement allowance across the chain to bound that combined motion.
            if (joint.Reach > 1e-5f)
                maxTurn = Mathf.Min(maxTurn, Mathf.Rad2Deg * joint.Settings.travelSpeed * elapsed / joint.Reach);
            float swing = joint.Settings.swingDegrees;
            Quaternion Limit(Quaternion target) => Quaternion.RotateTowards(previous,
                Quaternion.RotateTowards(Quaternion.identity, target, swing), maxTurn);
            var rotation = Limit(aim);
            var tip = frame.MultiplyPoint3x4(rotation * localTip);
            var fallback = frame.MultiplyVector(Vector3.right);
            var best = rotation;
            float bestDepth = float.PositiveInfinity;
            for (int i = 0; i <= iterations; i++)
            {
                var push = Vector3.zero;
                foreach (var contact in joint.Contacts)
                {
                    var correction = contact.Push(tip, joint.Spring.radius, fallback);
                    if (correction.sqrMagnitude > push.sqrMagnitude) push = correction;
                }
                float depth = push.sqrMagnitude;
                if (depth < bestDepth)
                {
                    best = rotation;
                    bestDepth = depth;
                }
                if (depth < 1e-10f || weight <= 0) break;
                rotation = Limit(Aim(inverse, localTip, tip + push * (1.2f * weight), rotation));
                tip = frame.MultiplyPoint3x4(rotation * localTip);
            }
            return best;
        }

        private static Quaternion Aim(Matrix4x4 inverse, Vector3 restDirection, Vector3 tip, Quaternion fallback)
        {
            var direction = inverse.MultiplyPoint3x4(tip);
            float length = direction.sqrMagnitude;
            return length > 1e-10f && float.IsFinite(length)
                ? Quaternion.FromToRotation(restDirection, direction) : fallback;
        }

        private static Matrix4x4 Blend(Matrix4x4 from, Matrix4x4 to, float fraction)
        {
            if (fraction >= 1) return to;
            var matrix = new Matrix4x4();
            for (int i = 0; i < 4; i++) matrix.SetColumn(i, Vector4.LerpUnclamped(from.GetColumn(i), to.GetColumn(i), fraction));
            return matrix;
        }
    }
}
