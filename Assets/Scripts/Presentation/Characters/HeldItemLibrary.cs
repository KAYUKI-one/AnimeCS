using System;
using System.Collections.Generic;
using AnimeCS.Match;
using UnityEngine;

namespace AnimeCS.Play
{
    [Serializable]
    public struct HandlingKey
    {
        [Range(0, 1)] public float time;
        public Vector3 offset, angles;
        public float hinge;

        public HandlingKey(float time, Vector3 offset, Vector3 angles, float hinge = 0)
        {
            this.time = time;
            this.offset = offset;
            this.angles = angles;
            this.hinge = hinge;
        }
    }

    [Serializable]
    public sealed class HandlingTrack
    {
        [Min(.01f)] public float seconds = 1;
        [Tooltip("按归一化时间排列。位移为米，转角为度。")] public HandlingKey[] keys;

        public bool Sample(float elapsed, out HandlingKey sample)
        {
            sample = default;
            if (keys == null || keys.Length == 0 || elapsed < 0 || elapsed >= seconds || seconds <= 0) return false;
            float time = elapsed / seconds;
            sample = keys[0];
            for (int i = 1; i < keys.Length; i++)
            {
                var next = keys[i];
                if (time <= next.time)
                {
                    float t = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(sample.time, next.time, time));
                    sample = new HandlingKey(time, Vector3.Lerp(sample.offset, next.offset, t),
                        Vector3.Lerp(sample.angles, next.angles, t), Mathf.Lerp(sample.hinge, next.hinge, t));
                    return true;
                }
                sample = next;
            }
            return true;
        }
    }

    [Serializable]
    public sealed class HeldItemActions
    {
        public string itemId;
        public HandlingTrack inspect, light, heavy, rear;
        public AudioClip inspectSound, activateSound, heavySound, rearSound;

        public HandlingTrack Attack(MeleeAction action) => action switch
        {
            MeleeAction.Heavy => heavy,
            MeleeAction.Rear => rear,
            _ => light
        };
    }

    [CreateAssetMenu(menuName = "AnimeCS/持物表现库")]
    public sealed class HeldItemLibrary : ScriptableObject
    {
        public const string ObjectiveId = "held.objective";
        public List<HeldItemActions> items = new();
        public AudioClip devicePulse, deviceBurst;
        private Dictionary<string, HeldItemActions> _index;

        public HeldItemActions Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_index == null)
            {
                _index = new Dictionary<string, HeldItemActions>(StringComparer.Ordinal);
                foreach (var item in items)
                    if (item != null && !string.IsNullOrWhiteSpace(item.itemId)) _index.TryAdd(item.itemId, item);
            }
            return _index.TryGetValue(id, out var actions) ? actions : null;
        }

        private void OnValidate() => _index = null;
        private void OnEnable() => _index = null;
    }
}
