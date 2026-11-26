using System;
using System.Collections.Generic;
using System.Linq;
using AnimeCS.Content;
using AnimeCS.Editor;
using AnimeCS.Match;
using AnimeCS.Profile;
using NUnit.Framework;
using UnityEngine;

namespace AnimeCS.Tests
{
    public sealed class MatchTests
    {
        private sealed class Arena : IMatchWorld, IMeleeWorld
        {
            public readonly Vector3[] Points = new Vector3[10];
            public readonly int[] Targets = Enumerable.Repeat(-1, 10).ToArray();
            public readonly bool[] Alive = new bool[10];
            public readonly bool[] Air = new bool[10];
            public readonly List<Vector2> Rays = new List<Vector2>();
            public readonly HitZone?[] Zones = new HitZone?[10];
            public void Spawn(int seat, Vector3 position, float yaw) { Points[seat] = position; Alive[seat] = true; }
            public MotionContact Move(int seat, Vector2 velocity, float yaw, float pitch, bool jump, bool crouch) =>
                new MotionContact { Grounded = !Air[seat], Velocity = velocity, VerticalSpeed = Air[seat] ? 3f : 0f };
            public void SetAlive(int seat, bool alive) => Alive[seat] = alive;
            public Vector3 Position(int seat) => Points[seat];
            public Vector3 LastGround(int seat) => Points[seat];
            public Contact? Observe(int seat, int firstEnemy, int count) => null;
            public bool SeesPoint(int seat, Vector3 point) => false;
            public HitZone Zone = HitZone.Head;
            public void SyncMovement() { }
            public ArenaHit? Swing(int seat) => Trace(seat, Vector2.zero, false);
            public ArenaHit? Trace(int seat, Vector2 offset, bool suppressed)
            {
                Rays.Add(offset);
                return Targets[seat] < 0 ? null : new ArenaHit(Targets[seat], Zones[seat] ?? Zone, 0);
            }
        }
        private ContentCatalog _catalog;
        private LocalMatch _match;
        private MatchRules _rules;
        private Arena _arena;
        private ISeatPort[] _seats;

        [SetUp] public void Setup()
        {
            _catalog = ContentLibrary.Catalog;
            _rules = ScriptableObject.CreateInstance<MatchRules>();
            _rules.buySeconds = 0.16f; _rules.contestSeconds = 0.46f;
            _rules.plantedSeconds = 0.30f; _rules.settlementSeconds = 0.06f;
            _rules.plantSeconds = 0.06f; _rules.defuseSeconds = 0.08f;
            _arena = new Arena();
            _match = new LocalMatch(_catalog, _rules, _arena, 29, FirstSide.Attack, WardrobeProfile.Create(_catalog));
            _seats = Enumerable.Range(0, 10).Select(_match.BindSeat).ToArray();
        }
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(_rules);
        [Test] public void OnlyBotMatchesAcceptAChosenOpeningSide()
        {
            foreach(var side in new[]{FirstSide.Attack,FirstSide.Defend})
            {
                Assert.That(LocalMatch.OpeningFor(MatchMode.BotMatch,side),Is.EqualTo(side));
                Assert.That(LocalMatch.OpeningFor(MatchMode.AimPractice,side),Is.EqualTo(FirstSide.Attack));
                Assert.That(LocalMatch.OpeningFor(MatchMode.Online,side),Is.EqualTo(FirstSide.Random));
            }
            var observed=new HashSet<Side>();
            for(int seed=0;seed<12;seed++)
            {
                var match=new LocalMatch(_catalog,_rules,new Arena(),seed,FirstSide.Defend,WardrobeProfile.Create(_catalog),MatchMode.Online);
                observed.Add(match.BindSeat(0).Read().Side);
            }
            Assert.That(observed.Count,Is.EqualTo(2));
        }

        [Test] public void RangeIssuesBothFactionsAndRepopulatesHeadshotTargetsWithoutEnding()
        {
            var profile = WardrobeProfile.Create(_catalog);
            profile.current.attackOutfitId = "outfit.laevatain";
            profile.current.outfitId = profile.current.defendOutfitId = "outfit.lily";
            _match=new LocalMatch(_catalog,_rules,_arena,17,FirstSide.Defend,profile,MatchMode.AimPractice);
            _seats=Enumerable.Range(0,10).Select(_match.BindSeat).ToArray();
            var player=_seats[0]; Assert.That(player.Read().HasObjective,Is.False);
            Assert.That(player.Read().Side, Is.EqualTo(Side.Attack));
            Assert.That(CharacterAt(player.Read()), Is.EqualTo("character.laevatain"));
            Assert.That(_arena.Alive.Count(x=>x),Is.EqualTo(6));
            var targets = CharactersOnSide(Side.Defend, aliveOnly: true);
            Assert.That(targets, Is.EquivalentTo(new[] { "character.mornye", "character.rover", "character.lily" }));
            _arena.Points[0]=new Vector3(999,0,999);int opening=player.Read().Credits;
            foreach(var gun in _catalog.Entries.OfType<WeaponDefinition>().Where(x=>!x.Archived))
            {
                Input(0,new SeatIntent{Buy=gun.Class==WeaponClass.Pistol?BuySlot.Sidearm:gun.Class==WeaponClass.Knife?BuySlot.Knife:BuySlot.Primary,ItemId=gun.Id});_match.Step();
                Assert.That(player.Read().WeaponId,Is.EqualTo(gun.Id));
            }
            foreach(var item in _catalog.Entries.OfType<UtilityDefinition>().Where(x=>!x.Archived))
            { Input(0,new SeatIntent{Buy=BuySlot.Utility,ItemId=item.Id});_match.Step();Assert.That(player.Read().UtilityId,Is.EqualTo(item.Id)); }
            Input(0,new SeatIntent{Buy=BuySlot.Kit});_match.Step();Assert.That(player.Read().HasKit,Is.True);
            Input(0,new SeatIntent{Buy=BuySlot.Armor});_match.Step();Assert.That(player.Read().Armor,Is.True);
            Input(0,new SeatIntent{Buy=BuySlot.Primary,ItemId="weapon.longglass96"});_match.Step();
            for(int i=0;i<65;i++)_match.Step();
            _arena.Targets[0]=5;Input(0,new SeatIntent{Fire=true});_match.Step();
            Assert.That(_seats[5].Read().Health,Is.Zero);
            Assert.That(_match.Public.PracticeHeadshots,Is.EqualTo(1)); Assert.That(_match.Public.PracticeKills,Is.EqualTo(1));
            for(int i=0;i<MatchRules.TickRate*2;i++)_match.Step();
            Assert.That(_seats[5].Read().Health,Is.EqualTo(100));Assert.That(_match.Public.Phase,Is.EqualTo(RoundPhase.Practice));
            Assert.That(CharactersOnSide(Side.Defend, aliveOnly: true), Is.EquivalentTo(targets));
            Assert.That(_match.Public.Score0+_match.Public.Score1,Is.Zero);Assert.That(player.Read().Credits,Is.EqualTo(opening));
        }

        [Test] public void HitTagsTheVictimOnceAndMovementRecovers()
        {
            _rules.contestSeconds = 8;
            Until(RoundPhase.Contest);
            for (int t=0;t<12;t++){Input(5,new SeatIntent{Move=Vector2.right});_match.Step();}
            float full=_seats[5].Read().Motion.Velocity.x;
            _arena.Targets[0]=5;_arena.Zone=HitZone.Legs;
            Input(0,new SeatIntent{Fire=true});Input(5,new SeatIntent{Move=Vector2.right});_match.Step();
            var victim=_seats[5].Read();
            Assert.That(victim.Health,Is.InRange(1,99));
            Assert.That(victim.LastHitZone,Is.EqualTo(HitZone.Legs));
            Assert.That(victim.LastHitTick,Is.EqualTo(_match.Public.Tick));
            Assert.That(victim.Motion.Velocity.x,Is.EqualTo(full*_rules.hitSpeedFactor).Within(.001));
            for(int t=0;t<26;t++){Input(5,new SeatIntent{Move=Vector2.right});_match.Step();}
            Assert.That(_seats[5].Read().Motion.Velocity.x,Is.EqualTo(full).Within(.001));
        }
        [Test] public void FollowUpHitsRefreshTheSlowWithoutMultiplyingIt()
        {
            _rules.contestSeconds = 10;
            Until(RoundPhase.Contest);
            void Move() => Input(5, new SeatIntent {Move = Vector2.right});
            for (int i=0; i<15; i++) { Move(); _match.Step(); }
            float top = _seats[5].Read().Motion.Velocity.x;
            float slow = top * _rules.hitSpeedFactor;
            _arena.Zone = HitZone.Legs;
            void Tag(int attacker)
            {
                _arena.Targets[attacker] = 5;
                Input(attacker, new SeatIntent {Fire = true});
                Move(); _match.Step();
                Assert.That(_seats[5].Read().LastHitTick, Is.EqualTo(_match.Public.Tick));
                Assert.That(_seats[5].Read().Motion.Velocity.x, Is.EqualTo(slow).Within(.002));
            }
            Tag(0); Tag(1);
            for (int t=0; t<MatchRules.Ticks(_rules.hitHoldSeconds); t++)
            {
                Move(); _match.Step();
                Assert.That(_seats[5].Read().Motion.Velocity.x, Is.EqualTo(slow).Within(.002));
            }
            for (int t=0; t<5; t++) { Move(); _match.Step(); }
            Assert.That(_seats[5].Read().Motion.Velocity.x, Is.InRange(slow+.01f, top-.01f));
            Tag(2);
            _arena.Air[5] = true;
            _arena.Targets[3] = 5;
            Input(3, new SeatIntent {Fire = true}); Move(); _match.Step();
            Assert.That(_seats[5].Read().Motion.VerticalSpeed, Is.EqualTo(3f));
            _arena.Air[5] = false;
            for (int t=0;t<Mathf.CeilToInt((_rules.hitHoldSeconds+_rules.hitRecoverySeconds)*MatchRules.TickRate)+1;t++)
            { Move(); _match.Step(); }
            Assert.That(_seats[5].Read().Health, Is.Positive);
            Assert.That(_seats[5].Read().Motion.Velocity.x, Is.EqualTo(top).Within(.002));
        }

        [Test] public void SeparateProtectionOrdersCancelIndependentlyAndProtectTheMatchingRegion()
        {
            void Buy(int seat, BuySlot slot, bool cancel=false)
            { Input(seat, new SeatIntent {Buy=slot, CancelPurchase=cancel}); _match.Step(); }
            Buy(5, BuySlot.Helmet);
            Assert.That(_seats[5].Read().Credits, Is.EqualTo(900-_rules.helmetPrice));
            Buy(5, BuySlot.Armor); Buy(5, BuySlot.Armor, true);
            Assert.That(_seats[5].Read().Draft.Helmet, Is.True);
            Assert.That(_seats[5].Read().Draft.Armor, Is.False);
            Buy(6, BuySlot.Armor);
            Until(RoundPhase.Contest);
            Assert.That(_seats[5].Read().Helmet && !_seats[5].Read().Armor, Is.True);
            Assert.That(_seats[6].Read().Armor && !_seats[6].Read().Helmet, Is.True);
            var gun = _catalog.Find<WeaponDefinition>(_seats[0].Read().WeaponId);
            _arena.Targets[0]=5; _arena.Targets[1]=6;
            Input(0,new SeatIntent{Fire=true}); Input(1,new SeatIntent{Fire=true}); _match.Step();
            Assert.That(_seats[5].Read().Health, Is.EqualTo(100-gun.DamageAt(0,HitZone.Head,true)));
            Assert.That(_seats[6].Read().Health, Is.Zero);
            foreach (var zone in new[]{HitZone.Chest,HitZone.Abdomen,HitZone.Legs})
            {
                Assert.That(gun.DamageAt(0,zone,false,true),Is.EqualTo(gun.DamageAt(0,zone,false)));
                Assert.That(gun.DamageAt(0,zone,true,false),Is.EqualTo(gun.DamageAt(0,zone,true)));
            }
            Until(RoundPhase.Settlement); Until(RoundPhase.Buy);
            Assert.That(_seats[5].Read().Helmet && !_seats[5].Read().Armor,Is.True);
            Assert.That(_seats[6].Read().Armor || _seats[6].Read().Helmet,Is.False);
        }

        [Test] public void FeedKeepsTheLethalShooterAndAirborneStateThroughASimultaneousTrade()
        {
            Until(RoundPhase.Contest);
            _arena.Targets[0]=_arena.Targets[1]=_arena.Targets[2]=5;
            _arena.Zones[0]=HitZone.Legs; _arena.Air[1]=true;
            _arena.Targets[5]=1;
            foreach (int i in new[]{0,1,2,5}) Input(i,new SeatIntent{Fire=true});
            _match.Step();
            Assert.That(_match.EliminationCount,Is.EqualTo(2));
            Assert.That(_match.TryGetElimination(1,out var trade),Is.True);
            Assert.That(_match.TryGetElimination(2,out var kill),Is.True);
            Assert.That(trade.Attacker,Is.EqualTo(5));Assert.That(trade.Airborne,Is.False);
            Assert.That(kill.Attacker,Is.EqualTo(1));Assert.That(kill.Victim,Is.EqualTo(5));
            Assert.That(kill.Airborne && kill.Headshot && !kill.Knife,Is.True);
            Assert.That(kill.WeaponId,Is.EqualTo("weapon.street20"));
            Assert.That(kill.From,Is.EqualTo(_seats[1].Read().DisplayId));Assert.That(_match.TryGetElimination(3,out _),Is.False);
        }

        [Test] public void MeleeUsesItsOwnFeedBadgeAndNeverCreditsAHeadshot()
        {
            _rules.contestSeconds=10; Until(RoundPhase.Contest);
            Input(0,new SeatIntent{Select=HeldSlot.Knife}); _match.Step();
            for(int t=0;t<30;t++) _match.Step();
            _arena.Targets[0]=5; _arena.Air[0]=true;
            for(int t=0;t<80 && _match.EliminationCount==0;t++)
            { Input(0,new SeatIntent{Fire=true}); _match.Step(); }
            Assert.That(_match.TryGetElimination(1,out var kill),Is.True);
            Assert.That(kill.Knife && kill.Airborne && !kill.Headshot,Is.True);
            Assert.That(kill.WeaponId,Is.EqualTo(_seats[0].Read().KnifeId));
        }

        [TestCase("weapon.feather08", .18f, .045f)]
        [TestCase("weapon.longglass96", 4.62f, .10f)]
        public void SniperRoundsUseTheCurrentStanceAndResumeTheSelectedLensAfterBolting(string id,float hip,float scoped)
        {
            _match=new LocalMatch(_catalog,_rules,_arena,139,FirstSide.Attack,WardrobeProfile.Create(_catalog),MatchMode.AimPractice);
            _seats=Enumerable.Range(0,10).Select(_match.BindSeat).ToArray();
            void Act(SeatIntent intent=default) {Input(0,intent);_match.Step();}
            void Advance(int ticks) {for(int t=0;t<ticks;t++) Act();}
            var gun=_catalog.Find<WeaponDefinition>(id);var lens=gun.Optics;
            Act(new SeatIntent{Buy=BuySlot.Primary,ItemId=id});Advance(21);
            Assert.That(lens.Spread(0,0,true,0),Is.EqualTo(hip).Within(.0001));
            Assert.That(lens.Spread(0,0,true,1),Is.EqualTo(scoped).Within(.0001));
            Assert.That(lens.Spread(1,0,true,0),Is.GreaterThan(hip+5));
            Assert.That(lens.Spread(0,0,false,1),Is.GreaterThan(8));
            Act(new SeatIntent{Fire=true});Assert.That(_arena.Rays.Last().magnitude,Is.InRange(0,hip));
            int bolt=Mathf.CeilToInt(60f*MatchRules.TickRate/gun.ShotsPerMinute);
            Advance(bolt);Act(new SeatIntent{Zoom=true});
            Assert.That(_seats[0].Read().ScopeStability,Is.Zero);
            Advance(4);Assert.That(_seats[0].Read().ScopeStability,Is.InRange(.4f,.7f));
            Advance(5);Assert.That(_seats[0].Read().Magnification,Is.EqualTo(1.5f));
            Act(new SeatIntent{Zoom=true});Advance(8);Assert.That(_seats[0].Read().Magnification,Is.EqualTo(2.5f));
            Act(new SeatIntent{Fire=true});Assert.That(_arena.Rays.Last().magnitude,Is.InRange(0,scoped));
            Assert.That(_seats[0].Read().Bolting,Is.True);Assert.That(_seats[0].Read().ScopeLevel,Is.Zero);
            Advance(bolt-1);Assert.That(_seats[0].Read().ScopeLevel,Is.Zero);
            Advance(9);Assert.That(_seats[0].Read().Magnification,Is.EqualTo(2.5f));
            Act(new SeatIntent{Zoom=true});Assert.That(_seats[0].Read().ScopeLevel,Is.Zero);
            Act(new SeatIntent{Zoom=true});Advance(10);Act(new SeatIntent{Reload=true});
            Assert.That(_seats[0].Read().ScopeLevel,Is.Zero);Assert.That(_seats[0].Read().Bolting,Is.False);
            Advance(MatchRules.Ticks(gun.ReloadSeconds)+2);
            Act(new SeatIntent{Zoom=true});Advance(9);Act(new SeatIntent{Fire=true});
            Act(new SeatIntent{Select=HeldSlot.Sidearm});Advance(bolt+8);
            Assert.That(_seats[0].Read().ScopeLevel,Is.Zero);
            Assert.That(_seats[0].Read().WeaponId,Is.Not.EqualTo(id));
        }

        [TestCase("weapon.feather08")][TestCase("weapon.longglass96")]
        public void StationaryAndMovingShotSamplesRespectEachSniperCone(string id)
        {
            var gun=_catalog.Find<WeaponDefinition>(id);
            var profile=WardrobeProfile.Create(_catalog);
            var maximum=new float[4];
            for(int condition=0;condition<4;condition++)
            {
                for(int seed=0;seed<48;seed++)
                {
                    var world=new Arena();
                    var match=new LocalMatch(_catalog,_rules,world,seed,FirstSide.Attack,profile,MatchMode.AimPractice);
                    var port=match.BindSeat(0);
                    for(int t=1;t<=35;t++)
                    {
                        world.Air[0]=condition==3;
                        var input=new SeatIntent{Move=condition==2?Vector2.right:Vector2.zero,Fire=t==35,Zoom=t==22 && condition==1};
                        if(t==1){input.Buy=BuySlot.Primary;input.ItemId=id;}
                        port.Submit(t,input);match.Step();
                    }
                    maximum[condition]=Mathf.Max(maximum[condition],world.Rays.Single().magnitude);
                }
            }
            float hip=gun.Optics.hipDegrees, scoped=gun.Optics.scopedDegrees;
            Assert.That(maximum[0],Is.InRange(hip*.85f,hip));
            Assert.That(maximum[1],Is.InRange(scoped*.85f,scoped));
            Assert.That(maximum[2],Is.GreaterThan(hip+3f));
            Assert.That(maximum[3],Is.GreaterThan(hip+6f));
            TestContext.WriteLine("{0} / maximum angular offset for 48 seeds each (hip, scope, run, air), degrees: {1}",
                id,string.Join(", ",maximum.Select(a=>a.ToString("F3"))));
        }

        private void Until(RoundPhase phase)
        {
            for (int i = 0; i < 100 && _match.Public.Phase != phase; i++) _match.Step();
            Assert.That(_match.Public.Phase, Is.EqualTo(phase));
        }
        private void Input(int i, SeatIntent input) => Assert.That(_seats[i].Submit(_match.Public.Tick + 1, input), Is.True);
        private void Hold(int i, int ticks)
        {
            for (int t = 0; t < ticks; t++) { Input(i, new SeatIntent { Interact = true }); _match.Step(); }
        }
        private void ShootTeam(int team)
        {
            for (int i = 0; i < 5; i++)
            {
                int shooter = team * 5 + i; _arena.Targets[shooter] = (1 - team) * 5 + i;
                Input(shooter, new SeatIntent { Fire = true });
            }
        }
        private void Plant()
        {
            Until(RoundPhase.Contest);
            int carrier = Array.FindIndex(_seats, seat => seat.Read().HasObjective);
            _arena.Points[carrier] = _catalog.Map.SitePosition(Site.A);
            Hold(carrier, 1 + MatchRules.Ticks(_rules.plantSeconds));
            Assert.That(_match.Public.Phase, Is.EqualTo(RoundPhase.Planted));
        }

        [TestCase(13, 0)] [TestCase(13, 11)] [TestCase(13, 12)]
        public void MatchEndsOnlyAtThirteenAndResetsSideBudgets(int wins, int losses)
        {
            var profile = WardrobeProfile.Create(_catalog);
            profile.current.outfitId = profile.current.attackOutfitId = "outfit.blackmiku";
            profile.current.defendOutfitId = "outfit.lily";
            _match = new LocalMatch(_catalog, _rules, _arena, 29, FirstSide.Attack, profile);
            _seats = Enumerable.Range(0, LocalMatch.Seats).Select(_match.BindSeat).ToArray();
            var attackers = CharactersOnSide(Side.Attack);
            var defenders = CharactersOnSide(Side.Defend);
            Assert.That(attackers, Is.EquivalentTo(new[] { "character.flor", "character.blackmiku", "character.laevatain" }));
            Assert.That(defenders, Is.EquivalentTo(new[] { "character.mornye", "character.rover", "character.lily" }));
            int attackCount = 0;
            for (int round = 1; round <= wins + losses; round++)
            {
                Assert.That(_match.Public.Round, Is.EqualTo(round));
                var player = _seats[0].Read();
                Assert.That(player.OutfitId, Is.EqualTo(profile.current.OutfitFor(player.Side)), $"Round {round}");
                Assert.That(CharactersOnSide(Side.Attack), Is.EquivalentTo(attackers), $"Round {round}");
                Assert.That(CharactersOnSide(Side.Defend), Is.EquivalentTo(defenders), $"Round {round}");
                if (_seats[0].Read().Side == Side.Attack && round <= 24) attackCount++;
                if (round == 13 || round == 25)
                {
                    if (round == 13) Assert.That(_seats[0].Read().Side, Is.EqualTo(Side.Defend));
                    foreach (var seat in _seats) Assert.That(seat.Read().Credits, Is.EqualTo(round == 25 ? 4000 : 900));
                }
                Until(RoundPhase.Contest);
                int winningTeam = round <= 2 * losses && round % 2 == 0 ? 1 : 0;
                ShootTeam(winningTeam); _match.Step();
                Assert.That(_match.Public.Phase, Is.EqualTo(RoundPhase.Settlement));
                Until(round == wins + losses ? RoundPhase.Finished : RoundPhase.Buy);
            }
            Assert.That(attackCount, Is.EqualTo(12));
            Assert.That(_match.Public.Score0, Is.EqualTo(wins));
            Assert.That(_match.Public.Score1, Is.EqualTo(losses));
            _match.Step(); Assert.That(_match.Public.Score0, Is.EqualTo(wins));
        }

        private string CharacterAt(SeatView seat)
        {
            Assert.That(_match.RosterOutfit(seat.Seat), Is.EqualTo(seat.OutfitId));
            var outfit = _catalog.Find<OutfitDefinition>(seat.OutfitId);
            Assert.That(outfit, Is.Not.Null, $"Seat {seat.Seat} outfit {seat.OutfitId}");
            Assert.That(outfit.Archived, Is.False);
            Assert.That(outfit.Character, Is.Not.Null, outfit.Id);
            Assert.That(outfit.Character.Archived, Is.False);
            Assert.That(outfit.Character.Faction, Is.EqualTo(seat.Side), $"Seat {seat.Seat}");
            return outfit.Character.Id;
        }

        private HashSet<string> CharactersOnSide(Side side, bool aliveOnly = false) =>
            _seats.Select(seat => seat.Read())
                .Where(seat => seat.Side == side && (!aliveOnly || seat.Health > 0))
                .Select(CharacterAt).ToHashSet();

        [Test] public void RoundClockBeatsLatePlantButCompletedDefuseWinsFuseTie()
        {
            Until(RoundPhase.Contest); _arena.Points[0] = _catalog.Map.SitePosition(Site.A);
            while (_match.Public.RemainingTicks > MatchRules.Ticks(_rules.plantSeconds) + 1) _match.Step();
            Hold(0, _match.Public.RemainingTicks); Assert.That(_match.Public.Reason, Is.EqualTo(WinReason.TimeExpired));
            Until(RoundPhase.Buy); Plant(); _arena.Points[5] = _catalog.Map.SitePosition(Site.A);
            while (_match.Public.RemainingTicks > MatchRules.Ticks(_rules.defuseSeconds) + 1) _match.Step();
            Hold(5, _match.Public.RemainingTicks); Assert.That(_match.Public.Reason, Is.EqualTo(WinReason.Defused));
        }

        [TestCase(false, Side.Defend)] [TestCase(true, Side.Attack)]
        public void SimultaneousEliminationUsesObjectiveState(bool planted, Side winner)
        {
            if (planted) Plant(); else Until(RoundPhase.Contest);
            ShootTeam(0); ShootTeam(1); _match.Step();
            Assert.That(_match.Public.Winner, Is.EqualTo(winner));
            Assert.That(_match.Public.Score0 + _match.Public.Score1, Is.EqualTo(1));
        }

        [Test] public void PlantedObjectiveSurvivesAttackingTeamAndSpectatorGetsPublicOnly()
        {
            Plant(); ShootTeam(1); _match.Step();
            Assert.That(_match.Public.Phase, Is.EqualTo(RoundPhase.Planted));
            var publicView = _seats[0].WatchTeam(); Assert.That(publicView.Seat, Is.EqualTo(-1));
            Assert.That(publicView.Match.PlantedSite, Is.EqualTo(Site.A)); Assert.That(publicView.VisibleEnemy, Is.Null);
            Assert.That(publicView.WeaponId, Is.Null);
        }

        [Test] public void OneDefuserOwnsPrivateProgressAndPauseFreezesIt()
        {
            Plant(); _arena.Points[5] = _arena.Points[6] = _catalog.Map.SitePosition(Site.A);
            for (int t = 0; t < 2; t++)
            { Input(5, new SeatIntent { Interact = true }); Input(6, new SeatIntent { Interact = true }); _match.Step(); }
            float progress = _seats[5].Read().InteractionFraction;
            Assert.That(progress, Is.GreaterThan(0)); Assert.That(_seats[6].Read().InteractionFraction, Is.Zero);
            int tick = _match.Public.Tick;
            _match.SetPaused(true); _match.Step();
            Assert.That(_match.Public.Tick, Is.EqualTo(tick)); Assert.That(_seats[5].Read().InteractionFraction, Is.EqualTo(progress));
            _match.SetPaused(false); Hold(6, 2);
            Assert.That(_seats[5].Read().InteractionFraction, Is.Zero);
            Assert.That(_seats[6].Read().InteractionFraction, Is.EqualTo(progress));
        }

        [Test] public void SeatPortRejectsReplayFutureAndInvalidInputAndHidesEnemyObjective()
        {
            Assert.That(_seats[5].Read().ObjectiveHolder, Is.EqualTo(-1));
            Assert.That(_seats[0].Read().ObjectiveHolder, Is.EqualTo(0));
            Assert.That(_seats[0].Submit(2, default), Is.False);
            Assert.That(_seats[0].Submit(1, new SeatIntent { Yaw = float.NaN }), Is.False);
            Input(0, default); Assert.That(_seats[0].Submit(1, default), Is.False);
            _match.Step(); Assert.That(_seats[0].Submit(1, default), Is.False);
        }

        [TestCase(false, 1900)] [TestCase(true, 2200)]
        public void FirstLossPaysTheDesignBillOnce(bool planted, int credits)
        {
            Input(0, new SeatIntent { Buy = BuySlot.Armor }); _match.Step();
            Input(0, new SeatIntent { Buy = BuySlot.Helmet }); _match.Step();
            Input(0, new SeatIntent { Buy = BuySlot.Utility, ItemId = "utility.smoke" }); _match.Step();
            Assert.That(_seats[0].Read().Credits, Is.Zero);
            if (planted) { Plant(); _arena.Points[5] = _catalog.Map.SitePosition(Site.A); Hold(5, 1 + MatchRules.Ticks(_rules.defuseSeconds)); }
            else Until(RoundPhase.Settlement);
            Assert.That(_seats[0].Read().Credits, Is.EqualTo(credits)); Until(RoundPhase.Buy);
            if (!planted) { Until(RoundPhase.Settlement); Assert.That(_seats[0].Read().Credits, Is.EqualTo(4300)); }
        }

        [Test] public void OrdersRejectWrongSideAndRefundOnlyTheCurrentOrder()
        {
            Input(0, new SeatIntent { Buy = BuySlot.Sidearm, ItemId = "weapon.verdict57" }); _match.Step();
            Assert.That(_seats[0].Read().Credits, Is.EqualTo(900));
            Input(0, new SeatIntent { Buy = BuySlot.Sidearm, ItemId = "weapon.dash9" }); _match.Step();
            Assert.That(_seats[0].Read().Credits, Is.EqualTo(400));
            for (int i = 0; i < 2; i++)
            { Input(0, new SeatIntent { Buy = BuySlot.Sidearm, CancelPurchase = true }); _match.Step(); }
            Until(RoundPhase.Contest);
            Assert.That(_seats[0].Read().WeaponId, Is.EqualTo("weapon.street20"));
            Assert.That(_seats[0].Read().Credits, Is.EqualTo(900));
        }

        [Test] public void RoundReceiptsRecordOverflowAndNeverPayTwice()
        {
            _rules.walletCap = 3000;
            Until(RoundPhase.Contest); ShootTeam(0); _match.Step();
            var receipt = _match.Reports.Single().Receipts[0];
            Assert.That(receipt.Income, Is.EqualTo(2700)); Assert.That(receipt.Balance, Is.EqualTo(3000));
            Assert.That(receipt.Overflow, Is.EqualTo(600)); Assert.That(receipt.Survived, Is.True);
            _match.Step(); Assert.That(_match.Reports.Count, Is.EqualTo(1));
        }

        [Test] public void FreezePermitsAimAndDropButRejectsJumpDuckMovementAndShots()
        {
            var start=_seats[0].Read();
            Input(0,new SeatIntent { Yaw=116,Pitch=-28,Move=Vector2.up,Jump=true,Crouch=true,Fire=true });
            _match.Step(); var view=_seats[0].Read();
            Assert.That(view.Yaw,Is.EqualTo(116)); Assert.That(view.Pitch,Is.EqualTo(-28));
            Assert.That(view.Motion.Velocity,Is.EqualTo(Vector2.zero));Assert.That(view.Motion.Crouch,Is.Zero);
            Assert.That(view.Magazine,Is.EqualTo(start.Magazine));
            Input(0,new SeatIntent { Buy=BuySlot.Sidearm,ItemId="weapon.dash9" });_match.Step();
            Input(0,new SeatIntent { DropHeld=true });_match.Step();
            Assert.That(_match.GroundWeapons.Single().Weapon.Definition.Id,Is.EqualTo("weapon.dash9"));
            Assert.That(_seats[0].Read().Held,Is.EqualTo(HeldSlot.Knife));
            Assert.That(_seats[0].Read().HasObjective,Is.True);
            Input(0,new SeatIntent { Interact=true });_match.Step();
            Assert.That(_seats[0].Read().Held,Is.EqualTo(HeldSlot.Sidearm));Assert.That(_match.GroundWeapons,Is.Empty);
        }

        [Test] public void LiveShopRequiresOwnSpawnAndExpiresAtConfiguredTick()
        {
            _rules.liveBuySeconds=.12f;Until(RoundPhase.Contest);
            var home=_arena.Points[0];_arena.Points[0]=_arena.Points[5];
            Input(0,new SeatIntent { Buy=BuySlot.Armor });_match.Step();
            Assert.That(_seats[0].Read().CanBuy,Is.False);Assert.That(_seats[0].Read().Armor,Is.False);
            _arena.Points[0]=home;Input(0,new SeatIntent { Buy=BuySlot.Armor });_match.Step();
            Assert.That(_seats[0].Read().Armor,Is.True);Assert.That(_seats[0].Read().Credits,Is.EqualTo(900 - _rules.armorPrice));
            while (_seats[0].Read().BuyRemaining>1) _match.Step();
            Input(0,new SeatIntent {Buy=BuySlot.Utility,ItemId="utility.smoke"});_match.Step();
            Assert.That(_seats[0].Read().CanBuy,Is.False);Assert.That(_seats[0].Read().UtilityId,Is.Null);
        }

        [Test] public void LoosePackOnlyGoesToAttackersAndCannotPlantAwayFromSite()
        {
            Input(0,new SeatIntent { Select=HeldSlot.Objective,DropHeld=true });_match.Step();
            Assert.That(_seats[0].Read().HasObjective,Is.False);
            var point=_seats[0].Read().DroppedObjective.Value;_arena.Points[5]=point;
            Input(5,new SeatIntent { Interact=true });_match.Step();
            Assert.That(_seats[5].Read().HasObjective,Is.False);
            _arena.Points[1]=point;_match.Step();
            Assert.That(_seats[1].Read().HasObjective,Is.True);Until(RoundPhase.Contest);
            Hold(1,MatchRules.Ticks(_rules.plantSeconds)+1);Assert.That(_match.Public.PlantedSite,Is.EqualTo(Site.None));
            _arena.Points[1]=_catalog.Map.SitePosition(Site.B);Hold(1,2);_match.Step();
            Assert.That(_seats[1].Read().InteractionFraction,Is.Zero);
            Hold(1,MatchRules.Ticks(_rules.plantSeconds)+1);
            Assert.That(_match.Public.Phase,Is.EqualTo(RoundPhase.Planted));Assert.That(_match.Public.PlantedSite,Is.EqualTo(Site.B));
            Assert.That(_match.Public.Score0+_match.Public.Score1,Is.Zero);
            Assert.That(_match.Public.RemainingTicks,Is.EqualTo(MatchRules.Ticks(_rules.plantedSeconds)));
        }

        [TestCase(true)] [TestCase(false)] public void DefuseUsesKitOfCurrentOperator(bool kit)
        {
            _rules.kitDefuseSeconds=.04f;
            if(kit){Input(5,new SeatIntent{Buy=BuySlot.Kit});_match.Step();}
            Plant();int ticks=MatchRules.Ticks(kit?_rules.kitDefuseSeconds:_rules.defuseSeconds);
            _arena.Points[5]=_catalog.Map.SitePosition(Site.A);Hold(5,ticks);
            Assert.That(_match.Public.Phase,Is.EqualTo(RoundPhase.Planted));Hold(5,1);
            Assert.That(_match.Public.Reason,Is.EqualTo(WinReason.Defused));
        }

        [Test] public void ExpiredDeviceDamagesNearbyPlayersAndLocksTheResult()
        {
            Plant();_arena.Points[6]=_arena.Points[0];_arena.Points[5]=_arena.Points[0]+Vector3.right*50;
            Until(RoundPhase.Settlement);
            Assert.That(_match.Public.Reason,Is.EqualTo(WinReason.DeviceExpired));
            Assert.That(_seats[6].Read().Health,Is.Zero);Assert.That(_seats[5].Read().Health,Is.EqualTo(100));
            Assert.That(_match.Reports.Count,Is.EqualTo(1));
        }

        [Test] public void AfterRoundWinSurvivorsMoveKillAndLoseCarriedGearWithoutAnotherAward()
        {
            _rules.settlementSeconds=.60f;Until(RoundPhase.Settlement);
            var report=_match.Reports.Single(); int balance=_seats[5].Read().Credits;
            _arena.Targets[0]=5;
            Input(0,new SeatIntent{Yaw=70,Move=Vector2.right,Fire=true});_match.Step();
            Assert.That(_seats[0].Read().Motion.Velocity.x,Is.GreaterThan(0));Assert.That(_seats[5].Read().Health,Is.Zero);
            Assert.That(_seats[5].Read().Credits,Is.EqualTo(balance));Assert.That(_match.Public.Winner,Is.EqualTo(Side.Defend));
            Until(RoundPhase.Buy); Assert.That(report.Receipts[5].Survived,Is.False);
            Assert.That(_match.Reports.Count,Is.EqualTo(1));Assert.That(_match.Public.Score1,Is.EqualTo(1));
        }

        [Test] public void WheelSkipsEmptySlotsAndReloadIsInterruptedBySwitch()
        {
            _rules.contestSeconds=4; Until(RoundPhase.Contest);
            Input(0,new SeatIntent{Fire=true});_match.Step();
            int rounds=_seats[0].Read().Magazine;
            Input(0,new SeatIntent{Reload=true});_match.Step();
            Assert.That(_seats[0].Read().ReloadEnd,Is.GreaterThan(_match.Public.Tick));
            Input(0,new SeatIntent{Cycle=1});_match.Step();
            Assert.That(_seats[0].Read().Held,Is.EqualTo(HeldSlot.Knife));Assert.That(_seats[0].Read().ReloadEnd,Is.Zero);
            Input(0,new SeatIntent{Cycle=-1});_match.Step();Assert.That(_seats[0].Read().Magazine,Is.EqualTo(rounds));
            for (int i=0;i<20;i++) _match.Step();Input(0,new SeatIntent{Inspect=true});_match.Step();
            Assert.That(_seats[0].Read().InspectTick,Is.EqualTo(_match.Public.Tick));
            Input(0,new SeatIntent{Fire=true});_match.Step();Assert.That(_seats[0].Read().InspectTick,Is.LessThan(0));
        }

        [TestCase("weapon.ember47", 108)] [TestCase("weapon.fold44", 89)] [TestCase("weapon.watch41s", 98)]
        public void DamageUsesThePublishedFormulaAndFloor(string id, int damage)
        {
            var weapon = _catalog.Find<WeaponDefinition>(id);
            Assert.That(weapon.DamageAt(15, HitZone.Head, true), Is.EqualTo(damage));
            Assert.That(weapon.DamageAt(15, HitZone.Legs, true), Is.EqualTo(weapon.DamageAt(15, HitZone.Legs, false)));
            Assert.That(weapon.IsSteady(weapon.RunSpeed * 0.34f, true), Is.True);
        }
    }
}
