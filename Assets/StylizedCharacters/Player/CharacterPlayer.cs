using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StylizedCharacters
{
    /// <summary>Standalone desktop player for a prepared humanoid prefab.</summary>
    [RequireComponent(typeof(CharacterController)), DisallowMultipleComponent]
    public sealed class CharacterPlayer : MonoBehaviour
    {
        [Serializable]
        public struct VisiblePart
        {
            public SkinnedMeshRenderer renderer;
            public Mesh mesh;
        }

        [SerializeField] private Animator avatar;
        [SerializeField] private Camera view;
        [SerializeField] private PreparedCharacter prepared;
        [SerializeField] private VisiblePart[] body = Array.Empty<VisiblePart>();
        [SerializeField] private Renderer[] hideInFirstPerson = Array.Empty<Renderer>();
        [SerializeField, Min(.1f)] private float moveSpeed = 4.2f;
        [SerializeField, Min(.01f)] private float mouseSensitivity = .12f;
        private CharacterMotor _motor;
        private CharacterCamera _camera;
        private FirstPersonVisibility _visibility;
        private HumanoidLocomotion _locomotion;
        private UprightStance _posture;
        private HumanoidSecondaryMotion _secondaryMotion;
        private int _worldMask;
        private float _pitch;
        private bool _captured;

        private void Awake()
        {
            if (!avatar || !avatar.avatar || !avatar.isHuman || !view)
            {
                Debug.LogError("CharacterPlayer requires a humanoid Animator and a Camera.", this);
                enabled = false;
                return;
            }
            avatar.applyRootMotion = false;
            avatar.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _secondaryMotion = HumanoidSecondaryMotion.Attach(avatar.gameObject);
            _locomotion = new HumanoidLocomotion(avatar);
            _posture = new UprightStance(avatar);
            _motor = new CharacterMotor(GetComponent<CharacterController>());
            _camera = new CharacterCamera(view);
            _worldMask = view.cullingMask;
            _visibility = new FirstPersonVisibility();
            if (prepared) prepared.Bind(_visibility);
            else
            {
                foreach (var part in body) if (part.renderer && part.mesh) _visibility.Split(part.renderer, part.mesh);
                foreach (var renderer in hideInFirstPerson) _visibility.Conceal(renderer);
            }
        }

        private void Update()
        {
            _posture.Restore();
            var keys = Keyboard.current;
            var mouse = Mouse.current;
            if (keys == null || mouse == null || !Application.isFocused) return;
            if (keys.escapeKey.wasPressedThisFrame) Capture(false);
            else if (!_captured && mouse.leftButton.wasPressedThisFrame) Capture(true);
            if (!_captured) return;

            if (keys.vKey.wasPressedThisFrame)
            {
                _camera.SetThirdPerson(!_camera.ThirdPerson, transform.eulerAngles.y);
                if (_secondaryMotion) _secondaryMotion.ResetMotion(preservePose: true);
            }
            var look = mouse.delta.ReadValue() * mouseSensitivity;
            if (_camera.ThirdPerson) _camera.Orbit(look, mouse.scroll.ReadValue().y / 120f);
            else
            {
                transform.Rotate(Vector3.up, look.x, Space.World);
                _pitch = Mathf.Clamp(_pitch + look.y, -82, 82);
            }
            var move = Vector2.ClampMagnitude(new Vector2(
                Axis(keys.dKey.isPressed, keys.aKey.isPressed), Axis(keys.wKey.isPressed, keys.sKey.isPressed)), 1f);
            float pace = keys.shiftKey.isPressed ? .51f : 1f;
            bool crouch = keys.ctrlKey.isPressed || keys.cKey.isPressed;
            _motor.Step(transform.TransformDirection(new Vector3(move.x, 0, move.y)), moveSpeed * pace,
                keys.spaceKey.wasPressedThisFrame, crouch, Time.deltaTime);
            _locomotion.Apply(move * (pace * Mathf.Lerp(1, .49f, _motor.Crouch)), _motor.Crouch,
                _motor.Vertical, _camera.ThirdPerson ? 0 : Mathf.Clamp(look.x / 5f, -1, 1), _motor.Grounded, Time.time, false);
        }

        private static float Axis(bool plus, bool minus) => (plus ? 1f : 0f) - (minus ? 1f : 0f);

        private void LateUpdate()
        {
            if (_motor.Grounded) _posture.Apply((1 - _motor.Crouch) * Mathf.Lerp(1f, .5f, _motor.Velocity.magnitude / moveSpeed));
            _visibility.SetLocal(!_camera.ThirdPerson);
            _visibility.SyncShadows();
            _camera.Present(transform.TransformPoint(Vector3.up * _motor.EyeHeight), transform.eulerAngles.y, _pitch, transform, _worldMask);
        }

        private void Capture(bool enabled)
        {
            _captured = enabled;
            Cursor.lockState = enabled ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !enabled;
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus && _captured) Capture(false);
        }

        private void OnDisable()
        {
            if (_captured) Capture(false);
            _posture?.Restore();
            _visibility?.SetLocal(false);
            if (_camera != null && view) view.cullingMask = _worldMask;
        }

        private void OnDestroy() => _visibility?.Release();
    }
}
