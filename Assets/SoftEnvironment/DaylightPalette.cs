using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SoftEnvironment
{
    [CreateAssetMenu(menuName = "Rendering/Soft Environment/Daylight Palette")]
    public sealed class DaylightPalette : ScriptableObject
    {
        [SerializeField] private Material _skybox;
        [SerializeField] private Color _sunlight = new(1f, .93f, .83f);
        [SerializeField, Min(0)] private float _intensity = 1.48f;
        [SerializeField, Range(0, 1)] private float _shadowStrength = .76f;
        [SerializeField] private Color _skyLight = new(.72f, .84f, 1f);
        [SerializeField] private Color _horizonLight = new(.74f, .77f, .87f);
        [SerializeField] private Color _groundLight = new(.57f, .50f, .48f);
        [SerializeField] private Color _haze = new(.83f, .87f, .92f);
        [SerializeField, Min(0)] private float _hazeDensity = .0045f;
        [Header("New or rebuilt lighting scenes")]
        [SerializeField] private Vector3 _bakeSunEuler = new(48f, -33f, 0);
        [SerializeField, Min(0)] private float _bounceIntensity = 1;

        public void PrepareSunForBake(Light sun)
        {
            ApplyToActiveScene(sun);
            sun.transform.rotation = Quaternion.Euler(_bakeSunEuler);
            sun.bounceIntensity = _bounceIntensity;
        }

        /// <summary>Apply the palette to the active scene, retaining the authored sun direction and baked GI.</summary>
        public void ApplyToActiveScene(Light sun)
        {
            if (!sun || sun.type != LightType.Directional || sun.gameObject.scene != SceneManager.GetActiveScene())
                throw new System.ArgumentException("Select a directional light in the active scene.", nameof(sun));
            RenderSettings.sun = sun;
            sun.color = _sunlight;
            sun.intensity = _intensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = _shadowStrength;
            if (_skybox) RenderSettings.skybox = _skybox;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = _skyLight;
            RenderSettings.ambientEquatorColor = _horizonLight;
            RenderSettings.ambientGroundColor = _groundLight;
            RenderSettings.fogColor = _haze;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = _hazeDensity;
            RenderSettings.fog = _hazeDensity > 0;
        }
    }
}
