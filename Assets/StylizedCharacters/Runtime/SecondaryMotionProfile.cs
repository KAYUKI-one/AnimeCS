using System;
using UnityEngine;

namespace StylizedCharacters
{
    [CreateAssetMenu(menuName = "Stylized Characters/Secondary Motion Profile")]
    public sealed class SecondaryMotionProfile : ScriptableObject
    {
        internal enum Part { Hair, Skirt, Sleeve, Ribbon }

        [Serializable]
        internal struct SpringSettings
        {
            [Min(0)] public float stiffness;
            [Range(0, 1)] public float dampingAt60Hz;
            [Range(0, 1)] public float inertia;
            [Range(0, 75)] public float swingDegrees;
            [Min(0)] public float angularSpeed;
            [Min(0)] public float travelSpeed;
            [Min(0)] public float radiusScale;

            public SpringSettings(float stiffness, float damping, float inertia, float swing, float angularSpeed,
                float travelSpeed, float radiusScale = .13f)
            {
                this.stiffness = stiffness;
                dampingAt60Hz = damping;
                this.inertia = inertia;
                swingDegrees = swing;
                this.angularSpeed = angularSpeed;
                this.travelSpeed = travelSpeed;
                this.radiusScale = radiusScale;
            }
        }

        [SerializeField, Range(60, 120)] private int simulationRate = 120;
        [SerializeField, Range(1, 8)] private int iterations = 4;
        [SerializeField, Min(0)] private float fadeInSeconds = .12f;
        [SerializeField] private Vector3 gravity = new(0, -.75f, 0);
        [SerializeField] private SpringSettings hair = Hair;
        [SerializeField] private SpringSettings skirt = Skirt;
        [SerializeField] private SpringSettings sleeve = Sleeve;
        [SerializeField] private SpringSettings ribbon = Ribbon;

        internal static SpringSettings Hair => new(260f, .33f, .62f, 34f, 420f, 3.4f);
        internal static SpringSettings Skirt => new(370f, .47f, .48f, 30f, 350f, 2.5f);
        internal static SpringSettings Sleeve => new(410f, .50f, .39f, 24f, 330f, 2f);
        internal static SpringSettings Ribbon => new(285f, .32f, .59f, 41f, 410f, 3.6f);
        internal float Step => 1f / Mathf.Clamp(simulationRate, 60, 120);
        internal int Iterations => Mathf.Clamp(iterations, 1, 8);
        internal float FadeIn => fadeInSeconds;
        internal Vector3 Gravity => gravity;
        internal SpringSettings For(Part part) => part switch
        {
            Part.Hair => hair,
            Part.Sleeve => sleeve,
            Part.Ribbon => ribbon,
            _ => skirt
        };
    }
}
