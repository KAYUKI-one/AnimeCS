using UnityEngine;

namespace AnimeCS.Match
{
    /// <summary>Local horizontal prediction. Collision and height are corrected by the next authority snapshot.</summary>
    internal sealed class SeatMotionPrediction
    {
        private struct Frame { public int Tick, Sequence; public SeatIntent Input; }
        internal const int MaxLead = 4;
        private readonly Frame[] _pending = new Frame[MaxLead];
        private readonly MovementTuning _tuning;
        private SeatView _base;
        private int _head, _count;
        internal Vector3 Position { get; private set; }

        internal SeatMotionPrediction(MovementTuning tuning) => _tuning = tuning ?? SeatMovement.Defaults;
        internal void Clear() { _head = _count = 0; _base = default; Position = default; }

        internal void Record(int tick, int sequence, in SeatIntent input)
        {
            if (_count != 0)
            {
                int last = (_head + _count - 1) % MaxLead;
                if (_pending[last].Tick == tick)
                {
                    _pending[last] = new Frame { Tick=tick, Sequence=sequence, Input=input };
                    Replay();
                    return;
                }
            }
            if (_count == MaxLead) { _head = (_head + 1) % MaxLead; _count--; }
            _pending[(_head + _count++) % MaxLead] = new Frame { Tick=tick, Sequence=sequence, Input=input };
            Replay();
        }

        internal void Reconcile(in SeatView authority, int applied)
        {
            _base = authority;
            while (_count > 0 && _pending[_head].Sequence <= applied)
            {
                _head = (_head + 1) % MaxLead;
                _count--;
            }
            Replay();
        }

        private void Replay()
        {
            Position = _base.Position;
            if (_base.PredictionSpeed <= 0 || _base.Health <= 0) return;
            var motion = _base.Motion;
            bool stopped = _base.PredictionJustStopped;
            for (int i=0; i<_count; i++)
            {
                var input = _pending[(_head+i) % MaxLead].Input;
                motion.Velocity = SeatMovement.Advance(motion, input, _base.PredictionSpeed, ref stopped, _tuning);
                Position += new Vector3(motion.Velocity.x,0,motion.Velocity.y) * SeatMovement.StepTime;
                float transition = input.Crouch ? _tuning.crouchSeconds : _tuning.standSeconds;
                motion.Crouch = Mathf.MoveTowards(motion.Crouch, input.Crouch ? 1f : 0f,
                    SeatMovement.StepTime / Mathf.Max(SeatMovement.StepTime, transition));
            }
        }
    }
}
