using UnityEngine;
using UnityEngine.Rendering;

namespace AnimeCS.Play
{
    internal sealed class SmokeParticleView : ISmokeFieldView
    {
        private static readonly int Origin = Shader.PropertyToID("_SmokeOrigin");
        private static readonly int Clock = Shader.PropertyToID("_SmokeTime");
        private static readonly int Entry = Shader.PropertyToID("_HoleA");
        private static readonly int Exit = Shader.PropertyToID("_HoleB");
        private Vector3 _source;
        private float _radius;
        private readonly UtilityViewPool _pool;
        private readonly GameObject _root;
        private readonly ParticleSystem _system;
        private readonly ParticleSystemRenderer _renderer;
        private readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();
        private readonly ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[SmokeField.Count];

        internal SmokeParticleView(Transform parent, UtilityViewPool pool)
        {
            _pool=pool;
            _root = new GameObject("烟幕粒子");
            _root.transform.SetParent(parent, true);
            _system = _root.AddComponent<ParticleSystem>();
            _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = SmokeField.Count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = _system.emission;
            emission.enabled = false;
            var shape = _system.shape;
            shape.enabled = false;
            _renderer = _root.GetComponent<ParticleSystemRenderer>();
            _renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _renderer.sortMode = ParticleSystemSortMode.Distance;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
        }

        internal void Show(Vector3 source,float radius,Material material)
        {
            _source=source; _radius=radius;
            _root.transform.position=source;
            _root.SetActive(true);
            _properties.Clear();
            _renderer.SetPropertyBlock(null);
            _renderer.sharedMaterial=material;
            _renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * (radius * 5f + 1f));
            _properties.SetVector(Origin, source);
        }

        public void SetLobe(int index, Vector3 offset, float size, float rotation)
        {
            float light = .65f + .19f * offset.y / _radius + .035f * Mathf.Sin(index * 2.1f);
            _particles[index] = new ParticleSystem.Particle
            {
                startLifetime = 100, remainingLifetime = 99, randomSeed = (uint)(index + 1), rotation = rotation,
                position = _source + offset, startSize = size * 2.5f,
                // The vertex shader reads the lobe index from the alpha byte.
                startColor = new Color(light * 1.04f, light, light * .96f, index / 255f)
            };
        }

        public void Start()
        {
            _system.Play();
            _system.Pause();
            _system.SetParticles(_particles, _particles.Length);
        }

        public void SetState(float seconds, float growth, float opacity, Vector4[] entries, Vector4[] exits)
        {
            _properties.SetVector(Clock, new Vector4(seconds, growth, opacity, 0));
            _properties.SetVectorArray(Entry, entries);
            _properties.SetVectorArray(Exit, exits);
            _renderer.SetPropertyBlock(_properties);
        }

        public void Dispose()
        {
            _system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            _pool.Return(this,_root);
        }
    }
}
