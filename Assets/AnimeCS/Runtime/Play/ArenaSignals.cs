using System;
using System.Collections.Generic;
using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;

namespace AnimeCS.Play
{
    internal sealed class ArenaSignals
    {
        private sealed class Effect
        {
            public UtilityDefinition Item;
            public Vector3 Position, Velocity, Fallback;
            public GameObject Visual, Cloud;
            public int Thrown, Landed = -1, Pulse;
        }
        private readonly List<Effect> _effects = new List<Effect>(10);
        private readonly SoundClue?[] _heard = new SoundClue?[LocalMatch.Seats];
        private readonly int[] _blindUntil = new int[LocalMatch.Seats];
        private readonly RaycastHit[] _walls = new RaycastHit[32];
        private readonly ArenaPawn[] _actors;
        private readonly MapDefinition _map;
        private readonly Transform _root;
        private readonly Dictionary<Collider, int> _bodies;
        private int _tick;

        public ArenaSignals(ArenaPawn[] actors, MapDefinition map, Transform root, Dictionary<Collider, int> bodies)
        { _actors = actors; _map = map; _root = root; _bodies = bodies; }
        public bool Blinded(int seat) => _tick < _blindUntil[seat];
        public SoundClue? Heard(int seat) => _heard[seat].HasValue && _heard[seat].Value.Expires > _tick ? _heard[seat] : null;

        public void ResetRound()
        {
            foreach (var effect in _effects) Remove(effect);
            _effects.Clear(); Array.Clear(_heard, 0, _heard.Length); Array.Clear(_blindUntil, 0, _blindUntil.Length);
        }

        public void Throw(int seat, UtilityDefinition item)
        {
            var actor = _actors[seat];
            var direction = Quaternion.Euler(-actor.Pitch, actor.Yaw, 0) * Vector3.forward;
            var visual = AppearancePresenter.NewRoot(item.DisplayName, _root);
            AppearancePresenter.AddVisual(item.Model, visual.transform, Vector3.zero);
            visual.transform.position = actor.Eye; visual.SetActive(true);
            _effects.Add(new Effect { Item = item, Position = actor.Eye, Velocity = direction * 10 + Vector3.up * 2.6f,
                Fallback = actor.transform.position, Thrown = _tick, Visual = visual });
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
                            e.Cloud = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                            e.Cloud.name = "Smoke volume"; e.Cloud.GetComponent<Collider>().enabled = false;
                            e.Cloud.transform.SetParent(_root, false); e.Cloud.transform.position = Center(e);
                            e.Cloud.transform.localScale = Vector3.one * (2 * e.Item.Radius);
                            if (e.Item.Cloud != null) e.Cloud.GetComponent<Renderer>().sharedMaterial = e.Item.Cloud;
                            Emit(e.Position, SoundKind.Utility, 12);
                        }
                        done = age >= MatchRules.Ticks(e.Item.Duration);
                        break;
                    case UtilityEffect.Flash:
                        Emit(e.Position, SoundKind.Utility, 12); Flash(e); done = true; break;
                    default:
                        int interval = MatchRules.Ticks(0.3f);
                        if (e.Pulse < 3 && age >= e.Pulse * interval) { Emit(e.Position, SoundKind.Step, e.Item.Radius); e.Pulse++; }
                        done = age >= 3 * interval;
                        if (done) Emit(e.Position, SoundKind.EchoTail, 7);
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
            e.Visual.transform.position = e.Position;
        }

        private static Vector3 Center(Effect e) => e.Position + Vector3.up * (e.Item.Radius * 0.35f);
        public bool Occludes(Vector3 start, Vector3 end)
        {
            var segment = end - start;
            foreach (var e in _effects)
            {
                if (e.Cloud == null) continue;
                Vector3 center = Center(e);
                float t = segment.sqrMagnitude < 0.00001f ? 0 : Mathf.Clamp01(Vector3.Dot(center - start, segment) / segment.sqrMagnitude);
                if ((start + t * segment - center).sqrMagnitude <= e.Item.Radius * e.Item.Radius) return true;
            }
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

        public void Emit(Vector3 source, SoundKind kind, float radius, int sourceSeat = -1, bool suppressed = false)
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
                var previous = _heard[i];
                if (previous.HasValue && previous.Value.Tick == _tick && previous.Value.Own && previous.Value.Kind == SoundKind.Gun) continue;
                _heard[i] = new SoundClue { Kind = kind, Own = i == sourceSeat, Suppressed = suppressed, Region = _map.NearestRegion(source),
                    Bearing = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, 360f) / 45f) % 8,
                    Tick = _tick, Expires = _tick + 3 * MatchRules.TickRate };
            }
        }
        private static void Remove(Effect e) { AppearancePresenter.Retire(e.Visual); AppearancePresenter.Retire(e.Cloud); }
    }
}
