using AnimeCS.Content;

namespace AnimeCS.Match
{
    /// <summary>Public result of a lethal hit, with the attacker's state at the firing tick.</summary>
    public readonly struct Elimination
    {
        public readonly int Tick, Round, Attacker, Victim;
        public readonly string From, To, WeaponId;
        public readonly bool Headshot, Airborne, Knife;
        internal Elimination(int tick,int round,int attacker,int victim,string from,string to,
            WeaponDefinition weapon,HitZone zone,bool airborne)
        {
            Tick=tick; Round=round; Attacker=attacker; Victim=victim; From=from; To=to;
            WeaponId=weapon ? weapon.Id : null; Knife=weapon && weapon.Class==WeaponClass.Knife;
            Headshot=weapon && !Knife && zone==HitZone.Head; Airborne=airborne;
        }
    }

    public sealed partial class LocalMatch
    {
        private readonly Elimination[] _recentKills=new Elimination[32];
        private readonly Elimination?[] _lethalHit=new Elimination?[Seats];
        public int EliminationCount { get; private set; }

        public bool TryGetElimination(int sequence,out Elimination kill)
        {
            kill=default;
            if (sequence<=0 || sequence>EliminationCount || sequence<=EliminationCount-_recentKills.Length) return false;
            kill=_recentKills[(sequence-1)%_recentKills.Length]; return true;
        }

        private void WeaponDamage(int attacker,int victim,WeaponDefinition weapon,HitZone zone,int amount)
        {
            var target=_seats[victim];
            if (amount<=0 || _damage[victim]>=target.Health) return;
            _seats[attacker].LastConfirmationTick=_tick;
            _damage[victim]+=amount; target.LastHitZone=zone;
            if (_damage[victim]>=target.Health)
                _lethalHit[victim]=new Elimination(_tick,_round,attacker,victim,_seats[attacker].DisplayId,target.DisplayId,
                    weapon,zone,!_seats[attacker].Motion.Grounded);
        }

        private void RecordElimination(int victim)
        {
            var kill=_lethalHit[victim] ?? new Elimination(_tick,_round,-1,victim,null,_seats[victim].DisplayId,null,HitZone.Chest,false);
            _recentKills[EliminationCount%_recentKills.Length]=kill; EliminationCount++;
            if (_phase==RoundPhase.Practice && kill.Attacker==0)
            {
                _practiceKills++; if(kill.Headshot) _practiceHeadshots++;
            }
        }
    }
}

