using System;
using System.Collections.Generic;
using AnimeCS.Content;
using AnimeCS.Profile;
using UnityEngine;

namespace AnimeCS.Match
{
    /// <summary>Owns the match clock, ten seats and all mutations. Step exactly once per simulation tick.</summary>
    public sealed partial class LocalMatch
    {
        public const int Seats = 10;
        public const int TeamSize = 5;
        private sealed class SeatState
        {
            public SeatInventory Bag;
            public string DisplayId;
            public int Health, NextShot, ReloadAt, ReloadStart, EquipTick, InspectTick, Burst;
            public int ShotSequence;
            public bool FiredLastTick;
            public float Yaw, Pitch;
            public MotionContact Motion;
            public bool JustStopped;
            public int LastHitTick, LastShotTick, LastConfirmationTick;
            public ScopeState Scope;
            public HitZone LastHitZone;
            public Contact? Visible;
            public int ObservedTick = -1;
        }

        private sealed class Port : ISeatPort
        {
            private readonly LocalMatch _match;
            private readonly int _seat;
            internal Port(LocalMatch match, int seat) { _match = match; _seat = seat; }
            public SeatView Read() => _match.ReadSeat(_seat);
            public SeatView WatchTeam()
            {
                if (_match._seats[_seat].Health > 0) return Read();
                int first = _seat / TeamSize * TeamSize;
                for (int i = first; i < first + TeamSize; i++)
                    if (_match._seats[i].Health > 0) return _match.ReadSeat(i);
                return new SeatView { Match = _match.Public, Seat = -1, ObjectiveHolder = -1 };
            }
            public bool Submit(int tick, SeatIntent intent) => _match.Submit(_seat, tick, intent);
        }

        private readonly ContentCatalog _catalog;
        private readonly MatchRules _rules;
        public MatchMode Mode { get; }
        public bool MovingTargets { get; set; }
        private readonly int[] _respawnAt=new int[Seats];
        private int _practiceKills, _practiceHeadshots;
        public static FirstSide OpeningFor(MatchMode mode, FirstSide choice) =>
            mode==MatchMode.BotMatch ? choice : mode==MatchMode.Online ? FirstSide.Random : FirstSide.Attack;
        private readonly IMatchWorld _world;
        private readonly IMatchSignals _signals;
        private readonly System.Random _draw;
        private readonly SeatState[] _seats = new SeatState[Seats];
        private readonly Port[] _ports = new Port[Seats];
        private readonly SeatIntent[] _inputs = new SeatIntent[Seats];
        private readonly bool[] _submitted = new bool[Seats];
        private readonly int[] _score = new int[2], _losses = new int[2], _rotation = new int[2];
        private readonly List<RoundReport> _reports = new List<RoundReport>();
        public IReadOnlyList<RoundReport> Reports => _reports.AsReadOnly();
        private int _attackTeam, _deadline, _round, _tick, _buyUntil;
        private SeatReceipt[] _receipts;
        private RoundPhase _phase;
        private Side _winner;
        private WinReason _reason;
        private bool _paused;
        public int Seed { get; }
        public bool IsPaused => _paused;
        public PublicMatchView Public => new PublicMatchView
        {
            Tick = _tick, Round = _round, Phase = _phase, PracticeKills=_practiceKills, PracticeHeadshots=_practiceHeadshots, Score0 = _score[0], Score1 = _score[1],
            RemainingTicks = _phase == RoundPhase.Finished ? 0 : Math.Max(0, _deadline - _tick),
            Reason = _reason, Winner = _winner, PlantedSite = _site, Paused = _paused
        };

        public LocalMatch(ContentCatalog catalog, MatchRules rules, IMatchWorld world, int seed,
            FirstSide openingSide, WardrobeProfile player, MatchMode mode=MatchMode.BotMatch)
        {
            Mode=mode; openingSide=OpeningFor(mode,openingSide);
            _catalog = catalog; _rules = rules; _world = world; Seed = seed;
            _signals = world as IMatchSignals;
            _draw = new System.Random(seed ^ 0x51EA72);
            _attackTeam = openingSide == FirstSide.Random ? _draw.Next(2) : openingSide == FirstSide.Attack ? 0 : 1;
            var skins = new List<WeaponSkinDefinition>();
            foreach (var entry in catalog.Entries)
            {
                if (entry is WeaponSkinDefinition skin && skin.Weapon != null) skins.Add(skin);
            }
            for (int i = 0; i < Seats; i++)
            {
                var appearance = i == 0 ? player.Resolve(player.current, catalog) :
                    new WardrobeLoadout();
                if (i > 0 && skins.Count > 0)
                {
                    var skin = skins[(i - 1) % skins.Count];
                    appearance.skins.Add(new SkinChoice { weaponId = skin.Weapon.Id, skinId = skin.Id });
                }
                _seats[i] = new SeatState { Bag = new SeatInventory(catalog, appearance), DisplayId = i==0 ? "本地玩家" : $"BOT {i:00}" };
                _ports[i] = new Port(this, i);
            }
            BeginRound();
        }

        public ISeatPort BindSeat(int seat)
        {
            if ((uint)seat >= Seats) throw new ArgumentOutOfRangeException(nameof(seat));
            return _ports[seat];
        }

        public void SetSeatDisplayId(int seat, string id)
        {
            if ((uint)seat>=Seats || string.IsNullOrWhiteSpace(id)) return;
            var text=new System.Globalization.StringInfo(id.Trim().Replace('\r',' ').Replace('\n',' ').Replace('\t',' '));
            _seats[seat].DisplayId=text.SubstringByTextElements(0,Math.Min(24,text.LengthInTextElements));
        }

        public string RosterOutfit(int seat) => _seats[seat].Bag.OutfitId;

        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (paused) Array.Clear(_submitted, 0, Seats);
        }

        private bool Submit(int seat, int tick, SeatIntent intent)
        {
            if (_paused || _phase == RoundPhase.Finished || tick != _tick + 1 || _submitted[seat] ||
                !float.IsFinite(intent.Yaw) || !float.IsFinite(intent.Pitch) ||
                !float.IsFinite(intent.Move.x) || !float.IsFinite(intent.Move.y) ||
                !float.IsFinite(intent.BrakeTap.x) || !float.IsFinite(intent.BrakeTap.y)) return false;
            _inputs[seat] = intent;
            _submitted[seat] = true;
            return true;
        }

        public void Step()
        {
            if (_paused || _phase == RoundPhase.Finished) return;
            _tick++;
            for (int i = 0; i < Seats; i++)
                if (!_submitted[i]) _inputs[i] = new SeatIntent { Yaw = _seats[i].Yaw, Pitch = _seats[i].Pitch };
            Array.Clear(_submitted, 0, Seats);
            var phase = _phase;
            _signals?.Advance(_tick);
            for (int i = 0; i < Seats; i++)
            {
                if (_inputs[i].Buy == BuySlot.None || !CanBuy(i)) continue;
                var bag = _seats[i].Bag;
                if (phase==RoundPhase.Practice) { if (bag.Train(_inputs[i])) StartDraw(_seats[i]); continue; }
                var oldGun = bag.Active;
                bag.Order(_inputs[i], SideOf(i), _rules);
                if (phase != RoundPhase.Buy) bag.Commit(_rules);
                if (bag.Active != oldGun) StartDraw(_seats[i]);
            }
            if (phase == RoundPhase.Contest && _tick >= _deadline)
            {
                Finish(Side.Defend, WinReason.TimeExpired);
                return;
            }
            MoveAndResolveShots(phase == RoundPhase.Buy);
            if (phase==RoundPhase.Practice)
            {
                ResolvePickups();
                for (int i=TeamSize;i<Seats;i++)
                    if (_seats[i].Health==0 && _tick>=_respawnAt[i]) SpawnTarget(i);
                return;
            }
            if (phase == RoundPhase.Settlement)
            {
                ResolvePickups();
                if (_tick < _deadline) return;
                for (int i = 0; i < Seats; i++)
                {
                    var paid = _receipts[i];
                    _receipts[i] = new SeatReceipt(_seats[i].Bag, _seats[i].Health > 0, paid.Income, paid.Overflow);
                }
                if (Math.Max(_score[0], _score[1]) >= _rules.winsRequired) _phase = RoundPhase.Finished;
                else BeginRound();
                return;
            }
            if (phase == RoundPhase.Buy)
            {
                ResolvePickups();
                if (_tick >= _deadline)
                {
                    foreach (var seat in _seats) seat.Bag.Commit(_rules);
                    SetPhase(RoundPhase.Contest, _rules.contestSeconds);
                    _buyUntil = _tick + Mathf.RoundToInt(_rules.liveBuySeconds * MatchRules.TickRate);
                }
                return;
            }
            if (phase == RoundPhase.Planted && _tick >= _nextPulse && _tick < _deadline)
            {
                (_world as IDeviceFeedback)?.DeviceSound(_device,false);
                float remaining = Mathf.Clamp01((_deadline-_tick)/(float)MatchRules.Ticks(_rules.plantedSeconds));
                _nextPulse = _tick + MatchRules.Ticks(Mathf.Lerp(.16f,1,remaining));
            }
            ResolveObjective();
            if (_phase == RoundPhase.Settlement) return;
            if (_phase == RoundPhase.Planted && _tick >= _deadline)
            {
                ExplodeDevice();
                Finish(Side.Attack, WinReason.DeviceExpired);
            }
            else if (_phase == RoundPhase.Contest && !TeamAlive(_attackTeam)) Finish(Side.Defend, WinReason.Eliminated);
            else if (!TeamAlive(1 - _attackTeam)) Finish(Side.Attack, WinReason.Eliminated);
        }

        private bool CanBuy(int i) => _seats[i].Health > 0 &&
            (_phase==RoundPhase.Practice ? i==0 :
             (_phase==RoundPhase.Buy || _phase==RoundPhase.Contest && _tick<_buyUntil) && _catalog.Map.InBuyZone(SideOf(i),_world.Position(i)));

        private void BeginRound()
        {
            _round++;
            bool half = _round == _rules.winsRequired;
            bool decider = _round == 2 * _rules.winsRequired - 1 && _round > 1;
            bool reset = _round == 1 || half || decider;
            if (decider) _attackTeam = _draw.Next(2);
            else if (half && _round > 1) _attackTeam = 1 - _attackTeam;
            if (reset) Array.Clear(_losses, 0, 2);
            _carrier = _attackTeam * TeamSize + _rotation[_attackTeam]++ % TeamSize;
            if (Mode==MatchMode.AimPractice) _carrier=-1;
            _operator = -1; _dropped = null; _site = Site.None; _reason = WinReason.None;
            _ground.Clear(); _kits.Clear(); _buyUntil = 0; _packDropper = -1;
            _signals?.ResetRound();
            Array.Clear(_heldInteract, 0, Seats);
            for (int i = 0; i < Seats; i++)
            {
                var s = _seats[i];
                if (reset) s.Bag.Wallet = decider ? _rules.deciderCredits : _rules.openingCredits;
                var side = SideOf(i);
                s.Bag.Dress(_catalog.OutfitForSide(side,i==0?s.Bag.PreferredOutfit(side):null,i%TeamSize));
                s.Bag.BeginRound(side, reset || s.Health == 0);
                s.Bag.CarriesObjective = i == _carrier;
                s.EquipTick = s.InspectTick = -100000; s.ReloadStart = 0;
                s.Scope = default; s.Health = 100; s.NextShot = s.ReloadAt = s.Burst = 0; s.FiredLastTick = false;
                s.Yaw = _catalog.Map.SpawnYaw(SideOf(i), i % TeamSize); s.Pitch = 0;
                s.Motion = MotionContact.Standing; s.JustStopped = false;
                s.LastHitTick = s.LastShotTick = s.LastConfirmationTick = -100000;
                _world.Spawn(i, _catalog.Map.Spawn(SideOf(i), i % TeamSize), s.Yaw);
                if (Mode==MatchMode.AimPractice && i>0 && i<TeamSize) { s.Health=0; _world.SetAlive(i,false); }
            }
            if (Mode==MatchMode.AimPractice) { _phase=RoundPhase.Practice; _deadline=0; }
            else SetPhase(RoundPhase.Buy, _rules.buySeconds);
        }

        private void SpawnTarget(int seat)
        {
            var s=_seats[seat]; s.Scope=default; s.Health=100; s.LastHitTick=-100000; s.LastHitZone=default;
            s.Motion=MotionContact.Standing; s.JustStopped=false; s.ObservedTick=-1;
            var side=SideOf(seat); int slot=seat%TeamSize;
            s.Yaw=_catalog.Map.SpawnYaw(side,slot); s.Pitch=0;
            _world.Spawn(seat,_catalog.Map.Spawn(side,slot),s.Yaw);
        }

        private void SetPhase(RoundPhase phase, float seconds)
        {
            _phase = phase;
            _deadline = _tick + MatchRules.Ticks(seconds);
        }

        private bool TeamAlive(int team)
        {
            for (int i = team * TeamSize; i < (team + 1) * TeamSize; i++) if (_seats[i].Health > 0) return true;
            return false;
        }

        private Side SideOf(int seat) => seat / TeamSize == _attackTeam ? Side.Attack : Side.Defend;

        private void Finish(Side winner, WinReason reason)
        {
            int team = winner == Side.Attack ? _attackTeam : 1 - _attackTeam;
            _score[team]++; _losses[team] = 0; _losses[1 - team]++;
            _winner = winner; _reason = reason; _operator = -1;
            _receipts = new SeatReceipt[Seats];
            for (int i = 0; i < Seats; i++)
            {
                var bag = _seats[i].Bag;
                int income = i / TeamSize == team ? _rules.winIncome :
                    _losses[1 - team] == 1 ? _rules.firstLossIncome : _rules.lossIncome;
                if (SideOf(i) == Side.Attack && reason == WinReason.Defused) income += _rules.plantIncome;
                int total = bag.Wallet + income;
                bag.Wallet = Math.Min(_rules.walletCap, total);
                _receipts[i] = new SeatReceipt(bag, _seats[i].Health > 0, income, total - bag.Wallet);
            }
            SetPhase(RoundPhase.Settlement, _rules.settlementSeconds);
            _reports.Add(new RoundReport(Public, _attackTeam, _receipts));
        }

        private SeatView ReadSeat(int i)
        {
            var s = _seats[i]; var bag = s.Bag; var gun = bag.Active;
            bool attack = SideOf(i) == Side.Attack;
            bool active = s.Health > 0 && (_phase == RoundPhase.Practice || _phase == RoundPhase.Contest || _phase == RoundPhase.Planted || _phase == RoundPhase.Settlement);
            if (active && s.ObservedTick != _tick)
            {
                s.Visible = _world.Observe(i, (1 - i / TeamSize) * TeamSize, TeamSize);
                s.ObservedTick = _tick;
            }
            return new SeatView
            {
                Match = Public, Seat = i, DisplayId = s.DisplayId, Side = SideOf(i), Health = s.Health, Position = _world.Position(i),
                Yaw = s.Yaw, Pitch = s.Pitch, Motion = s.Motion, Credits = bag.Available(_rules), Armor = bag.Armor, Helmet = bag.Helmet, HasKit = bag.Kit,
                Held = bag.Held, CanBuy = CanBuy(i), BuyRemaining = _phase == RoundPhase.Buy ? _deadline - _tick : Math.Max(0, _buyUntil - _tick),
                ScopeLevel = s.Scope.Level, Magnification = s.Scope.Magnification(gun?.Definition.Optics,_tick),
                ScopeStability = gun?.Definition.Optics is SniperOptics lens ? s.Scope.Stability(lens,_tick) : 0,
                Bolting = s.Scope.ResumeLevel != 0,
                CanStartShot = !_paused && _phase!=RoundPhase.Buy && _phase!=RoundPhase.Finished && s.Health>0 && _operator!=i &&
                    gun!=null && gun.Definition.Class!=WeaponClass.Knife && gun.Magazine>0 && s.ReloadAt==0 && _tick+1>=s.NextShot,
                EquipTick = s.EquipTick, InspectTick = s.InspectTick, ReloadStart = s.ReloadStart, ReloadEnd = s.ReloadAt,
                LastConfirmationTick=s.LastConfirmationTick, LastHitTick = s.LastHitTick, LastHitZone = s.LastHitZone, LastShotTick = s.LastShotTick,
                PrimaryId = bag.Primary?.Definition.Id, SidearmId = bag.Sidearm?.Definition.Id, KnifeId = bag.Knife?.Definition.Id,
                WeaponId = gun?.Definition.Id, SkinId = gun?.SkinId, Magazine = gun?.Magazine ?? 0, Reserve = gun?.Reserve ?? 0,
                OutfitId = bag.OutfitId, Worn=bag.Worn, UtilityId = bag.Utility != null ? bag.Utility.Id : null,
                Draft = _phase == RoundPhase.Buy ? bag.Draft : default,
                ObjectiveHolder = attack && s.Health > 0 ? _carrier : -1, HasObjective = _carrier == i,
                DroppedObjective = attack && s.Health > 0 ? _dropped : null,
                InteractionFraction = _operator == i ? (float)(_tick - _interactionStart) / InteractionTicks : 0,
                VisibleEnemy = active ? s.Visible : null,
                Heard = active ? _signals?.Heard(i) : null,
                Blinded = active && _signals != null && _signals.Blinded(i),
                InsideSmoke = active && _signals != null && _signals.InSmoke(i),
                VisibleDevice = active && _phase == RoundPhase.Planted && _world.SeesPoint(i, _device) ? _device : (Vector3?)null
            };
        }
    }
}
