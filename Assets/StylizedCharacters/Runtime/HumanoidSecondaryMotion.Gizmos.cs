#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace StylizedCharacters
{
    public sealed partial class HumanoidSecondaryMotion
    {
        [SerializeField, Tooltip("In Play Mode, select this character and enable Scene Gizmos to inspect its spring tips.")]
        private bool showSpringDebug;

        private struct DebugTip
        {
            public Vector3 previous, current;
            public float distance;
        }

        private DebugTip[] _debugTips;
        private int _largestTip = -1;

        private void SampleDebug(bool resetting)
        {
            if (!showSpringDebug)
            {
                _debugTips = null;
                return;
            }
            resetting |= _debugTips == null;
            _debugTips ??= new DebugTip[_joints.Count];
            _largestTip = -1;
            var worldToModel = transform.worldToLocalMatrix;
            for (int i = 0; i < _joints.Count; i++)
            {
                ref var sample = ref _debugTips[i];
                var current = worldToModel.MultiplyPoint3x4(_joints[i].DisplayedTip);
                sample.previous = resetting ? current : sample.current;
                sample.current = current;
                sample.distance = transform.TransformVector(current - sample.previous).magnitude;
                if (_largestTip < 0 || sample.distance > _debugTips[_largestTip].distance) _largestTip = i;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!showSpringDebug || _debugTips == null) return;
            var oldColor = Handles.color;
            for (int i = 0; i < _joints.Count; i++)
            {
                var spring = _joints[i].Spring;
                if (!spring) continue;
                spring.MarkCollidersForDrawing();
                var tip = _debugTips[i];
                var worldTip = transform.TransformPoint(tip.current);
                Handles.color = i == _largestTip ? Color.red : Color.cyan;
                Handles.DrawLine(spring.transform.position, worldTip);
                Handles.DrawLine(transform.TransformPoint(tip.previous), worldTip);
                Handles.Label(worldTip, $"{spring.name}  {tip.distance * 1000:F1} mm");
            }
            Handles.color = oldColor;
        }
    }
}
#endif
