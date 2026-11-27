using UnityEngine;

namespace StylizedCharacters
{
    /// <summary>Provides a downward ground sample with a world-space point and unit normal.</summary>
    public interface IFootSurface
    {
        bool TryFind(Vector3 origin, float distance, out Vector3 point, out Vector3 normal);
    }

    public sealed class PhysicsFootSurface : IFootSurface
    {
        public static readonly PhysicsFootSurface Shared = new();

        public bool TryFind(Vector3 origin, float distance, out Vector3 point, out Vector3 normal)
        {
            bool found = Physics.Raycast(origin, Vector3.down, out var hit, distance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            point = hit.point;
            normal = hit.normal;
            return found;
        }
    }

    /// <summary>Adjusts the grounded portions of the evaluated gait. Restore the sampled rotations before animation evaluation.</summary>
    public sealed class GroundedFeet
    {
        private readonly Foot _left, _right;

        public GroundedFeet(Foot left, Foot right) { _left = left; _right = right; }

        /// <summary>Follows contact surfaces for the current frame. Floor height is the reference for determining which feet are planted.</summary>
        public void Apply(IFootSurface surface, float floorHeight, bool grounded)
        {
            if (!grounded) { Restore(); return; }
            _left?.Apply(surface, floorHeight);
            _right?.Apply(surface, floorHeight);
        }

        public void Restore()
        {
            _left?.Restore();
            _right?.Restore();
        }

        public sealed class Foot
        {
            private const float ProbeUp = .25f, ProbeDown = .27f, SoleClearance = .006f;
            private readonly Transform _space, _thigh, _knee, _ankle;
            private readonly Vector3 _sole, _soleUp;
            private Quaternion _thighPose, _kneePose, _anklePose;
            private bool _solved;

            /// <summary>Binds an animated leg. Sole position and normal are in ankle-local coordinates.</summary>
            public Foot(Transform space, Transform thigh, Transform knee, Transform ankle, Vector3 sole, Vector3 soleUp)
            {
                _space = space;
                _thigh = thigh;
                _knee = knee;
                _ankle = ankle;
                _sole = sole;
                _soleUp = soleUp.normalized;
            }

            internal void Apply(IFootSurface surface, float floorHeight)
            {
                if (_solved || !_space || !_thigh || !_knee || !_ankle) return;
                var sole = _ankle.TransformPoint(_sole);
                float plant = 1f - Mathf.InverseLerp(.045f, .14f, sole.y - floorHeight);
                if (plant <= 0 || !surface.TryFind(sole + Vector3.up * ProbeUp, ProbeUp + ProbeDown, out var ground, out var normal) ||
                    normal.y < .65f) return;

                var anklePosition = _space.InverseTransformPoint(_ankle.position);
                var soleOffset = _space.InverseTransformPoint(sole) - anklePosition;
                var ankleRotation = TwoBonePose.Rotation(_space, _ankle);
                // Plane normals are covectors. Transpose removes the parent scale when comparing soles on slopes.
                var localNormal = _space.localToWorldMatrix.transpose.MultiplyVector(normal).normalized;
                var tilt = Quaternion.FromToRotation(ankleRotation * _soleUp, localNormal);
                tilt = Quaternion.Slerp(Quaternion.identity, Quaternion.RotateTowards(Quaternion.identity, tilt, 40f), plant);
                var desiredSole = _space.InverseTransformPoint(Vector3.Lerp(sole, ground + normal * SoleClearance, plant));
                var pole = _space.InverseTransformVector(_knee.position - _thigh.position);
                _thighPose = _thigh.localRotation;
                _kneePose = _knee.localRotation;
                _anklePose = _ankle.localRotation;
                if (!TwoBonePose.Solve(_space, _thigh, _knee, _ankle, desiredSole - tilt * soleOffset, pole)) return;
                TwoBonePose.SetRotation(_space, _ankle, tilt * ankleRotation);
                _solved = true;
            }

            internal void Restore()
            {
                if (!_solved) return;
                _solved = false;
                if (_thigh) _thigh.localRotation = _thighPose;
                if (_knee) _knee.localRotation = _kneePose;
                if (_ankle) _ankle.localRotation = _anklePose;
            }
        }
    }
}
