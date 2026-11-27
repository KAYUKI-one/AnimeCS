using System;
using System.Collections.Generic;
using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;

namespace AnimeCS.Play
{
    internal interface IThrownUtilityView : IDisposable
    {
        void Move(Vector3 position);
        ISmokeFieldView CreateSmoke(Vector3 center, float radius);
    }

    internal sealed class ArenaSignals
    {
        private sealed class Effect
        {
            public UtilityDefinition Item;
            public Vector3 Position, Velocity, Fallback;
            public IThrownUtilityView Visual;
            public SmokeField Cloud;
            public int Thrown, Landed = -1, Pulse;
        }
        private readonly List<Effect> _effects = new List<Effect>(10);
        private const int SoundCapacity=32;
        private readonly SoundClue[,] _events=new SoundClue[LocalMatch.Seats,SoundCapacity];
        private readonly int[] _sequence=new int[LocalMatch.Seats], _roundStart=new int[LocalMatch.Seats];
        public int LatestSound(int seat)=>_sequence[seat];
        public bool TrySound(int seat,int seq,out SoundClue clue)
        {
            clue=default;
            if(seq<=_roundStart[seat] || seq>_sequence[seat] || seq<=_sequence[seat]-SoundCapacity)return false;
            clue=_events[seat,(seq-1)%SoundCapacity];return true;
        }
        private readonly SoundClue?[] _heard = new SoundClue?[LocalMatch.Seats];
        private readonly int[] _blindUntil = new int[LocalMatch.Seats];
        private readonly RaycastHit[] _walls = new RaycastHit[32];
        private readonly ArenaPawn[] _actors;
        private readonly MapDefinition _map;
        private readonly Func<UtilityDefinition, Vector3, IThrownUtilityView> _present;
        private readonly Dictionary<Collider, int> _bodies;
        private int _tick;

        public ArenaSignals(ArenaPawn[] actors, MapDefinition map, Dictionary<Collider, int> bodies,
            Func<UtilityDefinition, Vector3, IThrownUtilityView> present)
        {
            _actors = actors;
            _map = map;
            _bodies = bodies;
            _present = present;
        }
        public bool Blinded(int seat) => _tick < _blindUntil[seat];
        public SoundClue? Heard(int seat) => _heard[seat].HasValue && _heard[seat].Value.Expires > _tick ? _heard[seat] : null;

        public void ResetRound()
        {
            foreach (var effect in _effects) Remove(effect);
            Array.Copy(_sequence,_roundStart,_sequence.Length);
            _effects.Clear(); Array.Clear(_heard, 0, _heard.Length); Array.Clear(_blindUntil, 0, _blindUntil.Length);
        }

        public void Throw(int seat, UtilityDefinition item)
        {
            var actor = _actors[seat];
            var direction = Quaternion.Euler(-actor.Pitch, actor.Yaw, 0) * Vector3.forward;
            _effects.Add(new Effect { Item = item, Position = actor.Eye, Velocity = actor.Momentum + direction * 10 + Vector3.up * 2.6f,
                Fallback = actor.transform.position, Thrown = _tick, Visual = _present?.Invoke(item, actor.Eye) });
        }

        public void Advance(int tick)
        {
            _tick = tick;
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                var e = _effects[i];
                if (e.Landed < 0) { AdvanceFlight(e); continue; }
                int age = tick - e.Landed - MatchRules.Ticks(e.Item.Delay);
                if (age < 0) continue;
                bool done;
                switch (e.Item.Effect)
                {
                    case UtilityEffect.Smoke:
                        if (e.Cloud == null)
                        {
                            e.Cloud=new SmokeField(e.Position, e.Item.Radius, e.Thrown, e.Visual?.CreateSmoke(e.Position, e.Item.Radius));
                            Emit(e.Position, SoundKind.Utility, 12, itemId:e.Item.Id);
                        }
                        e.Cloud.Advance(age*SeatMovement.StepTime,e.Item.Duration-age*SeatMovement.StepTime);
                        done = age >= MatchRules.Ticks(e.Item.Duration);
                        break;
                    case UtilityEffect.Flash:
                        Emit(e.Position, SoundKind.Utility, 12, itemId:e.Item.Id); Flash(e); done = true; break;
                    default:
                        int interval = MatchRules.Ticks(0.3f);
                        if (e.Pulse < 3 && age >= e.Pulse * interval) { Emit(e.Position, SoundKind.Step, e.Item.Radius); e.Pulse++; }
                        done = age >= 3 * interval;
                        if (done) Emit(e.Position, SoundKind.EchoTail, 7, itemId:e.Item.Id);
                        break;
                }
                if (done) { Remove(e); _effects.RemoveAt(i); }
            }
        }

        private void AdvanceFlight(Effect e)
        {
            e.Velocity += Vector3.down * (15f / MatchRules.TickRate);
            var step = e.Velocity / MatchRules.TickRate;
            if (Physics.Raycast(e.Position, step.normalized, out var hit, step.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && !_bodies.ContainsKey(hit.collider))
            {
                e.Position = hit.point + hit.normal * 0.06f; e.Landed = _tick;
            }
            else if (_tick - e.Thrown > 3 * MatchRules.TickRate)
            { e.Position = e.Fallback; e.Landed = _tick; }
            else e.Position += step;
            e.Visual?.Move(e.Position);
        }

        public void Punch(Vector3 start,Vector3 end)
        {
            foreach(var e in _effects)e.Cloud?.Punch(start,end);
        }
        public bool Occludes(Vector3 start, Vector3 end)
        {
            foreach(var e in _effects)if(e.Cloud!=null && e.Cloud.Occludes(start,end))return true;
            return false;
        }

        private void Flash(Effect e)
        {
            Vector3 source = e.Position + Vector3.up * 0.12f;
            for (int i = 0; i < _actors.Length; i++)
            {
                var actor = _actors[i]; if (!actor.Alive) continue;
                Vector3 delta = source - actor.Eye;
                if (delta.sqrMagnitude > e.Item.Radius * e.Item.Radius || Occludes(actor.Eye, source)) continue;
                Vector3 forward = Quaternion.Euler(-actor.Pitch, actor.Yaw, 0) * Vector3.forward;
                float angle = Vector3.Angle(forward, delta);
                if (angle > 50 || Physics.Raycast(actor.Eye, delta.normalized, delta.magnitude,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                int duration = MatchRules.Ticks(Mathf.Lerp(e.Item.Duration, 0.45f, angle / 50f));
                _blindUntil[i] = Math.Max(_blindUntil[i], _tick + duration);
            }
        }

        public void Emit(Vector3 source, SoundKind kind, float radius, int sourceSeat = -1, bool suppressed = false, WeaponClass category=WeaponClass.Rifle, string itemId=null, MeleeAction melee=default)
        {
            for (int i = 0; i < _actors.Length; i++)
            {
                var actor = _actors[i]; if (!actor.Alive) continue;
                Vector3 delta = source - actor.Eye;
                if (delta.sqrMagnitude > radius * radius) continue;
                int count = Physics.RaycastNonAlloc(actor.Eye, delta.normalized, _walls, delta.magnitude,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float reach = radius;
                for (int k = 0; k < count; k++) if (!_bodies.ContainsKey(_walls[k].collider)) reach *= 0.5f;
                if (delta.sqrMagnitude > reach * reach) continue;
                var clue = new SoundClue { Kind = kind, WeaponClass=category, ItemId=itemId, Melee=melee, Own = i == sourceSeat, Suppressed = suppressed, Region = _map.NearestRegion(source),
                    Bearing = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, 360f) / 45f) % 8,
                    Tick = _tick, Expires = _tick + 3 * MatchRules.TickRate };
                _events[i,_sequence[i]++%SoundCapacity]=clue;
                var previous=_heard[i];
                if(!previous.HasValue || previous.Value.Tick!=_tick || !previous.Value.Own || previous.Value.Kind!=SoundKind.Gun)_heard[i]=clue;
            }
        }
        private static void Remove(Effect e) { e.Visual?.Dispose(); e.Cloud?.Dispose(); }
    }
}
