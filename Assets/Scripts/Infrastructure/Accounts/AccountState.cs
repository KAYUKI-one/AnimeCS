using System;
#nullable disable
#pragma warning disable CS0649 // Public wire fields are populated by the appropriate platform serializer.

namespace AnimeCS.Accounts
{
    [Serializable] public sealed class AccountState
    {
        public string id, signature, avatarId, rank, showCharacterId, showSkinId, loadoutJson, lastDrawId;
        public int level, xp, rankPoints, victories, credits, tickets, drawCost;
        public string[] inventory, friends, invites, party, drawPool;
        public ChatLine[] messages;
        public PartyMemberState[] members;
        public ShopOffer[] offers;
        public TaskState[] tasks;
        public string battleId, battleHost, battleTicket, mapId, contentVersion;
        public bool battleFinished, localPracticeProgress;
        public int battlePort;
    }

    [Serializable] public sealed class ChatLine { public string id, text; }
    [Serializable] public sealed class PartyMemberState { public string id, showCharacterId, showSkinId; }
    [Serializable] public sealed class ShopOffer { public string id, itemId; public int price; }
    [Serializable] public sealed class TaskState
    {
        public string id, title, description, eventKey;
        public int progress, goal, experience, credits;
        public bool claimed;
    }

    [Serializable] internal sealed class AccountReply { public string token, error; public AccountState state; }
    [Serializable] internal sealed class AccountRequest
    {
        public string id, password, signature, avatarId, showCharacterId, showSkinId, loadoutJson, text, eventId, kind, battleId;
        public int amount;
    }
}
