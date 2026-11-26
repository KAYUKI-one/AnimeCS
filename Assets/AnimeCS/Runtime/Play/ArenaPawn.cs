using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;

namespace AnimeCS.Play
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class ArenaPawn : MonoBehaviour
    {
        private CharacterController _body;
        private CapsuleCollider _hitbox;
        private SphereCollider _headHitbox;
        internal SphereCollider HeadHitbox => _headHitbox;
        private Vector3 _spawn, _safe, _previousEye, _previousPosition;
        private float _vertical, _crouch, _tuck;
        private bool _grounded;
        private readonly RaycastHit[] _headroomHits = new RaycastHit[12];
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public Collider Body => _hitbox;
        public Vector3 LastGround => _safe;
        public bool Alive => _body.enabled;
        public Vector3 Eye => transform.position + Vector3.up * SeatMovement.EyeHeight(_crouch);
        public float Height => _body.height;
        internal MovementTuning Tuning { get; set; } = SeatMovement.Defaults;
        public Transform Visuals { get; private set; }
        internal float GroundSkin => _body.skinWidth;
        internal Vector3 RenderEye(float alpha) => Vector3.Lerp(_previousEye, Eye, alpha);
        internal Vector3 RenderFeet(float alpha) => Vector3.Lerp(_previousPosition,transform.position,alpha);
        internal void InterpolateVisuals(float alpha) => Visuals.position=RenderFeet(alpha);
        private void Awake()
        {
            _body = GetComponent<CharacterController>();
            // The motor contacts the level. The ref's query capsule follows stance immediately, including between physics frames.
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            var hit = new GameObject("Hitbox");
            hit.transform.SetParent(transform,false);
            _hitbox = hit.AddComponent<CapsuleCollider>();
            _hitbox.isTrigger = true; _hitbox.radius = .285f;
            _headHitbox=hit.AddComponent<SphereCollider>(); _headHitbox.isTrigger=true; _headHitbox.radius=.145f;
            _body.radius = 0.32f; SetHeight(SeatMovement.StandingHeight);
            _body.skinWidth = 0.035f; _body.stepOffset = 0.25f; _body.minMoveDistance = 0;
            Visuals = new GameObject("Appearance").transform;
            Visuals.SetParent(transform, false);
        }

        internal void Spawn(Vector3 position, float yaw)
        {
            _body.enabled = false;
            _spawn = _safe = position; Yaw = yaw; Pitch = _vertical = 0;
            _crouch = _tuck = 0; _grounded = true; SetHeight(SeatMovement.StandingHeight);
            transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            _previousPosition = position; _previousEye = Eye; Visuals.localPosition = Vector3.zero;
            _body.enabled = true; _hitbox.enabled = _headHitbox.enabled = true;
            Visuals.gameObject.SetActive(true);
        }
        internal void SetAlive(bool alive) { _body.enabled = _hitbox.enabled = _headHitbox.enabled = alive; Visuals.gameObject.SetActive(alive); }
        private void SetHeight(float value)
        {
            _body.height = value;
            _body.center = Vector3.up * (value * .5f);
            float eyes=SeatMovement.EyeHeight(_crouch);
            _hitbox.height=eyes-.074f;
            _hitbox.center=Vector3.up*(_hitbox.height*.5f);
            _headHitbox.center=new Vector3(0,eyes+.006f,Mathf.Lerp(.03f,.10f,_crouch));
        }

        private bool CanRise(float delta)
        {
            float radius = _body.radius - .012f;
            Vector3 start = transform.position + Vector3.up * (_body.height - _body.radius);
            int count = Physics.SphereCastNonAlloc(start, radius, Vector3.up, _headroomHits, delta + .017f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) if (_headroomHits[i].collider != _body) return false;
            return count < _headroomHits.Length;
        }

        private Vector3 ClipAirborneFeet(Vector3 travel,float rise)
        {
            // A rounded controller can otherwise roll up the top of a vertical crate while its feet are still below it.
            float length=travel.magnitude;
            if(length<.0001f)return travel;
            Vector3 direction=travel/length;
            const float toe=.035f;
            Vector3 origin=transform.position+Vector3.up*(Mathf.Max(0,rise)+toe+_body.skinWidth);
            int count=Physics.SphereCastNonAlloc(origin,toe,direction,_headroomHits,length+_body.radius+toe,
                Physics.AllLayers,QueryTriggerInteraction.Ignore);
            float nearest=float.MaxValue;Vector3 wall=default;
            for(int i=0;i<count;i++)
            {
                var hit=_headroomHits[i];
                if(hit.collider==_body || hit.normal.y>.45f || hit.normal.y<-.45f || hit.distance>=nearest)continue;
                nearest=hit.distance;wall=hit.normal;
            }
            if(nearest==float.MaxValue)return travel;
            Vector3 advance=direction*Mathf.Clamp(nearest+toe-_body.radius-_body.skinWidth,0,length);
            Vector3 side=travel-advance;wall.y=0;wall.Normalize();
            return advance+side-wall*Mathf.Min(0,Vector3.Dot(side,wall));
        }

        internal MotionContact Move(Vector2 velocity, float yaw, float pitch, bool jump, bool crouch)
        {
            _previousEye = Eye; _previousPosition = transform.position;
            Yaw = yaw; Pitch = pitch;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            const float dt = SeatMovement.StepTime;
            float amount = Mathf.MoveTowards(_crouch, crouch ? 1 : 0, dt / Mathf.Max(dt,crouch ? Tuning.crouchSeconds : Tuning.standSeconds));
            float height = Mathf.Lerp(SeatMovement.StandingHeight, SeatMovement.DuckHeight, amount);
            if (amount != _crouch && (height < _body.height || CanRise(height - _body.height)))
            {
                _crouch = amount;
                SetHeight(height);
            }
            bool takingOff = jump && _grounded;
            bool airborne = takingOff || !_grounded;
            float targetTuck = airborne && crouch ? Tuning.tuckHeight * _crouch : 0f;
            float nextTuck = Mathf.MoveTowards(_tuck,targetTuck,Tuning.tuckHeight*dt/Mathf.Max(dt,Tuning.crouchSeconds));
            float legMotion = nextTuck - _tuck;
            _tuck = nextTuck;
            // Only the feet retract. The ballistic velocity stays independent of repeated crouch input.
            _body.stepOffset = airborne ? 0f : .25f;
            _vertical = takingOff ? Tuning.jumpSpeed : _grounded ? -2f : Mathf.Max(-24, _vertical - Tuning.gravity * dt);
            var before = transform.position;
            float rise=_vertical*dt+legMotion;
            Vector3 travel=new Vector3(velocity.x,0,velocity.y)*dt;
            if(airborne)travel=ClipAirborneFeet(travel,rise);
            var flags = _body.Move(travel+Vector3.up*rise);
            _grounded = (flags & CollisionFlags.Below) != 0;
            if (_grounded) { _vertical = _tuck = 0; _safe = transform.position; }
            else if ((flags & CollisionFlags.Above) != 0) _vertical = Mathf.Min(0, _vertical);
            if (transform.position.y < Mathf.Min(_safe.y, _spawn.y) - 6f) Spawn(_safe, yaw);
            Vector3 actual = (transform.position - before) / dt;
            var flat = (flags & CollisionFlags.Sides) == 0 ? velocity : Vector2.ClampMagnitude(new Vector2(actual.x, actual.z),velocity.magnitude);
            return new MotionContact { Velocity = flat, VerticalSpeed = _vertical, Grounded = _grounded, Crouch = _crouch };
        }
    }
}
