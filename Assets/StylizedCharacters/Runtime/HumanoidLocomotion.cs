using UnityEngine;

namespace StylizedCharacters
{
    /// <summary>Feeds a humanoid Animator from normalized local movement, independent of the motor and input source.</summary>
    public sealed class HumanoidLocomotion
    {
        private static readonly int Strafe = Animator.StringToHash("Strafe"), Forward = Animator.StringToHash("Forward"),
            Crouch = Animator.StringToHash("Crouch"), Vertical = Animator.StringToHash("Vertical"), AirTurn = Animator.StringToHash("AirTurn");
        private static readonly int Ground = Animator.StringToHash("Base Layer.Ground"),
            Air = Animator.StringToHash("Base Layer.Air"), Land = Animator.StringToHash("Base Layer.Land");
        private readonly Animator _animator;
        private RuntimeAnimatorController _controller;
        private bool _driven, _air;
        private float _landUntil;

        public HumanoidLocomotion(Animator animator) => _animator = animator;

        public void Apply(Vector2 direction, float crouch, float vertical, float turn, bool grounded, float time, bool reset, bool paused = false)
        {
            if (!_animator || !_animator.isActiveAndEnabled || !_animator.runtimeAnimatorController) return;
            if (_controller != _animator.runtimeAnimatorController)
            {
                _controller = _animator.runtimeAnimatorController;
                _driven = false;
                foreach (var parameter in _animator.parameters)
                    if (parameter.nameHash == AirTurn) _driven = true;
                reset = true;
            }
            if (!_driven) return;
            _animator.speed = paused ? 0 : 1;
            if (reset)
            {
                _landUntil = 0;
                _air = false;
                _animator.Play(Ground, 0, 0);
            }
            _animator.SetFloat(Strafe, direction.x);
            _animator.SetFloat(Forward, direction.y);
            _animator.SetFloat(Crouch, crouch);
            _animator.SetFloat(Vertical, vertical);
            _animator.SetFloat(AirTurn, turn);
            if (!grounded && !_air)
            {
                _animator.CrossFadeInFixedTime(Air, .085f);
                _landUntil = 0;
            }
            if (grounded && _air)
            {
                _animator.CrossFadeInFixedTime(Land, .045f);
                _landUntil = time + .067f;
            }
            if (_landUntil > 0 && time >= _landUntil)
            {
                _animator.CrossFadeInFixedTime(Ground, .095f);
                _landUntil = 0;
            }
            _air = !grounded;
        }
    }
}
