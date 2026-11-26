using System.Collections.Generic;
using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;

namespace AnimeCS.Play
{
    internal sealed class ArenaWorld : IMatchWorld, IMatchSignals, IMeleeWorld, IDropWorld, IDeviceFeedback
    {
        private readonly ArenaPawn[] _actors;
        private readonly Vector3?[] _muzzles;
        private Vector3? _localView;
        private struct Shot { public int seat; public Vector3 end,normal; public bool trail,wall; }
        private readonly List<Shot> _pendingShots=new List<Shot>(24);
        public void SetLocalView(Vector3? eye) => _localView=eye;
        private readonly Dictionary<Collider, int> _bodies = new Dictionary<Collider, int>();
        private readonly ArenaSignals _signals;
        private readonly ShotFeedback _shots;
        private readonly RaycastHit[] _rayHits = new RaycastHit[64];
        private static readonly float[] SightHeights = { 1.26f, 1.65f, 0.83f };
        public ArenaWorld(ArenaPawn[] actors, MapDefinition map, Transform root, MatchPresentation style = null)
        {
            _actors = actors; _muzzles = new Vector3?[actors.Length];
            for (int i=0;i<actors.Length;i++) { _bodies.Add(actors[i].Body,i); _bodies.Add(actors[i].HeadHitbox,i); }
            _signals = new ArenaSignals(actors, map, root, _bodies);
            if (style) _shots = new ShotFeedback(style,root);
        }
        public void Dispose(){_signals.ResetRound();_shots?.Dispose();}
        public void ResetRound() { _signals.ResetRound(); _shots?.Reset(); _localView=null; _pendingShots.Clear(); System.Array.Clear(_muzzles,0,_muzzles.Length); }
        public void DeviceSound(Vector3 point,bool expired) =>
            _signals.Emit(point+Vector3.up*.18f,expired ? SoundKind.DeviceBurst : SoundKind.DevicePulse,expired ? 125f : 26f);
        public void SetMuzzle(int seat, Vector3? muzzle) => _muzzles[seat]=muzzle;
        public void RenderFeedback(float alpha)
        {
            if (_shots==null) return;
            foreach (var shot in _pendingShots)
            {
                var from=_muzzles[shot.seat] ?? _actors[shot.seat].Eye;
                _shots.Show(from,shot.end,shot.normal,shot.trail,shot.wall);
            }
            _pendingShots.Clear(); _shots.Render(alpha);
        }
        public void Advance(int tick) { _signals.Advance(tick); _shots?.Advance(tick); }
        public void Throw(int seat, UtilityDefinition item) => _signals.Throw(seat, item);
        public void Emit(int seat, SoundKind kind, float radius, bool suppressed = false) =>
            _signals.Emit(_actors[seat].Eye, kind, radius, seat, suppressed);
        public SoundClue? Heard(int seat) => _signals.Heard(seat);
        public bool Blinded(int seat) => _signals.Blinded(seat);
        public bool InSmoke(int seat) => _signals.Occludes(_actors[seat].Eye, _actors[seat].Eye);
        public void Spawn(int seat, Vector3 position, float yaw) => _actors[seat].Spawn(position, yaw);
        public MotionContact Move(int seat, Vector2 velocity, float yaw, float pitch, bool jump, bool crouch) =>
            _actors[seat].Move(velocity, yaw, pitch, jump, crouch);
        public void SetAlive(int seat, bool alive) => _actors[seat].SetAlive(alive);
        public Vector3 Position(int seat) => _actors[seat].transform.position;
        public Vector3 LastGround(int seat) => _actors[seat].LastGround;
        public void SyncMovement() => Physics.SyncTransforms();

        private readonly Dictionary<WeaponDefinition, Bounds> _dropBounds = new Dictionary<WeaponDefinition, Bounds>();
        private static readonly float[] DropDistances = { 1.35f, .9f, .5f, 0f };

        public bool VisibleAlly(Vector3 eye,int teammate,Vector3 head)
        {
            var actor=_actors[teammate];
            bool Clear(Vector3 point) => !_signals.Occludes(eye,point) &&
                !Physics.Linecast(eye,point,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            if(Clear(head)) return true;
            var chest=actor.transform.position+Vector3.up*(actor.Height*.67f);
            var side=Vector3.Cross(Vector3.up,chest-eye).normalized*.21f;
            return Clear(chest+side) || Clear(chest-side) || Clear(actor.transform.position+Vector3.up*.18f);
        }

        public bool WithinReach(int seat, Vector3 point)
        {
            var eye = _actors[seat].Eye;
            return !Physics.Linecast(eye, point + Vector3.up * .12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        internal static Bounds ModelBounds(GameObject model)
        {
            if (!model) return new Bounds(Vector3.up * .05f, new Vector3(.22f, .10f, .26f));
            bool found = false;
            var result = new Bounds();
            var root = model.transform;
            var toParent = Matrix4x4.TRS(root.localPosition, root.localRotation, root.localScale) * root.worldToLocalMatrix;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var bounds = renderer.localBounds;
                var matrix = toParent * renderer.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var sign = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                    var point = matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents, sign));
                    if (found) result.Encapsulate(point); else { result = new Bounds(point, Vector3.zero); found = true; }
                }
            }
            return result;
        }

        private static readonly float[] DropBearings = { 0, 48, -48, 95, -95, 160 };
        public bool TryRestingPose(int seat, WeaponDefinition gun, bool thrown, IReadOnlyList<GroundPickup> occupied, out Pose rest)
        {
            Bounds bounds;
            if (!gun) bounds = ModelBounds(null);
            else if (!_dropBounds.TryGetValue(gun, out bounds)) _dropBounds[gun] = bounds = ModelBounds(gun.WorldModel);
            var actor = _actors[seat];
            var floor = actor.LastGround;
            var head = actor.transform.position + Vector3.up * Mathf.Min(.85f, actor.Height * .6f);
            var forward = Quaternion.Euler(0, actor.Yaw, 0);
            var lateral = gun && gun.Class == WeaponClass.Pistol ? -.23f : .20f;
            rest=default;
            foreach(float bearing in DropBearings)
            foreach (float distance in DropDistances)
            {
                var offset = forward * Quaternion.Euler(0,bearing,0) * new Vector3(lateral * distance, 0, distance * (thrown ? 1f : .47f));
                if (offset.sqrMagnitude > .001f && Physics.SphereCast(head, .09f, offset.normalized, out _, offset.magnitude,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                for (int turn = 0; turn < 3; turn++)
                {
                    var facing = forward * Quaternion.Euler(0, turn * 60, gun ? 90 : 0);
                    if (TryRest(head + offset, bounds, facing, out rest) && ClearOfPickups(rest,bounds,occupied)) return true;
                }
            }
            return TryRest(floor + Vector3.up,bounds,forward * Quaternion.Euler(0,0,gun ? 90 : 0),out rest) &&
                ClearOfPickups(rest,bounds,occupied);
        }

        private bool ClearOfPickups(Pose rest, Bounds local, IReadOnlyList<GroundPickup> occupied)
        {
            if (occupied == null || occupied.Count==0) return true;
            Bounds Volume(Pose pose, Bounds model)
            {
                var m=Matrix4x4.Rotate(pose.rotation);var e=model.extents;
                var half=new Vector3(Mathf.Abs(m.m00)*e.x+Mathf.Abs(m.m01)*e.y+Mathf.Abs(m.m02)*e.z,
                    Mathf.Abs(m.m10)*e.x+Mathf.Abs(m.m11)*e.y+Mathf.Abs(m.m12)*e.z,
                    Mathf.Abs(m.m20)*e.x+Mathf.Abs(m.m21)*e.y+Mathf.Abs(m.m22)*e.z);
                return new Bounds(pose.position+pose.rotation*model.center,2*half+Vector3.one*.012f);
            }
            var box=Volume(rest,local);
            foreach (var other in occupied)
                if (_dropBounds.TryGetValue(other.Weapon.Definition,out var bounds) && box.Intersects(Volume(other.Rest,bounds))) return false;
            return true;
        }

        public void DropPath(int seat, WeaponDefinition gun, Pose rest, out Vector3 launch, out Vector3 apex)
        {
            var bounds = _dropBounds[gun];
            var center = rest.position + rest.rotation * bounds.center;
            Vector3 extents = Vector3.Max(bounds.extents,Vector3.one*.008f);
            // Both segments are swept using the resting mesh orientation. Narrow spaces shorten the throw.
            Vector3 ClearSegment(Vector3 from, Vector3 to)
            {
                var delta=to-from;float distance=delta.magnitude;
                if (distance<.001f) return from;
                if (Physics.BoxCast(from,extents,delta.normalized,out var hit,rest.rotation,distance,
                    Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                    return from+delta.normalized*Mathf.Max(0,hit.distance-.012f);
                return to;
            }
            var high=ClearSegment(center,center+Vector3.up*.67f);
            var near=_actors[seat].transform.position;near.y=high.y-.025f;
            var start=ClearSegment(high,high+Vector3.ClampMagnitude(near-high,.92f));
            apex=rest.position+high-center; launch=rest.position+start-center;
        }

        private static bool TryRest(Vector3 above, Bounds bounds, Quaternion facing, out Pose pose)
        {
            pose = default;
            const int mask = Physics.DefaultRaycastLayers;
            const QueryTriggerInteraction ignore = QueryTriggerInteraction.Ignore;
            if (!Physics.Raycast(above, Vector3.down, out var floor, 30, mask, ignore) || floor.normal.y < .60f) return false;
            var rotation = Quaternion.FromToRotation(Vector3.up, floor.normal) * facing;
            var e = bounds.extents; e = Vector3.Max(e, Vector3.one * .008f);
            var up = Quaternion.Inverse(rotation) * floor.normal;
            float thickness = Mathf.Abs(up.x) * e.x + Mathf.Abs(up.y) * e.y + Mathf.Abs(up.z) * e.z;
            var center = floor.point + floor.normal * (thickness + .006f);
            // A short swept box finds the highest tread under the whole mesh footprint.
            var start = center + floor.normal * .32f;
            if (Physics.CheckBox(start, e, rotation, mask, ignore)) return false;
            if (Physics.BoxCast(start, e, -floor.normal, out var hit, rotation, .34f, mask, ignore))
                center = start - floor.normal * Mathf.Max(0, hit.distance - .005f);
            if (Physics.CheckBox(center, e * .985f, rotation, mask, ignore)) return false;
            pose = new Pose(center - rotation * bounds.center, rotation);
            return true;
        }

        private bool FirstHit(Vector3 origin, Vector3 direction, float range, ArenaPawn self, out RaycastHit closest)
        {
            int count=Physics.RaycastNonAlloc(origin,direction,_rayHits,range,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide);
            var hits = count == _rayHits.Length ? Physics.RaycastAll(origin,direction,range,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide) : _rayHits;
            if (hits != _rayHits) count=hits.Length;
            closest=default;
            float nearest=float.PositiveInfinity;
            for(int i=0;i<count;i++)
            {
                var hit=hits[i]; var collider=hit.collider;
                if (collider==self.Body || collider==self.HeadHitbox || (collider.isTrigger && !_bodies.ContainsKey(collider)) || hit.distance>=nearest) continue;
                closest=hit; nearest=hit.distance;
            }
            return !float.IsPositiveInfinity(nearest);
        }

        public Contact? Observe(int seat, int firstEnemy, int enemyCount)
        {
            var actor = _actors[seat];
            if (Blinded(seat)) return null;
            Vector3 origin = actor.Eye, forward = actor.transform.forward;
            Contact? best = null; float bestDist = float.MaxValue;
            for (int i = firstEnemy; i < firstEnemy + enemyCount; i++)
            {
                var enemy = _actors[i]; if (!enemy.Alive) continue;
                Vector3 offset = enemy.transform.position - actor.transform.position;
                offset.y = 0;
                float dist = offset.sqrMagnitude;
                if (dist >= bestDist || dist < 0.0001f || Vector3.Dot(forward, offset.normalized) < 0.6427876f) continue;
                foreach (float height in SightHeights)
                {
                    Vector3 point = enemy.transform.position + Vector3.up * (height * enemy.Height / SeatMovement.StandingHeight);
                    if (_signals.Occludes(origin, point)) continue;
                    Vector3 ray = point - origin;
                    if (!FirstHit(origin,ray.normalized,ray.magnitude+.1f,actor,out var hit) || !_bodies.TryGetValue(hit.collider,out int seen) || seen!=i) continue;
                    best = new Contact(i, point); bestDist = dist; break;
                }
            }
            return best;
        }

        public bool SeesPoint(int seat, Vector3 point)
        {
            var actor = _actors[seat]; Vector3 delta = point + Vector3.up * 0.18f - actor.Eye;
            var flat = new Vector3(delta.x, 0, delta.z);
            return !Blinded(seat) && !_signals.Occludes(actor.Eye, point) &&
                (flat.sqrMagnitude < 0.25f || Vector3.Dot(actor.transform.forward, flat.normalized) >= 0.6427876f)
                && !Physics.Raycast(actor.Eye, delta.normalized, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        public ArenaHit? Swing(int seat)
        {
            var actor=_actors[seat];var ray=Quaternion.Euler(-actor.Pitch,actor.Yaw,0)*Vector3.forward;
            return FirstHit(actor.Eye,ray,1.45f,actor,out var hit) && _bodies.TryGetValue(hit.collider,out int other)
                ? new ArenaHit(other,HitZone.Chest,hit.distance) : null;
        }

        public ArenaHit? Trace(int seat, Vector2 angularOffset, bool suppressed)
        {
            var actor = _actors[seat];
            var ray = Quaternion.Euler(-actor.Pitch - angularOffset.y, actor.Yaw + angularOffset.x, 0) * Vector3.forward;
            // The local camera is interpolated between ticks. Use the last displayed viewpoint for a local trigger pull.
            var eye=seat==0 && _localView.HasValue && (_localView.Value-actor.Eye).sqrMagnitude<.16f ? _localView.Value : actor.Eye;
            bool contact = FirstHit(eye,ray,500,actor,out var hit);
            int target = -1;
            bool pawn = contact && _bodies.TryGetValue(hit.collider,out target);
            Vector3 end=contact ? hit.point : eye+ray*50;
            if (_shots!=null) _pendingShots.Add(new Shot {seat=seat,end=end,normal=contact?hit.normal:Vector3.up,
                trail=!suppressed && !_signals.Occludes(eye,end),wall=contact && !pawn});
            if (!pawn) return null;
            float height = (hit.point.y - _actors[target].transform.position.y) * SeatMovement.StandingHeight / _actors[target].Height;
            HitZone zone = hit.collider==_actors[target].HeadHitbox ? HitZone.Head : height >= 1.05f ? HitZone.Chest :
                height >= 0.7f ? HitZone.Abdomen : HitZone.Legs;
            return new ArenaHit(target, zone, hit.distance);
        }
    }
}
