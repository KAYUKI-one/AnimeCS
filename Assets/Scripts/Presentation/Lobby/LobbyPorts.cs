using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeCS.Accounts;
using AnimeCS.Content;
using AnimeCS.Match;
using AnimeCS.Profile;
using UnityEngine;

namespace AnimeCS.Play
{
    internal interface ILobbyHost
    {
        bool IsActive { get; }
        bool InLobby { get; }
        MapDefinition SelectedMap { get; }
        MapDefinition LastBombMap { get; }
        MatchMode Mode { get; }
        FirstSide OpeningSide { get; }
        string LobbyWeaponId { get; }
        LobbySettings Settings { get; }
        ILobbyPreview Preview { get; }
        ILobbyAccount Account { get; }
        WardrobeProfile ReadWardrobe();
        PictureQuality ApplyPictureQuality(PictureQuality requested);
    }

    internal readonly struct LobbySettings
    {
        internal readonly PlayAspect Aspect;
        internal readonly bool FriendlyCues, GraphicsSupported;
        internal readonly float Sensitivity, Volume;
        internal readonly int MaxSamples, MaxShadow;
        internal readonly PictureQuality CurrentQuality, DefaultQuality;

        internal LobbySettings(PlayAspect aspect, bool friendlyCues, float sensitivity, float volume,
            bool graphicsSupported, int maxSamples, int maxShadow, PictureQuality currentQuality, PictureQuality defaultQuality)
        {
            Aspect = aspect;
            FriendlyCues = friendlyCues;
            Sensitivity = sensitivity;
            Volume = volume;
            GraphicsSupported = graphicsSupported;
            MaxSamples = maxSamples;
            MaxShadow = maxShadow;
            CurrentQuality = currentQuality;
            DefaultQuality = defaultQuality;
        }
    }

    internal interface ILobbyPreview
    {
        void ShowWardrobe(WardrobeLoadout loadout, string weaponId, bool firstPerson, bool inspectItem = false,
            bool unarmed = false, WornItems? previewWorn = null, string previewSkin = null);
        void ShowItem(ContentDefinition item);
        void EndInspection();
        void Center(float viewportX, bool showcase = false, bool equipment = false);
        void Rotate(float angle);
        void ShowTeammates(IReadOnlyList<PartyMemberState> members);
        bool TryNamePosition(int index, out Vector2 viewport);
    }

    internal enum LobbyAccountAction
    {
        Refresh, SendChat, AddFriend, Invite, AcceptInvite, LeaveParty, StartParty,
        RestartBattle, EndBattle, Buy, Draw, Claim
    }

    internal readonly struct LobbyAccountIntent
    {
        internal readonly LobbyAccountAction Action;
        internal readonly string Argument;

        internal LobbyAccountIntent(LobbyAccountAction action, string argument = null)
        {
            Action = action;
            Argument = argument;
        }
    }

    internal interface ILobbyAccount
    {
        AccountState State { get; }
        string LastPlayerId { get; }
        Task AuthenticateAsync(bool register, string playerId, string password);
        Task<string> CheckUpdateAsync();
        Task RequestAsync(LobbyAccountIntent intent);
        Task SaveProfileAsync(string signature, string avatarId, string showCharacterId, string showSkinId);
        Task SaveWardrobeAsync(WardrobeProfile profile, string weaponId);
    }
}
