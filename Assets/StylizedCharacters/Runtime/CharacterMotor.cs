using UnityEngine;

namespace StylizedCharacters
{
    /// <summary>Walks, jumps and crouches using a CharacterController. The caller supplies world-space input.</summary>
    public sealed class CharacterMotor
    {
        private readonly CharacterController _controller;
        private readonly float _standingHeight, _duckHeight;
        private readonly Vector3 _standingCenter;
        private readonly Collider[] _obstacles = new Collider[12];
        public bool Grounded { get; private set; }
        public float Vertical { get; private set; }
        public Vector3 Velocity { get; private set; }
        public float Crouch => 1f - Mathf.InverseLerp(_duckHeight, _standingHeight, _controller.height);
        public float EyeHeight => _controller.height - .14f;

        public CharacterMotor(CharacterController controller)
        {
            _controller = controller;
            _standingHeight = controller.height;
            _standingCenter = controller.center;
            _duckHeight = Mathf.Min(_standingHeight, Mathf.Max(2f * controller.radius, _standingHeight * .74f));
            Grounded = controller.isGrounded;
        }

        public void Step(Vector3 direction, float speed, bool jump, bool crouch, float elapsed)
        {
            if (elapsed <= 0) return;
            float height = Mathf.MoveTowards(_controller.height, crouch ? _duckHeight : _standingHeight, 2.5f * elapsed);
            if (height <= _controller.height || HasHeadroom(height))
            {
                _controller.height = height;
                _controller.center = _standingCenter + Vector3.up * ((height - _standingHeight) * .5f);
            }
            Vertical = Grounded ? (jump ? 5.4f : -2f) : Vertical;
            Vertical -= 20f * elapsed;
            Velocity = Vector3.ClampMagnitude(direction, 1) * (speed * Mathf.Lerp(1, .49f, Crouch));
            var collision = _controller.Move((Velocity + Vector3.up * Vertical) * elapsed);
            Grounded = (collision & CollisionFlags.Below) != 0;
            if (Vertical > 0 && (collision & CollisionFlags.Above) != 0) Vertical = 0;
        }

        private bool HasHeadroom(float height)
        {
            var root = _controller.transform;
            float radius = Mathf.Max(.01f, _controller.radius * Mathf.Max(Mathf.Abs(root.lossyScale.x), Mathf.Abs(root.lossyScale.z)) - .01f);
            var from = root.TransformPoint(_controller.center + Vector3.up * (_controller.height * .5f - _controller.radius));
            var to = from + root.TransformVector(Vector3.up * (height - _controller.height));
            int count = Physics.OverlapCapsuleNonAlloc(from, to, radius, _obstacles,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!_obstacles[i].transform.IsChildOf(root)) return false;
            return count < _obstacles.Length;
        }
    }
}
