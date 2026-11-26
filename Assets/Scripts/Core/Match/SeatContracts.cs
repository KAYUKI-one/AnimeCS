using AnimeCS.Content;
using UnityEngine;

namespace AnimeCS.Match
{
    public enum RoundPhase { Buy, Contest, Planted, Settlement, Finished, Practice }
    public enum WinReason { None, TimeExpired, Eliminated, Defused, DeviceExpired }
    public enum HeldSlot { Current, Primary, Sidearm, Knife, Objective, Utility }
    public interface IDeviceFeedback { void DeviceSound(Vector3 point, bool expired); }
    public interface IMeleeWorld { ArenaHit? Swing(int seat); }
    public enum BuySlot { None, Primary, Sidearm, Armor, Utility, Kit, Knife, Helmet }
    public enum MatchMode { BotMatch, AimPractice, Online }
    public enum FirstSide { Attack, Defend, Random }

    /// <summary>One seat's inputs for one simulation tick. Directions, never hit or money outcomes.</summary>
    public struct SeatIntent
    {
        public Vector2 Move; // World X/Z axes, normalized by the referee.
        public Vector2 BrakeTap; // A direction pressed and released between ticks, braking only.
        public float Yaw, Pitch;
        public bool Fire, Reload, SwitchWeapon, Interact, DropObjective, Walk, ThrowUtility, Jump, Crouch;
        public bool DropHeld, Inspect, Zoom, LowerScope;
        public bool FirePressed;
        // Set by the host input adapter, from its measured RTT and the last acknowledged snapshot.
        internal int? ObservedTick;
        internal ushort ObservedFraction;
        public int Cycle;
        public BuySlot Buy;
        public HeldSlot Select;
        public string ItemId;
        public bool CancelPurchase;
    }

    public readonly struct Contact
    {
        public readonly int Seat;
        public readonly Vector3 Position;
        public Contact(int seat, Vector3 position) { Seat = seat; Position = position; }
    }

    public struct PublicMatchView
    {
        public int Tick, Round, Score0, Score1, RemainingTicks, PracticeKills, PracticeHeadshots;
        public RoundPhase Phase;
        public Site PlantedSite;
        public WinReason Reason;
        public Side Winner;
        public bool Paused;
    }

    public struct SeatView
    {
        public PublicMatchView Match;
        public int Seat, Health, Credits, Magazine, Reserve, ObjectiveHolder;
        public Side Side;
        public Vector3 Position;
        public float Yaw, Pitch, InteractionFraction;
        public MotionContact Motion;
        internal float PredictionSpeed;
        internal bool PredictionJustStopped;
        public int LastHitTick, LastShotTick, LastConfirmationTick, ScopeLevel;
        public float Magnification, ScopeStability;
        public bool Bolting, CanStartShot;
        public HitZone LastHitZone;
        public bool Armor, Helmet, HasObjective, HasKit, CanBuy;
        public HeldSlot Held;
        public int EquipTick, InspectTick, ReloadStart, ReloadEnd, BuyRemaining;
        public WornItems Worn;
        public string DisplayId, WeaponId, SkinId, OutfitId, UtilityId, PrimaryId, SidearmId, KnifeId;
        public Vector3? DroppedObjective;
        public Vector3? VisibleDevice;
        public Contact? VisibleEnemy;
        public SoundClue? Heard;
        public bool Blinded, InsideSmoke;
        public PurchaseDraft Draft;
        public UtilityCounts Utilities;
    }

    public struct PurchaseDraft
    {
        public string Primary, Sidearm, Utility;
        public UtilityCounts Utilities;
        public bool Armor, Helmet, Kit;
    }

    public enum SoundKind { Step, Gun, Utility, EchoTail, Objective, DevicePulse, DeviceBurst }
    public struct SoundClue
    {
        public SoundKind Kind;
        public WeaponClass WeaponClass;
        public bool Own, Suppressed;
        public string Region;
        public int Bearing, Tick, Expires; // Eight directions; never an exact source position.
    }

    /// <summary>Optional trusted environment for throwables and shared sensory rules.</summary>
    public interface IMatchSignals
    {
        void ResetRound();
        void Advance(int tick);
        void Throw(int seat, UtilityDefinition item);
        void EmitGun(int seat,WeaponDefinition gun)=>Emit(seat,SoundKind.Gun,gun.SoundRadius,gun.Suppressed);
        void Emit(int seat, SoundKind kind, float radius, bool suppressed = false);
        SoundClue? Heard(int seat);
        bool Blinded(int seat);
        bool InSmoke(int seat);
    }

    /// <summary>Bound to exactly one seat. New input providers use only this endpoint and public map data.</summary>
    public interface ISeatPort
    {
        SeatView Read();
        SeatView WatchTeam(int direction = 0);
        bool Submit(int tick, SeatIntent intent);
    }

    public interface ISeatDriver { SeatIntent Decide(in SeatView view); }

    public readonly struct ArenaHit
    {
        public readonly int Seat;
        public readonly HitZone Zone;
        public readonly float Distance;
        public ArenaHit(int seat, HitZone zone, float distance) { Seat = seat; Zone = zone; Distance = distance; }
    }

    /// <summary>Trusted geometry adapter. Only the referee calls it; a seat never receives the world.</summary>
    public interface IHistoricalMatchWorld
    {
        ArenaHit? TraceAt(int seat, Vector2 offset, bool suppressed, int currentTick, int viewTick, ushort fraction)
            => TraceAt(seat, offset, suppressed, currentTick, viewTick);
        void CaptureHitboxes(int tick);
        ArenaHit? TraceAt(int seat, Vector2 offset, bool suppressed, int currentTick, int viewTick);
    }

    public interface IMatchWorld
    {
        void Spawn(int seat, Vector3 position, float yaw);
        MotionContact Move(int seat, Vector2 velocity, float yaw, float pitch, bool jump, bool crouch);
        void SyncMovement();
        void SetAlive(int seat, bool alive);
        Vector3 Position(int seat);
        Vector3 LastGround(int seat);
        Contact? Observe(int seat, int firstEnemy, int enemyCount);
        bool SeesPoint(int seat, Vector3 point);
        ArenaHit? Trace(int seat, Vector2 angularOffset, bool suppressed);
    }
}
