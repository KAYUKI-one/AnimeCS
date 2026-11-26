using UnityEngine;

namespace AnimeCS.Match
{
    [System.Serializable]
    public sealed class MovementTuning
    {
        [Min(.02f), InspectorName("地面起步至满速秒数")] public float accelerateSeconds=.17f;
        [Min(.02f), InspectorName("满速反向制动秒数")] public float counterStopSeconds=.09f;
        [Min(.02f), InspectorName("松键至稳定射速区秒数")] public float coastToSteadySeconds=.20f;
        [InspectorName("过零后保留一个模拟步")] public bool settleOnReverse=true;
        [Range(.1f,1), InspectorName("静步速度比例")] public float walkFraction=.51f;
        [Range(.1f,1), InspectorName("蹲走速度比例")] public float duckFraction=.34f;
        [Min(0), InspectorName("空中转向加速度倍数")] public float airAcceleration=1.65f;
        [Range(.01f,1), InspectorName("空中意向轴限速比例")] public float airProjection=.18f;
        [Min(1), InspectorName("空中速度上限倍数")] public float airCap=1.22f;
        [Min(.1f), InspectorName("初始起跳速度")] public float jumpSpeed=5.65f;
        [Min(.1f), InspectorName("重力加速度")] public float gravity=21f;
        [Min(.02f), InspectorName("下蹲过渡秒数")] public float crouchSeconds=.16f;
        [Min(.02f), InspectorName("起身过渡秒数")] public float standSeconds=.22f;
    }

    public struct MotionContact
    {
        public Vector2 Velocity;
        public float VerticalSpeed, Crouch;
        public bool Grounded;
        public static MotionContact Standing => new MotionContact { Grounded = true };
    }

    public static class SeatMovement
    {
        public const float StepTime = 1f / MatchRules.TickRate;
        internal static readonly MovementTuning Defaults = new MovementTuning();
        public const float StandingHeight = 1.78f, DuckHeight = 1.32f;
        public static float EyeHeight(float crouch) => Mathf.Lerp(1.62f, 1.16f, crouch);

        public static Vector2 Advance(in MotionContact state, in SeatIntent input, float runSpeed, ref bool justStopped, MovementTuning tuning=null)
        {
            tuning ??= Defaults;
            float coast = runSpeed * .66f * StepTime / Mathf.Max(StepTime,tuning.coastToSteadySeconds);
            Vector2 velocity = state.Velocity;
            Vector2 wish = Vector2.ClampMagnitude(input.Move, 1);
            if (!state.Grounded || input.Jump)
            {
                justStopped = false;
                if (wish == Vector2.zero) return velocity;
                var direction = wish.normalized;
                float room = runSpeed * tuning.airProjection * wish.magnitude - Vector2.Dot(velocity, direction);
                if (room <= 0) return velocity;
                return Vector2.ClampMagnitude(velocity + direction * Mathf.Min(room, runSpeed * tuning.airAcceleration * StepTime),
                    Mathf.Max(velocity.magnitude, runSpeed * tuning.airCap));
            }
            var brakeAxis = wish == Vector2.zero ? input.BrakeTap.normalized : wish.normalized;
            float projection = Vector2.Dot(velocity, brakeAxis);
            if (projection < -.001f)
            {
                float brakeStep = runSpeed * StepTime / Mathf.Max(StepTime,tuning.counterStopSeconds);
                var across = velocity - projection * brakeAxis;
                across = Vector2.MoveTowards(across, Vector2.zero, coast);
                float remaining = Mathf.Min(0, projection + brakeStep);
                justStopped = tuning.settleOnReverse && remaining == 0;
                return across + brakeAxis * remaining;
            }
            if (wish == Vector2.zero)
            {
                justStopped = false;
                return Vector2.MoveTowards(velocity, Vector2.zero, coast);
            }
            if (justStopped)
            {
                justStopped = false;
                return velocity;
            }
            float stance = input.Crouch ? Mathf.MoveTowards(state.Crouch, 1f, StepTime / Mathf.Max(StepTime, tuning.crouchSeconds)) : state.Crouch;
            float pace = Mathf.Lerp(input.Walk ? tuning.walkFraction : 1f, tuning.duckFraction, stance);
            return Vector2.MoveTowards(velocity, wish * (runSpeed * pace), runSpeed * StepTime / Mathf.Max(StepTime,tuning.accelerateSeconds));
        }
    }
}
