using UnityEngine;

namespace StylizedCharacters
{
    /// <summary>Solves a limb in rig coordinates so avatar scale does not change the segment lengths.</summary>
    public static class TwoBonePose
    {
        public static Quaternion Rotation(Transform space, Transform bone) => Quaternion.Inverse(space.rotation) * bone.rotation;

        public static void SetRotation(Transform space, Transform bone, Quaternion rotation) => bone.rotation = space.rotation * rotation;

        public static void Rotate(Transform space, Transform bone, Quaternion delta) =>
            SetRotation(space, bone, delta * Rotation(space, bone));

        /// <summary>Turns the root and middle joints toward a rig-local target and bend direction.</summary>
        public static bool Solve(Transform space, Transform root, Transform mid, Transform end,
            Vector3 localTarget, Vector3 localPole)
        {
            var origin = space.InverseTransformPoint(root.position);
            var upper = space.InverseTransformPoint(mid.position) - origin;
            var lower = space.InverseTransformPoint(end.position) - origin - upper;
            float upperLength = upper.magnitude, lowerLength = lower.magnitude;
            const float margin = .0001f;
            if (Mathf.Min(upperLength, lowerLength) <= margin) return false;

            var reach = localTarget - origin;
            var axis = reach.sqrMagnitude > margin * margin ? reach.normalized : (upper + lower).normalized;
            if (axis == Vector3.zero) axis = upper.normalized;
            float distance = Mathf.Clamp(reach.magnitude, Mathf.Abs(upperLength - lowerLength) + margin,
                upperLength + lowerLength - margin);
            float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2 * distance);
            var bend = Vector3.ProjectOnPlane(localPole, axis);
            if (bend.sqrMagnitude < margin * margin) bend = Vector3.ProjectOnPlane(upper, axis);
            if (bend.sqrMagnitude < margin * margin)
                bend = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right);
            var desiredUpper = along * axis + bend.normalized * Mathf.Sqrt(Mathf.Max(0, upperLength * upperLength - along * along));
            Rotate(space, root, Quaternion.FromToRotation(upper, desiredUpper));

            var midPoint = space.InverseTransformPoint(mid.position);
            var desiredLower = origin + axis * distance - midPoint;
            lower = space.InverseTransformPoint(end.position) - midPoint;
            Rotate(space, mid, Quaternion.FromToRotation(lower, desiredLower));
            return true;
        }
    }
}
