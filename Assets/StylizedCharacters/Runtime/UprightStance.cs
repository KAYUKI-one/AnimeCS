using UnityEngine;

namespace StylizedCharacters
{
    /// <summary>Relaxes standing knee flexion while keeping the evaluated foot positions and ankle rotations.</summary>
    public sealed class UprightStance
    {
        private readonly Transform _basis, _hips;
        private readonly Leg _left, _right;
        private Vector3 _hipsBefore;
        private bool _applied;

        public UprightStance(Animator animator)
        {
            _basis = animator.transform;
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _left = new Leg(animator, false);
            _right = new Leg(animator, true);
        }

        public void Apply(float weight, float worldGroundDrop = 0)
        {
            if (_applied || weight <= 0 || !_hips || !_left.Ready || !_right.Ready) return;
            float reserve = _basis.InverseTransformVector(Vector3.up * worldGroundDrop).magnitude;
            float lift = Mathf.Min(.063f * Mathf.Clamp01(weight), Mathf.Min(_left.Headroom(_basis), _right.Headroom(_basis)) - reserve);
            if (lift < .001f) return;
            _left.Remember(_basis);
            _right.Remember(_basis);
            _hipsBefore = _hips.localPosition;
            _hips.position += _basis.TransformVector(Vector3.up * lift);
            _left.Solve(_basis);
            _right.Solve(_basis);
            _applied = true;
        }

        public void Restore()
        {
            if (!_applied) return;
            _applied = false;
            _hips.localPosition = _hipsBefore;
            _left.Restore();
            _right.Restore();
        }

        private sealed class Leg
        {
            private readonly Transform _thigh, _knee, _foot;
            private Vector3 _target, _pole;
            private Quaternion _upperBefore, _midBefore, _footBefore, _footWorld;
            internal bool Ready => _thigh && _knee && _foot;

            internal Leg(Animator animator, bool left)
            {
                _thigh = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                _knee = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                _foot = animator.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            }

            internal float Headroom(Transform basis)
            {
                var upper = basis.InverseTransformVector(_knee.position - _thigh.position);
                var lower = basis.InverseTransformVector(_foot.position - _knee.position);
                var span = upper + lower;
                float reach = (upper.magnitude + lower.magnitude) * .991f;
                return Mathf.Sqrt(Mathf.Max(0, reach * reach - span.x * span.x - span.z * span.z)) - Mathf.Abs(span.y);
            }

            internal void Remember(Transform basis)
            {
                _target = basis.InverseTransformPoint(_foot.position);
                _pole = basis.InverseTransformVector(_knee.position - _thigh.position);
                _upperBefore = _thigh.localRotation;
                _midBefore = _knee.localRotation;
                _footBefore = _foot.localRotation;
                _footWorld = _foot.rotation;
            }

            internal void Solve(Transform basis)
            {
                TwoBonePose.Solve(basis, _thigh, _knee, _foot, _target, _pole);
                _foot.rotation = _footWorld;
            }

            internal void Restore()
            {
                _thigh.localRotation = _upperBefore;
                _knee.localRotation = _midBefore;
                _foot.localRotation = _footBefore;
            }
        }
    }
}
