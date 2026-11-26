using AnimeCS.Content;
using UnityEngine;

namespace AnimeCS.Match
{
    internal struct ScopeState
    {
        public int Level { get; private set; }
        public int ResumeLevel { get; private set; }
        private int _acquired, _changed;
        private float _from;

        public float Magnification(SniperOptics lens, int tick)
        {
            if (lens == null || Level == 0) return 1f;
            float t = Mathf.Clamp01((tick-_changed)*SeatMovement.StepTime/lens.acquireSeconds);
            return Mathf.SmoothStep(_from, lens.Power(Level), t);
        }

        public float Stability(SniperOptics lens, int tick) => Level == 0 ? 0f :
            Mathf.Clamp01((tick-_acquired)*SeatMovement.StepTime/lens.acquireSeconds);

        private void Raise(int level, SniperOptics lens, int tick)
        {
            _from = Magnification(lens, tick);
            if (Level == 0) _acquired = tick;
            _changed = tick;
            Level = level;
        }

        public void Update(SniperOptics lens, int tick, bool click, bool ready, bool hasAmmo)
        {
            if (lens == null) return;
            if (ResumeLevel != 0)
            {
                if (click || !hasAmmo) { this = default; return; }
                if (ready) { int level=ResumeLevel; ResumeLevel=0; Raise(level,lens,tick); }
                return;
            }
            if (!click || !ready) return;
            if (Level == 2) this = default;
            else Raise(Level + 1, lens, tick);
        }

        public void Bolt(bool hasAmmo)
        {
            int previous = Level;
            this = default;
            if (hasAmmo) ResumeLevel = previous;
        }
    }
}

