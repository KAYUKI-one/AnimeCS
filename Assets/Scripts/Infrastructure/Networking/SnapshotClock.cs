using System;
using System.Diagnostics;

namespace AnimeCS.Match
{
    /// <summary>Maps the monotonic receive clock to a buffered remote simulation time.</summary>
    internal sealed class SnapshotClock
    {
        private int _latest = -1, _first, _lastInputTick;
        private double _arrival, _jitter, _render;
        internal static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        internal void Reset() { _latest = -1; _lastInputTick = 0; _jitter = 0; }

        internal void Observe(int tick, double now)
        {
            if (_latest < 0) _render = _first = tick;
            else
            {
                if (tick <= _latest) return;
                double variation = Math.Abs((now - _arrival) * MatchRules.TickRate - (tick - _latest));
                _jitter += (Math.Min(8, variation) - _jitter) / 8;
            }
            _latest = tick;
            _arrival = now;
        }

        internal int InputTick(double now)
        {
            int elapsedTicks = (int)Math.Min(SeatMotionPrediction.MaxLead - 1, Math.Max(0, now - _arrival) * MatchRules.TickRate);
            _lastInputTick = Math.Max(_lastInputTick, _latest + 1 + elapsedTicks);
            return _lastInputTick;
        }

        internal double Sample(double now)
        {
            if (_latest < 0) return 0;
            double delay = Math.Min(6, 2 + 2 * _jitter);
            double estimate = _latest + Math.Max(0, now - _arrival) * MatchRules.TickRate - delay;
            _render = Math.Max(_render, Math.Max(_first, Math.Min(_latest + 2d, estimate)));
            return _render;
        }
    }
}
