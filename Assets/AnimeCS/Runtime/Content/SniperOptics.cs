using System;
using UnityEngine;

namespace AnimeCS.Content
{
    [Serializable]
    public sealed class SniperOptics
    {
        [Min(1f)] public float lowPower = 1.5f, highPower = 2.5f;
        [Min(.02f)] public float acquireSeconds = .15f;
        [Range(.15f, 1f)] public float speedFraction = .5f;
        [Min(0f), Tooltip("静止腰射 / 稳定开镜，角度半径")] public float hipDegrees = 4.62f, scopedDegrees = .10f;
        [Min(0f)] public float runPenalty = 10.1f, airPenalty = 9.9f;

        public float Power(int level) => level == 2 ? highPower : level == 1 ? lowPower : 1f;

        public float Spread(float speedFraction, float crouch, bool grounded, float settled)
        {
            float posture = Mathf.Lerp(1f, .75f, Mathf.Clamp01(crouch));
            float baseRadius = Mathf.Lerp(hipDegrees, scopedDegrees, settled) * posture;
            float movement = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(speedFraction));
            return baseRadius + movement * runPenalty + (grounded ? 0f : airPenalty);
        }
    }
}

