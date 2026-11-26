using AnimeCS.Match;
using UnityEngine;

namespace AnimeCS.Play
{
    // Presentation-only coordinates. Inputs have already been resolved by the seat's movement simulation.
    internal struct LocomotionPose
    {
        internal float BodyYaw, Twist, Crouch, Vertical, AirTurn;
        internal Vector2 Direction;
        internal bool Airborne, Landed, JustReset;
        private bool _initialized, _turning;
        private int _round;
        private float _frame, _previousAim, _turnFrom, _turnTo, _turnTime;
        internal const float TurnThreshold = 48f;
        internal void Sample(in SeatView seat, float speed, float alpha)
        {
            float frame = seat.Match.Tick + alpha;
            JustReset = !_initialized || _round != seat.Match.Round || frame + .01f < _frame;
            if (JustReset)
            {
                this = default; _initialized = JustReset = true; _round = seat.Match.Round;
                BodyYaw = _previousAim = seat.Yaw; _frame = frame; Crouch = seat.Motion.Crouch;
            }
            float dt = Mathf.Clamp((frame - _frame) * SeatMovement.StepTime, 0, .12f);
            _frame = frame;
            var motion = seat.Motion;
            bool moving = motion.Velocity.sqrMagnitude > .08f;
            float twist = Mathf.DeltaAngle(BodyYaw, seat.Yaw);
            float step = 0;
            if (moving || !motion.Grounded)
            {
                _turning = false;
                BodyYaw = Mathf.MoveTowardsAngle(BodyYaw, seat.Yaw, dt * 470f);
            }
            else
            {
                if (!_turning && Mathf.Abs(twist) >= TurnThreshold)
                {
                    _turning = true; _turnFrom = BodyYaw; _turnTo = seat.Yaw; _turnTime = 0;
                }
                if (_turning)
                {
                    _turnTime = Mathf.Min(1, _turnTime + dt / .29f);
                    BodyYaw = _turnFrom + Mathf.DeltaAngle(_turnFrom, _turnTo) * Mathf.SmoothStep(0,1,_turnTime);
                    step = Mathf.Sign(Mathf.DeltaAngle(_turnFrom, _turnTo)) * Mathf.Sin(_turnTime * Mathf.PI) * .43f;
                    if (_turnTime >= 1) _turning = false;
                }
            }
            Twist = Mathf.Clamp(Mathf.DeltaAngle(BodyYaw, seat.Yaw), -65, 65);
            var velocity = Quaternion.Euler(0, -BodyYaw, 0) * new Vector3(motion.Velocity.x, 0, motion.Velocity.y);
            var target = Vector2.ClampMagnitude(new Vector2(velocity.x, velocity.z) / Mathf.Max(.1f,speed), 1);
            target.x += step * Mathf.Lerp(1,.65f,motion.Crouch);
            Direction = Vector2.Lerp(Direction, target, 1f - Mathf.Exp(-dt * 16f));
            float rate = dt > .00001f ? Mathf.DeltaAngle(_previousAim, seat.Yaw) / dt : 0;
            AirTurn = Mathf.Lerp(AirTurn, Mathf.Clamp(rate/170f,-1,1), 1f-Mathf.Exp(-dt*11f));
            Landed = Airborne && motion.Grounded; Airborne = !motion.Grounded;
            Vertical = motion.VerticalSpeed; Crouch = motion.Crouch; _previousAim = seat.Yaw;
        }
    }
}
