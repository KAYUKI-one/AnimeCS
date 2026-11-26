using System.Collections.Generic;
using AnimeCS.Content;
using UnityEngine;

namespace AnimeCS.Match
{
    public interface IDropWorld
    {
        bool TryRestingPose(int seat, WeaponDefinition weapon, bool thrown, IReadOnlyList<GroundPickup> occupied, out Pose rest);
        bool WithinReach(int seat, Vector3 point);
        void DropPath(int seat, WeaponDefinition gun, Pose rest, out Vector3 launch, out Vector3 apex);
    }

    public readonly struct GroundPickup
    {
        public readonly Pose Rest;
        public Vector3 Position => Rest.position;
        public readonly WeaponInstance Weapon;
        public readonly int Tick;
        public readonly Vector3 Launch, Apex;
        public const float TravelSeconds = .28f;
        public GroundPickup(Pose rest, WeaponInstance weapon, int tick, Vector3 launch, Vector3 apex)
        { Rest = rest; Weapon = weapon; Tick = tick; Launch = launch; Apex = apex; }
        public Vector3 At(float frame)
        {
            float t = Mathf.Clamp01((frame-Tick)*SeatMovement.StepTime/TravelSeconds);
            if (t < .38f) return Vector3.Lerp(Launch,Apex,t/.38f);
            float fall = (t-.38f)/.62f;
            return Vector3.Lerp(Apex,Position,fall*fall);
        }
    }

    public sealed partial class LocalMatch
    {
        private readonly List<GroundPickup> _ground = new List<GroundPickup>();
        private readonly int[] _damage = new int[Seats];
        private int _carrier, _operator, _interactionStart;
        private Site _site, _attemptSite;
        private Vector3? _dropped;
        private Vector3 _device;
        private int _packDropper, _packUnlock, _nextPulse;
        private readonly List<Vector3> _kits = new List<Vector3>();
        public IReadOnlyList<Vector3> GroundKits => _kits;
        public IReadOnlyList<GroundPickup> GroundWeapons => _ground;
        public Vector3? WorldObjective => _reason == WinReason.Defused || _reason == WinReason.DeviceExpired ? null : _site != Site.None ? _device : _dropped;
        private int InteractionTicks => MatchRules.Ticks(_phase != RoundPhase.Planted ? _rules.plantSeconds :
            _operator >= 0 && _seats[_operator].Bag.Kit ? _rules.kitDefuseSeconds : _rules.defuseSeconds);

        private void MoveAndResolveShots(bool frozen)
        {
            System.Array.Clear(_damage, 0, Seats);
            System.Array.Clear(_lethalHit, 0, Seats);
            for (int i = 0; i < Seats; i++)
            {
                var s = _seats[i];
                if (s.Health <= 0) continue;
                var input = _inputs[i];
                s.Yaw = Mathf.Repeat(input.Yaw, 360); s.Pitch = Mathf.Clamp(input.Pitch, -82, 82);
                float speed = s.Bag.Active?.Definition.RunSpeed ?? 6f;
                if (s.Scope.Level > 0 && s.Bag.Active?.Definition.Optics is SniperOptics optics) speed *= optics.speedFraction;
                speed *= HitSpeedScale(s.LastHitTick);
                var velocity = frozen ? Vector2.zero : SeatMovement.Advance(s.Motion, input, speed, ref s.JustStopped,_rules.movement);
                s.Motion = _world.Move(i, velocity, s.Yaw, s.Pitch, !frozen && input.Jump && s.Motion.Grounded, !frozen && input.Crouch);
                if (s.Motion.Grounded && !input.Walk && s.Motion.Crouch < .5f && s.Motion.Velocity.magnitude > speed * .52f && (_tick + i * 7) % 22 == 0)
                    _signals?.Emit(i, SoundKind.Step, 14f);
            }
            _world.SyncMovement();
            if(_world is IHistoricalMatchWorld history) history.CaptureHitboxes(_tick);
            for (int i = 0; i < Seats; i++) if (_seats[i].Health > 0) HandleWeapon(i, frozen);
            ApplyDamage();
        }

        private float HitSpeedScale(int previousHit)
        {
            float age=(_tick-previousHit)*SeatMovement.StepTime;
            float recovery=Mathf.Clamp01((age-_rules.hitHoldSeconds)/Mathf.Max(SeatMovement.StepTime,_rules.hitRecoverySeconds));
            return Mathf.Lerp(_rules.hitSpeedFactor,1,recovery);
        }

        private void ApplyDamage()
        {
            for (int i = 0; i < Seats; i++)
            {
                var s = _seats[i];
                if (s.Health <= 0 || _damage[i] <= 0) continue;
                s.Health = Mathf.Max(0, s.Health - _damage[i]);
                // Refresh the existing slow on a follow-up hit, including during recovery.
                s.Motion.Velocity *= _rules.hitSpeedFactor / HitSpeedScale(s.LastHitTick);
                s.LastHitTick = _tick;
                if (s.Health != 0) continue;
                s.Scope = default;
                RecordElimination(i);
                if (_phase==RoundPhase.Practice)
                {
                    _respawnAt[i]=_tick+MatchRules.Ticks(.72f); _world.SetAlive(i,false); continue;
                }
                if (_carrier == i) DropPack(i);
                DropGun(i, s.Bag.Take(HeldSlot.Primary), false);
                DropGun(i, s.Bag.Take(HeldSlot.Sidearm), false);
                if (s.Bag.Kit) { _kits.Add(Rest(i, null, false).position); s.Bag.Kit = false; }
                _world.SetAlive(i, false);
            }
        }

        private void ExplodeDevice()
        {
            (_world as IDeviceFeedback)?.DeviceSound(_device, true);
            System.Array.Clear(_damage, 0, Seats);
            System.Array.Clear(_lethalHit, 0, Seats);
            for (int i = 0; i < Seats; i++)
            {
                float distance = Vector3.Distance(_device, _world.Position(i));
                float strength = Mathf.Clamp01(1f - distance / Mathf.Max(.1f, _rules.deviceRadius));
                _damage[i] = Mathf.RoundToInt(350 * strength * strength * (_seats[i].Bag.Armor ? .70f : 1f));
            }
            ApplyDamage();
        }

        private void StartDraw(SeatState s)
        {
            s.Scope = default;
            s.EquipTick = _tick; s.ReloadAt = 0; s.InspectTick = -100000; s.Burst = 0;
            s.NextShot = _tick + MatchRules.Ticks(.35f);
        }

        private void HandleWeapon(int i, bool frozen)
        {
            var s = _seats[i]; var input = _inputs[i]; var bag = s.Bag;
            if (!frozen && input.ThrowUtility && bag.Utility != null && _signals != null)
            {
                s.Scope = default;
                _signals.Throw(i, bag.Utility); bag.Utility = null;
                s.NextShot = _tick + MatchRules.Ticks(0.45f); s.ReloadAt = 0;
                return;
            }
            if (bag.Select(input.Select, input.SwitchWeapon) || bag.Cycle(input.Cycle)) StartDraw(s);
            if (input.DropObjective || input.DropHeld && bag.Held == HeldSlot.Objective)
            {
                if (_carrier == i) { DropPack(i); StartDraw(s); }
                return;
            }
            if (input.DropHeld && (bag.Held == HeldSlot.Primary || bag.Held == HeldSlot.Sidearm))
            {
                bag.Commit(_rules);
                if (DropGun(i, bag.Active, true)) { bag.Take(bag.Held); StartDraw(s); }
                return;
            }
            var gun = bag.Active;
            if (gun == null) return;
            if (_phase==RoundPhase.Practice) gun.Reserve=gun.Definition.Reserve;
            if (_operator == i && Eligible(i,out _)) { s.Scope=default; s.InspectTick=-100000; s.ReloadAt=0; return; }
            var lens = gun.Definition.Optics;
            if (input.LowerScope || input.Inspect) s.Scope = default;
            else s.Scope.Update(lens,_tick,input.Zoom,s.ReloadAt==0 && _tick>=s.NextShot,gun.Magazine>0);
            if (input.Inspect && s.ReloadAt == 0 && _tick >= s.NextShot) s.InspectTick = _tick;
            if (frozen) input.Fire = false;
            if(gun.Definition.Class==WeaponClass.Knife)
            {
                bool swing=input.Fire && _tick>=s.NextShot;
                s.FiredLastTick=input.Fire;
                if(swing)
                {
                    s.InspectTick=-100000;s.LastShotTick=_tick;s.NextShot=_tick+MatchRules.Ticks(.52f);
                    var contact=(_world as IMeleeWorld)?.Swing(i);
                    if(contact is ArenaHit touch && (uint)touch.Seat<Seats && touch.Seat/TeamSize!=i/TeamSize && _seats[touch.Seat].Health>0)
                        WeaponDamage(i,touch.Seat,gun.Definition,touch.Zone,40);
                }
                return;
            }
            if (s.ReloadAt != 0 && _tick >= s.ReloadAt)
            {
                int moved = Mathf.Min(gun.Definition.Magazine - gun.Magazine, gun.Reserve);
                gun.Magazine += moved; gun.Reserve -= moved; s.ReloadAt = 0;
            }
            if (input.Reload && s.ReloadAt == 0 && gun.Reserve > 0 && gun.Magazine < gun.Definition.Magazine)
            {
                s.Scope = default;
                s.ReloadStart = _tick; s.InspectTick = -100000;
                s.ReloadAt = _tick + MatchRules.Ticks(gun.Definition.ReloadSeconds);
            }
            bool press = input.Fire && (input.FirePressed || !s.FiredLastTick || gun.Definition.Automatic);
            s.FiredLastTick = input.Fire;
            if (!press || gun.Magazine == 0 || s.ReloadAt != 0 || _tick < s.NextShot) return;
            if (_tick - s.NextShot > MatchRules.Ticks(0.3f)) s.Burst = 0;
            gun.Magazine--; s.InspectTick = -100000;
            s.LastShotTick = _tick;
            _signals?.Emit(i, SoundKind.Gun, gun.Definition.SoundRadius, gun.Definition.Suppressed);
            s.NextShot = _tick + Mathf.CeilToInt(60f * MatchRules.TickRate / gun.Definition.ShotsPerMinute);
            bool steady = gun.Definition.IsSteady(s.Motion.Velocity.magnitude, s.Motion.Grounded);
            float speedRatio=s.Motion.Velocity.magnitude/gun.Definition.RunSpeed;
            float moving=Mathf.InverseLerp(.34f,1f,speedRatio);
            float spread=steady ? .06f : Mathf.Lerp(.06f,4f,Mathf.SmoothStep(0f,1f,moving));
            if(!s.Motion.Grounded) spread=Mathf.Max(2.8f,spread);
            if (lens != null)
                spread = lens.Spread(s.Motion.Velocity.magnitude / gun.Definition.RunSpeed, s.Motion.Crouch,
                    s.Motion.Grounded, s.Scope.Stability(lens, _tick));
            var offset = ShotPattern.Sample(Seed,i,++s.ShotSequence,spread,lens!=null);
            if (lens != null) s.Scope.Bolt(gun.Magazine > 0);
            if (gun.Definition.Automatic)
            {
                offset.y += Mathf.Min(7, s.Burst) * 0.22f;
                if (s.Burst >= 4) offset.x += Mathf.Sin((s.Burst - 4) * 0.65f) * 0.65f;
            }
            s.Burst++;
            var trace = input.ObservedTick.HasValue && _world is IHistoricalMatchWorld history ?
                history.TraceAt(i,offset,gun.Definition.Suppressed,_tick,input.ObservedTick.Value) : _world.Trace(i, offset, gun.Definition.Suppressed);
            if (!trace.HasValue) return;
            var hit = trace.Value;
            if ((uint)hit.Seat >= Seats || hit.Seat / TeamSize == i / TeamSize || _seats[hit.Seat].Health == 0) return;
            var protection=_seats[hit.Seat].Bag;
            WeaponDamage(i,hit.Seat,gun.Definition,hit.Zone,gun.Definition.DamageAt(hit.Distance,hit.Zone,protection.Armor,protection.Helmet));
        }

        private void ResolveObjective()
        {
            // Keep the incumbent unless they lose eligibility. A new holder starts from zero.
            if (_operator >= 0 && !Eligible(_operator, out _)) _operator = -1;
            for (int i = 0; _operator < 0 && i < Seats; i++)
            {
                if (!Eligible(i, out var site)) continue;
                _operator = i; _attemptSite = site; _interactionStart = _tick;
                _signals?.Emit(i, SoundKind.Objective, 10f);
            }
            ResolvePickups();
            if (_operator < 0 || _tick - _interactionStart < InteractionTicks) return;
            if (_phase == RoundPhase.Planted) Finish(Side.Defend, WinReason.Defused);
            else
            {
                _device = _world.LastGround(_operator); _site = _attemptSite;
                var carrier = _seats[_operator];
                if (carrier.Bag.Held == HeldSlot.Objective) StartDraw(carrier);
                carrier.Bag.RemoveObjective();
                _nextPulse = _tick;
                _operator = _carrier = -1;
                SetPhase(RoundPhase.Planted, _rules.plantedSeconds);
            }
        }

        private readonly bool[] _heldInteract = new bool[Seats];
        private bool Eligible(int i, out Site site)
        {
            site = Site.None;
            var input = _inputs[i]; var s = _seats[i];
            bool usingPack = _carrier == i && s.Bag.Held == HeldSlot.Objective && input.Fire;
            if (s.Health == 0 || (!input.Interact && !usingPack) || input.Fire && !usingPack || input.DropObjective ||
                input.DropHeld || input.Cycle != 0 || input.SwitchWeapon || input.Select != HeldSlot.Current || input.Jump || !s.Motion.Grounded || input.Move.sqrMagnitude > .01f ||
                s.Motion.Velocity.sqrMagnitude > .16f) return false;
            if (_phase == RoundPhase.Planted)
            {
                site = _site;
                return SideOf(i) == Side.Defend && Reach(i, _device, 2f);
            }
            if (_carrier != i) return false;
            var map = _catalog.Map;
            if (Near(i, map.SitePosition(Site.A), map.Radius)) site = Site.A;
            else if (Near(i, map.SitePosition(Site.B), map.Radius)) site = Site.B;
            return site != Site.None && (_operator != i || site == _attemptSite);
        }

        private bool Near(int seat, Vector3 position, float radius) => (_world.Position(seat) - position).sqrMagnitude <= radius * radius;
        private bool Reach(int seat, Vector3 p, float radius) => Near(seat, p, radius) &&
            (!(_world is IDropWorld geometry) || geometry.WithinReach(seat, p));

        private bool TryRest(int seat, WeaponDefinition gun, bool thrown, out Pose rest)
        {
            if (_world is IDropWorld geometry) return geometry.TryRestingPose(seat,gun,thrown,_ground,out rest);
            rest=new Pose(_world.LastGround(seat),Quaternion.identity); return true;
        }

        private Pose Rest(int seat, WeaponDefinition gun, bool thrown) => TryRest(seat,gun,thrown,out var rest)
            ? rest : new Pose(_world.LastGround(seat),Quaternion.identity);

        private void DropPack(int i)
        {
            _carrier = -1;
            _seats[i].Bag.RemoveObjective();
            _dropped = Rest(i, null, _seats[i].Health > 0).position;
            _packDropper = i; _packUnlock = _tick + MatchRules.Ticks(.7f);
        }

        private bool DropGun(int seat, WeaponInstance gun, bool thrown)
        {
            if (gun == null || !TryRest(seat,gun.Definition,thrown,out var rest)) return false;
            var launch = rest.position; var apex = rest.position;
            if (thrown && _world is IDropWorld geometry) geometry.DropPath(seat, gun.Definition, rest, out launch, out apex);
            _ground.Add(new GroundPickup(rest, gun, _tick, launch, apex));
            return true;
        }

        private void ResolvePickups()
        {
            for (int i = 0; i < Seats; i++)
            {
                var input = _inputs[i];
                bool pressed = input.Interact && !_heldInteract[i];
                _heldInteract[i] = input.Interact;
                if (_seats[i].Health == 0 || input.DropHeld || input.DropObjective || _operator == i) continue;
                if (_dropped.HasValue && _phase != RoundPhase.Settlement && SideOf(i) == Side.Attack &&
                    (i != _packDropper || _tick >= _packUnlock && pressed) && Reach(i, _dropped.Value, pressed ? 2f : .72f))
                {
                    _carrier = i; _seats[i].Bag.CarriesObjective = true; _dropped = null;
                    continue;
                }
                var bag = _seats[i].Bag;
                if (SideOf(i) == Side.Defend && !bag.Kit)
                    for (int j = _kits.Count - 1; j >= 0; j--)
                        if (Reach(i, _kits[j], pressed ? 2f : .72f)) { bag.Kit = true; _kits.RemoveAt(j); break; }
                if (pressed) TryPickupGun(i);
            }
        }

        private void TryPickupGun(int seat)
        {
            var bag = _seats[seat].Bag;
            int nearest = -1; float best = 2f * 2f;
            for (int i = 0; i < _ground.Count; i++)
            {
                var position = _ground[i].At(_tick);
                float range = (position - _world.Position(seat)).sqrMagnitude;
                if (range > best || !Reach(seat, position, 2f)) continue;
                nearest = i; best = range;
            }
            if (nearest < 0) return;
            var gun = _ground[nearest].Weapon;
            _ground.RemoveAt(nearest);
            bool primary = gun.Definition.Class != WeaponClass.Pistol;
            bag.Commit(_rules);
            DropGun(seat, bag.Take(primary ? HeldSlot.Primary : HeldSlot.Sidearm), false);
            bag.Equip(gun);
            StartDraw(_seats[seat]);
        }
    }
}
