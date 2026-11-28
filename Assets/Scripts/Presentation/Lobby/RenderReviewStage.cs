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
        internal enum Motion { Idle, Run, Crouch }
        internal enum Lighting { Studio, Daylight, Rim }

        private readonly Scene _scene, _previousScene;
        private readonly GameObject _root;
        private readonly Camera _camera;
        private readonly Light _key, _fill, _rim;
        private readonly Material _floor, _plinth;
        private readonly Mesh _backdrop;
        private readonly VolumeProfile _environment;
        private readonly GradientSky _sky;
        private readonly int _layer;
        private readonly MatchPresentation _style;
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
            _layer = LayerMask.NameToLayer("Render Review");
            if (_layer < 0) throw new InvalidOperationException("缺少 Render Review 图层。");
            _previousScene = SceneManager.GetActiveScene();
            _scene = SceneManager.CreateScene("Render Review");
            SceneManager.SetActiveScene(_scene);
            _root = new GameObject("Render Review Stage") { layer = _layer };
            _camera = Add("Render Review Camera").AddComponent<Camera>();
            _camera.enabled = false;
            _camera.depth = -10f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
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
            _floor.SetFloat("_Smoothness", .12f);
            _plinth.SetFloat("_Smoothness", .24f);
            var backdrop = Add("Curved studio backdrop");
            _backdrop = CreateBackdrop();
            backdrop.AddComponent<MeshFilter>().sharedMesh = _backdrop;
            var backdropRenderer = backdrop.AddComponent<MeshRenderer>();
            backdropRenderer.sharedMaterial = _floor;
            backdropRenderer.shadowCastingMode = ShadowCastingMode.Off;
            Surface(PrimitiveType.Cylinder, "Turntable platform", Vector3.down * .045f, new Vector3(1.88f, .045f, 1.88f), _plinth);
            _key = Lamp("Key", LightType.Directional, Vector3.zero);
            _key.shadows = LightShadows.Soft;
            _fill = Lamp("Fill", LightType.Point, new Vector3(2.0f, 1.6f, 2.3f));
            _rim = Lamp("Rim", LightType.Point, new Vector3(-1.5f, 2.3f, -1.7f));
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

        private void Surface(PrimitiveType primitive, string name, Vector3 position, Vector3 size, Material material)
        {
            var surface = GameObject.CreatePrimitive(primitive);
            surface.name = name;
            surface.layer = _layer;
            surface.transform.SetParent(_root.transform, false);
            surface.transform.localPosition = position;
            surface.transform.localScale = size;
            var collider = surface.GetComponent<Collider>();
            collider.enabled = false;
            Object.Destroy(collider);
            var renderer = surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
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
            _rig = new CharacterRig(body, definition, outfit.PoseController, true, outfit.Regions(false), _style.secondaryMotion);
            _rig.BindClothing(AppearancePresenter.AddVisual(outfit.Model, _avatar.transform, Vector3.zero));
            foreach (var item in _avatar.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = _layer;
            _turn = _targetTurn = _elapsed = _crouch = 0f;
            _avatar.SetActive(true);
        }

        internal void SetLighting(Lighting lighting)
        {
            bool day = lighting == Lighting.Daylight;
            bool rim = lighting == Lighting.Rim;
            _camera.backgroundColor = day ? new Color(.34f, .42f, .52f) : new Color(.075f, .10f, .15f);
            _floor.color = day ? new Color(.38f, .45f, .51f) : new Color(.19f, .23f, .30f);
            _plinth.color = day ? new Color(.45f, .51f, .57f) : new Color(.29f, .35f, .44f);
            RenderSettings.ambientLight = day ? new Color(.64f, .69f, .76f) : new Color(.48f, .52f, .62f);
            _sky.top.value = day ? new Color(.45f, .57f, .72f) : new Color(.30f, .35f, .43f);
            _sky.middle.value = new Color(.38f, .39f, .42f);
            _sky.bottom.value = new Color(.16f, .17f, .19f);
            _key.color = day ? new Color(1f, .94f, .84f) : new Color(1f, .95f, .88f);
            _key.intensity = day ? .96f : rim ? .63f : .88f;
            _key.transform.rotation = Quaternion.Euler(day ? 37f : 24f, rim ? 28f : day ? 216f : 158f, 0f);
            _fill.color = new Color(.85f, .91f, 1f);
            _fill.intensity = rim ? 1.8f : 2.35f;
            _rim.color = rim ? new Color(.58f, .77f, 1f) : new Color(.75f, .86f, 1f);
            _rim.intensity = rim ? 6f : 3.3f;
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
                Velocity = Action == Motion.Run ? Vector2.up * run : Vector2.zero
            };
            var view = new SeatView
            {
                Match = new PublicMatchView { Round = 1, Tick = Mathf.FloorToInt(_elapsed * MatchRules.TickRate), Paused = Paused },
                Motion = motion
            };
            _rig.Animate(view, run, _elapsed * MatchRules.TickRate % 1f);
        }

        internal void Present()
        {
            float height = _close && _rig?.Head ? _rig.FaceCenter.y - .10f : .88f - _crouch * .22f;
            var focus = Vector3.up * height;
            Vector3 position = focus + Quaternion.Euler(-_pitch, _yaw, 0f) * Vector3.forward * _distance;
            position.y = Mathf.Max(.14f, position.y);
            _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
            if (_rig != null && Action == Motion.Idle) _rig.Grip?.RestAtSides(_avatar.transform);
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
            _root.SetActive(false);
            _camera.targetTexture = null;
            if (_texture) Object.Destroy(_texture);
            Object.Destroy(_floor);
            Object.Destroy(_plinth);
            Object.Destroy(_backdrop);
            foreach (var effect in _environment.components) Object.Destroy(effect);
            Object.Destroy(_environment);
            if (_previousScene.IsValid() && _previousScene.isLoaded)
                SceneManager.SetActiveScene(_previousScene);
            if (_scene.IsValid() && _scene.isLoaded && SceneManager.sceneCount > 1)
                SceneManager.UnloadSceneAsync(_scene);
        }
    }
}
