using System.Collections.Generic;
using AnimeCS.Content;

namespace AnimeCS.Match
{
    public readonly struct SeatReceipt
    {
        public readonly bool Survived;
        public readonly string Primary, Sidearm, Utility;
        public readonly bool Armor, Helmet;
        public readonly int Income, Overflow, Balance;
        internal SeatReceipt(SeatInventory bag, bool survived, int income, int overflow)
        {
            Survived = survived; Income = income; Overflow = overflow; Balance = bag.Wallet;
            Primary = survived ? bag.Primary?.Definition.Id : null;
            Sidearm = survived ? bag.Sidearm?.Definition.Id : null;
            Utility = survived && bag.Utility != null ? bag.Utility.Id : null;
            Armor = survived && bag.Armor; Helmet = survived && bag.Helmet;
        }
    }

    /// <summary>Host-side round record for the post-match screen and replay exporters.</summary>
    public sealed class RoundReport
    {
        public int Round { get; }
        public int AttackTeam { get; }
        public int Score0 { get; }
        public int Score1 { get; }
        public int Tick { get; }
        public Site Site { get; }
        public Side Winner { get; }
        public WinReason Reason { get; }
        public IReadOnlyList<SeatReceipt> Receipts { get; }
        internal RoundReport(PublicMatchView view, int attackTeam, SeatReceipt[] receipts)
        {
            Round = view.Round; AttackTeam = attackTeam; Score0 = view.Score0; Score1 = view.Score1;
            Tick = view.Tick; Site = view.PlantedSite; Winner = view.Winner; Reason = view.Reason;
            Receipts = System.Array.AsReadOnly(receipts);
        }
    }
}
