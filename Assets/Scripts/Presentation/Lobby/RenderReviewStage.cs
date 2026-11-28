using System;
using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimeCS.Play
{
    internal sealed class RenderReviewStage : IDisposable
    {
        internal enum Motion { Idle, Run, Crouch, Walk, Pose }
        internal enum Lighting { Studio, Daylight, Rim }

        private readonly Scene _scene, _previousScene;
        private readonly GameObject _root;
        private readonly Camera _camera;
        private readonly Light _key, _fill, _rim;
        private readonly Material _floor, _plinth, _accentWall, _trimMetal;
        private readonly Mesh _backdrop;
        private readonly VolumeProfile _environment, _grading;
        private readonly GradientSky _sky;
        private readonly Bloom _bloom;
        private readonly ColorAdjustments _colorAdjustments;
        private readonly MaterialPropertyBlock _surfaceProperties = new();
        private float _presetYaw, _presetPitch, _yawOffset, _pitchOffset;
        private float _outlineScale = 1f, _rimScale = 1f, _presetRim;
        internal float BloomIntensity => _bloom ? _bloom.intensity.value : 0f;
        internal float Exposure => _colorAdjustments ? _colorAdjustments.postExposure.value : 0f;
        private readonly int _layer;
        private readonly MatchPresentation _style;
        private readonly RuntimeAnimatorController _showcaseController;
        private Animator _avatarAnimator;
        private GameObject _avatar;
        private CharacterRig _rig;
        private RenderTexture _texture;
        private float _yaw, _pitch, _distance;
        private bool _close;
        private float _turn, _targetTurn, _elapsed, _crouch;
        internal Motion Action { get; set; }
        internal bool Paused { get; set; }

        internal RenderReviewStage(MatchPresentation style)
        {
            _style = style;
            _showcaseController = Resources.Load<RuntimeAnimatorController>("AnimeShowcase");
            _layer = LayerMask.NameToLayer("Render Review");
            if (_layer < 0) throw new InvalidOperationException("缺少 Render Review 图层。");
            _previousScene = SceneManager.GetActiveScene();
            _scene = SceneManager.CreateScene("Render Review");
            SceneManager.SetActiveScene(_scene);
            _root = new GameObject("Render Review Stage") { layer = _layer };
            _camera = Add("Render Review Camera").AddComponent<Camera>();
            _camera.enabled = false;
            _camera.depth = -10f;
            _camera.clearFlags = CameraClearFlags.Skybox;
            _camera.cullingMask = 1 << _layer;
            _camera.allowMSAA = false;
            _camera.nearClipPlane = .035f;
            _camera.farClipPlane = 45f;
            _camera.fieldOfView = 34f;
            var cameraData = _camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.volumeLayerMask = 1 << _layer;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            _floor = new Material(lit) { name = "Review floor" };
            _plinth = new Material(lit) { name = "Review platform" };
            _accentWall = new Material(lit) { name = "Review gallery wall" };
            _trimMetal = new Material(lit) { name = "Review trim metal" };
            _floor.SetFloat("_Smoothness", .16f);
            _plinth.SetFloat("_Smoothness", .28f);
            _accentWall.SetFloat("_Smoothness", .10f);
            _trimMetal.SetFloat("_Metallic", .75f);
            _trimMetal.SetFloat("_Smoothness", .62f);

            var backdrop = Add("Curved studio backdrop");
            _backdrop = CreateBackdrop();
            backdrop.AddComponent<MeshFilter>().sharedMesh = _backdrop;
            var backdropRenderer = backdrop.AddComponent<MeshRenderer>();
            backdropRenderer.sharedMaterial = _floor;
            backdropRenderer.shadowCastingMode = ShadowCastingMode.Off;
            backdropRenderer.renderingLayerMask = 3u;

            // Tiered architectural showcase pedestal + gallery colonnade (inspired by DanbaidongRP Suomi/Vepley showcase room)
            Surface(PrimitiveType.Cylinder, "Turntable base", Vector3.down * .065f, new Vector3(2.26f, .035f, 2.26f), _accentWall, receiveShadows: true);
            Surface(PrimitiveType.Cylinder, "Turntable trim", Vector3.down * .032f, new Vector3(1.98f, .008f, 1.98f), _trimMetal, receiveShadows: false);
            Surface(PrimitiveType.Cylinder, "Turntable platform", Vector3.down * .018f, new Vector3(1.90f, .018f, 1.90f), _plinth, receiveShadows: true);
            BuildGalleryStage();

            _key = Lamp("Key", LightType.Directional, Vector3.zero);
            _key.shadows = LightShadows.Soft;
            var keyData = _key.GetUniversalAdditionalLightData();
            keyData.customShadowLayers = true;
            keyData.shadowRenderingLayers = 1u << 1; // MainLightShadows
            keyData.renderingLayers = 1u;
            keyData.angularDiameter = 2.2f;
            _fill = Lamp("Fill", LightType.Point, new Vector3(-2.4f, 1.35f, 0.9f));
            var fillData = _fill.GetUniversalAdditionalLightData();
            fillData.shapeRadius = 0.85f;
            fillData.baseContribution = 0.09f;
            fillData.rimContribution = 0.12f;
            fillData.outlineContribution = 0f;
            _rim = Lamp("Rim", LightType.Point, new Vector3(-1.6f, 2.25f, -1.9f));
            var rimData = _rim.GetUniversalAdditionalLightData();
            rimData.shapeRadius = 0.35f;
            rimData.baseContribution = 0.0f;
            rimData.rimContribution = 1.0f;
            rimData.outlineContribution = 0.25f;
            RenderSettings.sun = _key;
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            var volume = Add("Studio environment").AddComponent<Volume>();
            volume.isGlobal = true;
            _environment = ScriptableObject.CreateInstance<VolumeProfile>();
            _environment.Add<VisualSky>(true).skyType.value = SkyType.Gradient;
            _sky = _environment.Add<GradientSky>(true);
            volume.sharedProfile = _environment;
            if (style.renderReviewGrade)
            {
                var grading = Add("Global Volume").AddComponent<Volume>();
                grading.isGlobal = true;
                grading.priority = 10f;
                grading.sharedProfile = style.renderReviewGrade;
                // Volume.profile clones each override; each showroom owns its grading controls.
                _grading = grading.profile;
                _grading.TryGet(out _bloom);
                _grading.TryGet(out _colorAdjustments);
            }
            SetLighting(Lighting.Studio);
            Frame(false);
        }

        private GameObject Add(string name)
        {
            var item = new GameObject(name) { layer = _layer };
            item.transform.SetParent(_root.transform, false);
            return item;
        }

        private Light Lamp(string name, LightType type, Vector3 position)
        {
            var lamp = Add(name).AddComponent<Light>();
            lamp.type = type;
            lamp.cullingMask = 1 << _layer;
            lamp.transform.localPosition = position;
            lamp.range = 9f;
            return lamp;
        }

        private void Surface(
            PrimitiveType primitive, string name, Vector3 position, Vector3 size, Material material,
            bool receiveShadows = true, bool castShadows = false, Vector3 eulerAngles = default)
        {
            var surface = GameObject.CreatePrimitive(primitive);
            surface.name = name;
            surface.layer = _layer;
            surface.transform.SetParent(_root.transform, false);
            surface.transform.localPosition = position;
            surface.transform.localEulerAngles = eulerAngles;
            surface.transform.localScale = size;
            var collider = surface.GetComponent<Collider>();
            collider.enabled = false;
            Object.Destroy(collider);
            var renderer = surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.renderingLayerMask = receiveShadows ? 3u : 1u;
        }

        private void BuildGalleryStage()
        {
            // Clean architectural showcase backdrop behind the character (-Z) so silhouettes & colored anime outlines
            // read with crisp depth and gallery atmosphere rather than floating in a black void.
            const float radius = 4.6f;
            const int pillarCount = 5;
            for (int i = 0; i < pillarCount; i++)
            {
                float angleDeg = Mathf.Lerp(-54f, 54f, i / (pillarCount - 1f));
                float rad = angleDeg * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Sin(rad) * radius, 2.05f, -Mathf.Cos(rad) * radius);
                var rot = new Vector3(0f, -angleDeg, 0f);
                Surface(PrimitiveType.Cube, $"Gallery Pillar {i}", pos, new Vector3(.34f, 4.3f, .34f), _plinth, true, true, rot);
                Surface(PrimitiveType.Cube, $"Gallery Pillar Trim {i}", new Vector3(pos.x, .36f, pos.z), new Vector3(.42f, .05f, .42f), _trimMetal, false, false, rot);
            }

            // Soft curved gallery wall panels behind the pillars (spaced cleanly so the luminous GradientSky shines through!)
            const int panelCount = 4;
            for (int i = 0; i < panelCount; i++)
            {
                float angleDeg = Mathf.Lerp(-40.5f, 40.5f, i / (panelCount - 1f));
                float rad = angleDeg * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Sin(rad) * (radius + .65f), 1.35f, -Mathf.Cos(rad) * (radius + .65f));
                var rot = new Vector3(0f, -angleDeg, 0f);
                Surface(PrimitiveType.Cube, $"Gallery Panel {i}", pos, new Vector3(1.85f, 2.7f, .18f), _accentWall, true, false, rot);
                Surface(PrimitiveType.Cube, $"Gallery Wainscot {i}", new Vector3(pos.x, 2.72f, pos.z), new Vector3(1.90f, .05f, .24f), _trimMetal, false, false, rot);
            }

            // Overhead gallery lintel casting a natural soft architectural light-beam angle across the backdrop
            Surface(PrimitiveType.Cube, "Gallery Lintel", new Vector3(0f, 4.28f, -radius + .15f), new Vector3(9.4f, .26f, .72f), _plinth, true, true);
            Surface(PrimitiveType.Cube, "Gallery Side Wing L", new Vector3(-4.8f, 1.45f, -1.8f), new Vector3(.22f, 2.9f, 3.6f), _accentWall, true, false, new Vector3(0f, -24f, 0f));
            Surface(PrimitiveType.Cube, "Gallery Side Wing R", new Vector3(4.8f, 1.45f, -1.8f), new Vector3(.22f, 2.9f, 3.6f), _accentWall, true, false, new Vector3(0f, 24f, 0f));
        }

        private static Mesh CreateBackdrop()
        {
            const int sides = 96, curve = 10, rings = curve + 3;
            var vertices = new Vector3[rings * sides];
            var indices = new int[(rings - 1) * sides * 6];
            for (int ring = 0; ring < rings; ring++)
            {
                float bend = (ring - 1f) / curve * Mathf.PI * .5f;
                float radius = ring == 0 ? .01f : ring == rings - 1 ? 12f : 8f + 4f * Mathf.Sin(bend);
                float height = ring == 0 ? -.095f : ring == rings - 1 ? 16f : 4f * (1f - Mathf.Cos(bend)) - .095f;
                for (int side = 0; side < sides; side++)
                {
                    int index = ring * sides + side;
                    float angle = side * (2f * Mathf.PI / sides);
                    vertices[index] = new Vector3(radius * Mathf.Cos(angle), height, radius * Mathf.Sin(angle));
                    if (ring == rings - 1) continue;
                    int next = ring * sides + (side + 1) % sides;
                    int triangle = index * 6;
                    // Left-handed CW winding: Cross(next - index, up - index) points +Y on floor and inward on wall.
                    indices[triangle] = index;
                    indices[triangle + 1] = next;
                    indices[triangle + 2] = index + sides;
                    indices[triangle + 3] = next;
                    indices[triangle + 4] = next + sides;
                    indices[triangle + 5] = index + sides;
                }
            }
            var mesh = new Mesh { name = "Curved studio backdrop", vertices = vertices, triangles = indices };
            mesh.RecalculateNormals();
            return mesh;
        }

        internal void Select(OutfitDefinition outfit)
        {
            AppearancePresenter.Retire(_avatar);
            _avatar = AppearancePresenter.NewRoot(outfit.DisplayName, _root.transform);
            var definition = outfit.Character;
            var body = AppearancePresenter.AddVisual(definition.Model, _avatar.transform, Vector3.zero);
            _rig = new CharacterRig(body, definition, _showcaseController ? _showcaseController : outfit.PoseController, true, outfit.Regions(false), _style.secondaryMotion);
            _rig.BindClothing(AppearancePresenter.AddVisual(outfit.Model, _avatar.transform, Vector3.zero));
            _avatarAnimator = body.GetComponentInChildren<Animator>(true);
            foreach (var item in _avatar.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = _layer;
            var projector = _avatar.AddComponent<PerObjectShadowProjector>();
            projector.farPlaneScale = 4f;
            projector.CollectRenderers();
            _turn = _targetTurn = _elapsed = _crouch = 0f;
            _avatar.SetActive(true);
            ApplySurfaceControls();
        }

        internal void SetLighting(Lighting lighting)
        {
            bool day = lighting == Lighting.Daylight;
            bool rim = lighting == Lighting.Rim;
            // Official DanbaidongRP SkySystem + 3-point lighting:
            // Control ambient SH irradiance via GradientSky.exposure (EV stops) with top-to-bottom color contrast,
            // pair with a mid-bright neutral studio cyc (so colored anime outlines read cleanly instead of vanishing into black!),
            // and place Key at 3/4 side-top + shadow-side Fill + subtle back Rim!
            _camera.backgroundColor = day ? new Color(.56f, .64f, .74f) : rim ? new Color(.10f, .13f, .22f) : new Color(.36f, .40f, .48f);
            _floor.color = day ? new Color(.72f, .75f, .80f) : rim ? new Color(.22f, .26f, .36f) : new Color(.58f, .61f, .68f);
            _plinth.color = day ? new Color(.84f, .85f, .88f) : rim ? new Color(.30f, .34f, .45f) : new Color(.72f, .74f, .80f);
            _accentWall.color = day ? new Color(.74f, .77f, .84f) : rim ? new Color(.20f, .24f, .36f) : new Color(.56f, .60f, .68f);
            _trimMetal.color = day ? new Color(.88f, .82f, .68f) : rim ? new Color(.44f, .74f, .88f) : new Color(.84f, .80f, .72f);
            // Gentle studio bounce emission on architectural backdrop so shadow-facing gallery panels stay airy slate-blue instead of pitch black!
            SetArchitecturalFill(_floor, day ? new Color(.14f, .16f, .20f) : rim ? new Color(.03f, .04f, .08f) : new Color(.09f, .11f, .15f));
            SetArchitecturalFill(_plinth, day ? new Color(.18f, .20f, .24f) : rim ? new Color(.04f, .05f, .10f) : new Color(.12f, .14f, .19f));
            SetArchitecturalFill(_accentWall, day ? new Color(.18f, .21f, .28f) : rim ? new Color(.04f, .06f, .12f) : new Color(.12f, .14f, .20f));
            RenderSettings.ambientLight = day ? new Color(.34f, .38f, .46f) : rim ? new Color(.14f, .16f, .24f) : new Color(.24f, .27f, .34f);
            _sky.skyIntensityMode.Override(SkyIntensityMode.Exposure);
            _sky.exposure.Override(day ? -1.15f : rim ? -1.95f : -1.65f);
            _sky.top.Override(day ? new Color(.44f, .58f, .82f) : rim ? new Color(.16f, .26f, .46f) : new Color(.28f, .35f, .50f));
            _sky.middle.Override(day ? new Color(.62f, .60f, .58f) : rim ? new Color(.22f, .18f, .30f) : new Color(.36f, .36f, .40f));
            _sky.bottom.Override(day ? new Color(.22f, .20f, .22f) : new Color(.10f, .10f, .14f));
            _key.color = day ? new Color(1f, .96f, .90f) : rim ? new Color(.98f, .95f, 1f) : new Color(1f, .97f, .94f);
            _key.intensity = day ? 1.70f : rim ? 1.25f : 1.50f;
            // 3/4 side-top angles (114° / 236° / 76°) sculpt 3D form softly without blasting straight-on white!
            _presetYaw = rim ? 76f : day ? 236f : 114f;
            _presetPitch = day ? 38f : 32f;
            UpdateKeyDirection();
            _fill.color = rim ? new Color(.88f, .58f, 1f) : new Color(.78f, .85f, 1f);
            _fill.intensity = rim ? 1.45f : 1.15f;
            _rim.color = rim ? new Color(.32f, .88f, 1f) : new Color(.84f, .90f, 1f);
            _presetRim = rim ? 4.0f : 1.30f;
            _rim.intensity = _presetRim * _rimScale;
        }

        private void UpdateKeyDirection() =>
            _key.transform.rotation = Quaternion.Euler(Mathf.Clamp(_presetPitch + _pitchOffset, 0f, 85f), _presetYaw + _yawOffset, 0f);

        internal void SetKeyYawOffset(float value) { _yawOffset = value; UpdateKeyDirection(); }
        internal void SetKeyPitchOffset(float value) { _pitchOffset = value; UpdateKeyDirection(); }
        internal void SetOutline(float value) { _outlineScale = value; ApplySurfaceControls(); }
        internal void SetRim(float value)
        {
            _rimScale = value;
            _rim.intensity = _presetRim * value;
            ApplySurfaceControls();
        }

        private void ApplySurfaceControls()
        {
            if (!_avatar) return;
            foreach (var renderer in _avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    var material = slots[i];
                    if (!material || !material.HasProperty("_OutlineWidth")) continue;
                    renderer.GetPropertyBlock(_surfaceProperties, i);
                    _surfaceProperties.SetFloat("_OutlineWidth", material.GetFloat("_OutlineWidth") * _outlineScale);
                    if (material.HasProperty("_DirectRimFrontCol"))
                    {
                        ScaleRim("_DirectRimFrontCol");
                        ScaleRim("_DirectRimBackCol");
                    }
                    renderer.SetPropertyBlock(_surfaceProperties, i);

                    void ScaleRim(string property)
                    {
                        Color color = material.GetColor(property);
                        _surfaceProperties.SetColor(property, new Color(color.r * _rimScale, color.g * _rimScale, color.b * _rimScale, color.a));
                    }
                }
            }
        }

        internal void SetBloom(float value) { if (_bloom) _bloom.intensity.Override(value); }
        internal void SetExposure(float value) { if (_colorAdjustments) _colorAdjustments.postExposure.Override(value); }

        private static void SetArchitecturalFill(Material mat, Color emission)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission);
        }

        internal void Frame(bool close)
        {
            _yaw = 16f;
            _pitch = close ? 3f : 8f;
            _close = close;
            _distance = close ? 1.18f : 3.26f;
            Present();
        }

        internal void Orbit(Vector2 drag)
        {
            _yaw = Mathf.Repeat(_yaw - drag.x * .40f, 360f);
            _pitch = Mathf.Clamp(_pitch + drag.y * .32f, -42f, 75f);
        }

        internal void Zoom(float steps) =>
            _distance = Mathf.Clamp(_distance * Mathf.Pow(.77f, steps), .58f, 7.5f);

        internal void Turn() => _targetTurn = Mathf.Repeat(_targetTurn + 180f, 360f);

        internal void Animate()
        {
            if (_rig == null) return;
            _rig.Grip?.RestoreClavicles();
            float dt = Time.unscaledDeltaTime;
            if (!Paused)
            {
                _elapsed += dt;
                _crouch = Mathf.MoveTowards(_crouch, Action == Motion.Crouch ? 1f : 0f, dt * 5f);
            }
            _turn = Mathf.MoveTowardsAngle(_turn, _targetTurn, dt * 540f);
            _avatar.transform.localRotation = Quaternion.Euler(0f, _turn, 0f);
            const float run = 6f;
            var motion = new MotionContact
            {
                Grounded = true, Crouch = _crouch,
                Velocity = Action == Motion.Run ? Vector2.up * run : Action == Motion.Walk ? Vector2.up * 2.4f : Vector2.zero
            };
            var view = new SeatView
            {
                Match = new PublicMatchView { Round = 1, Tick = Mathf.FloorToInt(_elapsed * MatchRules.TickRate), Paused = Paused },
                Motion = motion
            };
            if (_showcaseController && _avatarAnimator && _avatarAnimator.isActiveAndEnabled)
            {
                _avatarAnimator.speed = Paused ? 0f : 1f;
                _avatarAnimator.SetInteger("Mode", (int)Action);
                _avatarAnimator.SetFloat("Crouch", _crouch);
                _avatarAnimator.SetFloat("Forward", Action == Motion.Run ? 1f : Action == Motion.Walk ? 0.5f : 0f);
            }
            else
            {
                _rig.Animate(view, run, _elapsed * MatchRules.TickRate % 1f);
            }
        }

        internal void Present()
        {
            float height = _close && _rig?.Head ? _rig.FaceCenter.y - .10f : .88f - _crouch * .22f;
            var focus = Vector3.up * height;
            Vector3 position = focus + Quaternion.Euler(-_pitch, _yaw, 0f) * Vector3.forward * _distance;
            position.y = Mathf.Max(.14f, position.y);
            _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
            if (!_showcaseController && _rig != null && Action == Motion.Idle) _rig.Grip?.RestAtSides(_avatar.transform);
        }

        internal RenderTexture Resize(Vector2Int pixels, int supersampling)
        {
            float scale = Mathf.Min(supersampling, 3200f / Mathf.Max(1, pixels.x), 2200f / Mathf.Max(1, pixels.y));
            int max = SystemInfo.maxTextureSize;
            int width = Mathf.Clamp(Mathf.RoundToInt(pixels.x * scale), 1, max);
            int height = Mathf.Clamp(Mathf.RoundToInt(pixels.y * scale), 1, max);
            if (_texture && width == _texture.width && height == _texture.height) return _texture;
            var previous = _texture;
            var descriptor = new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_SRGB, 24);
            _texture = new RenderTexture(descriptor) { name = "Render Review", filterMode = FilterMode.Bilinear };
            _texture.Create();
            _camera.targetTexture = _texture;
            _camera.enabled = true;
            if (previous) Object.Destroy(previous);
            return _texture;
        }

        public void Dispose()
        {
            if (_root) _root.SetActive(false);
            if (_camera) _camera.targetTexture = null;
            if (_texture) Object.Destroy(_texture);
            if (_floor) Object.Destroy(_floor);
            if (_plinth) Object.Destroy(_plinth);
            if (_accentWall) Object.Destroy(_accentWall);
            if (_trimMetal) Object.Destroy(_trimMetal);
            if (_backdrop) Object.Destroy(_backdrop);
            DestroyProfile(_grading);
            DestroyProfile(_environment);
            static void DestroyProfile(VolumeProfile profile)
            {
                if (!profile) return;
                foreach (var effect in profile.components)
                    if (effect) Object.Destroy(effect);
                Object.Destroy(profile);
            }
            if (_previousScene.IsValid() && _previousScene.isLoaded)
                SceneManager.SetActiveScene(_previousScene);
            if (_scene.IsValid() && _scene.isLoaded && SceneManager.sceneCount > 1)
                SceneManager.UnloadSceneAsync(_scene);
        }
    }
}
