using UnityEngine;

namespace StylizedCharacters
{
    /// <summary>Positions a single world camera for either eye-level play or orbit inspection.</summary>
    public sealed class CharacterCamera
    {
        public const string HiddenLayer = "Local Body Hidden";
        public const float EyeForward = .08f;
        public const float NearClip = .05f;
        private readonly Camera _camera;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private float _yaw, _pitch, _distance;

        public CharacterCamera(Camera camera) => _camera = camera;
        public bool ThirdPerson { get; private set; }
        public static int HiddenLayerMask => LayerMask.GetMask(HiddenLayer);

        public void SetThirdPerson(bool enabled, float facing)
        {
            if (ThirdPerson == enabled) return;
            ThirdPerson = enabled;
            _yaw = facing + 26f;
            _pitch = 13f;
            _distance = 2.7f;
        }

        public void Orbit(Vector2 lookDegrees, float wheelSteps)
        {
            _yaw = Mathf.Repeat(_yaw + lookDegrees.x, 360);
            _pitch = Mathf.Clamp(_pitch - lookDegrees.y, -35, 73);
            _distance = Mathf.Clamp(_distance * Mathf.Pow(.77f, wheelSteps), .60f, 6.5f);
        }

        public void Present(Vector3 eye, float yaw, float pitch, Transform actor, int visibleLayers, bool orbitAllowed = true)
        {
            _camera.nearClipPlane = NearClip;
            _camera.cullingMask = visibleLayers & ~HiddenLayerMask;
            if (ThirdPerson && orbitAllowed)
            {
                var focus = eye - Vector3.up * .43f;
                var rotation = Quaternion.Euler(_pitch, _yaw, 0);
                var direction = rotation * Vector3.back;
                float range = ClearDistance(focus, direction, _distance, .11f, actor);
                _camera.transform.SetPositionAndRotation(focus + direction * range, rotation);
                return;
            }
            var forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            float offset = ClearDistance(eye, forward, EyeForward, .055f, actor);
            _camera.transform.SetPositionAndRotation(eye + offset * forward, Quaternion.Euler(-pitch, yaw, 0));
        }

        private float ClearDistance(Vector3 start, Vector3 direction, float distance, float radius, Transform actor)
        {
            int count = Physics.SphereCastNonAlloc(start, radius, direction, _hits, distance,
                Physics.DefaultRaycastLayers & ~HiddenLayerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (actor && hit.transform.IsChildOf(actor)) continue;
                distance = Mathf.Min(distance, Mathf.Max(0, hit.distance - .035f));
            }
            return distance;
        }
    }
}
