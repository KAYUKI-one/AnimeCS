using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnimeCS.Accounts;
using AnimeCS.Content;
using AnimeCS.Match;
using AnimeCS.Profile;
using UnityEngine;

namespace AnimeCS.Play
{
    public sealed partial class LocalGame : ILobbyHost, ILobbyPreview, ILobbyAccount
    {
        bool ILobbyHost.IsActive => this;
        MapDefinition ILobbyHost.LastBombMap => LastBombMap;
        ILobbyPreview ILobbyHost.Preview => this;
        ILobbyAccount ILobbyHost.Account => this;
        LobbySettings ILobbyHost.Settings => new(AspectMode, FriendlyCues, Sensitivity, Volume,
            GraphicsOptions?.Supported == true, GraphicsOptions?.MaxSamples ?? 0, GraphicsOptions?.MaxShadow ?? 0,
            GraphicsOptions?.Current ?? default, GraphicsOptions?.Default ?? default);

        PictureQuality ILobbyHost.ApplyPictureQuality(PictureQuality requested)
        {
            GraphicsOptions.Apply(requested, true);
            return GraphicsOptions.Current;
        }

        void ILobbyPreview.ShowWardrobe(WardrobeLoadout loadout, string weaponId, bool firstPerson, bool inspectItem,
            bool unarmed, WornItems? previewWorn, string previewSkin) =>
            PreviewWardrobe(loadout, weaponId, firstPerson, inspectItem, unarmed, previewWorn, previewSkin);

        void ILobbyPreview.ShowItem(ContentDefinition item) => InspectItem(item);
        void ILobbyPreview.EndInspection() => EndInspection();
        void ILobbyPreview.Center(float viewportX, bool showcase, bool equipment) => CenterPreview(viewportX, showcase, equipment);
        void ILobbyPreview.Rotate(float angle) => RotateLobby(angle);
        bool ILobbyPreview.TryNamePosition(int index, out Vector2 viewport) => LobbyNamePosition(index, out viewport);
        void ILobbyPreview.ShowTeammates(IReadOnlyList<PartyMemberState> members) =>
            SetLobbyTeammates(members.Select(m => DisplayMember(m.id, m.showCharacterId, m.showSkinId)).ToArray());

        AccountState ILobbyAccount.State => Account.State;
        string ILobbyAccount.LastPlayerId => PlayerPrefs.GetString("AnimeCS.LastPlayerId", "");

        async Task ILobbyAccount.AuthenticateAsync(bool register, string playerId, string password)
        {
            if (register) await Account.RegisterAsync(playerId, password);
            else await Account.LoginAsync(playerId, password);
            if (this) PlayerPrefs.SetString("AnimeCS.LastPlayerId", Account.State.id);
        }

        Task<string> ILobbyAccount.CheckUpdateAsync() => Account.CheckUpdateAsync();

        Task ILobbyAccount.RequestAsync(LobbyAccountIntent intent) => intent.Action switch
        {
            LobbyAccountAction.Refresh => Account.RefreshAsync(),
            LobbyAccountAction.SendChat => Account.SendChatAsync(intent.Argument),
            LobbyAccountAction.AddFriend => Account.AddFriendAsync(intent.Argument),
            LobbyAccountAction.Invite => Account.InviteAsync(intent.Argument),
            LobbyAccountAction.AcceptInvite => Account.AcceptInviteAsync(intent.Argument),
            LobbyAccountAction.LeaveParty => Account.LeavePartyAsync(),
            LobbyAccountAction.StartParty => Account.StartPartyAsync(),
            LobbyAccountAction.RestartBattle => Account.RestartBattleAsync(intent.Argument),
            LobbyAccountAction.EndBattle => Account.EndBattleAsync(intent.Argument),
            LobbyAccountAction.Buy => Account.BuyAsync(intent.Argument),
            LobbyAccountAction.Draw => Account.DrawAsync(),
            LobbyAccountAction.Claim => Account.ClaimAsync(intent.Argument),
            _ => throw new ArgumentOutOfRangeException(nameof(intent))
        };

        Task ILobbyAccount.SaveProfileAsync(string signature, string avatarId, string showCharacterId, string showSkinId) =>
            Account.SaveProfileAsync(signature, avatarId, showCharacterId, showSkinId);

        Task ILobbyAccount.SaveWardrobeAsync(WardrobeProfile profile, string weaponId)
        {
            if (Account.State != null) return Account.SaveLoadoutAsync(SerializeGear(profile));
            SetLobbyWeapon(weaponId);
            SaveWardrobe(profile);
            return Task.CompletedTask;
        }

        private void SetLobbyAspect(PlayAspect value) => AspectMode = value;
        private void SetLobbyFriendlyCues(bool value) => FriendlyCues = value;
        private void SetLobbySensitivity(float value) => Sensitivity = value;
        private void SetLobbyVolume(float value) => Volume = value;
    }
}
