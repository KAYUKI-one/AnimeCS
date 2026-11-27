using System;
using System.Collections.Generic;
using System.Linq;
using AnimeCS.Accounts;
using AnimeCS.Content;
using AnimeCS.Profile;
using UnityEngine;

namespace AnimeCS.Play
{
    public sealed partial class LocalGame
    {
        public AccountClient Account { get; private set; }

        [Serializable]
        sealed class AccountGear
        {
            public WardrobeLoadout current;
            public WardrobeLoadout[] presets;
        }

        internal string SerializeGear(WardrobeProfile profile) => JsonUtility.ToJson(new AccountGear
        {
            current = profile.current, presets = profile.presets
        });

        WardrobeProfile AccountWardrobe()
        {
            var state = Account.State;
            var wardrobe = string.IsNullOrWhiteSpace(state.loadoutJson) ? new WardrobeProfile() :
                JsonUtility.FromJson<WardrobeProfile>(state.loadoutJson) ?? new WardrobeProfile();
            wardrobe.owned = new List<string>(state.inventory ?? Array.Empty<string>());
            wardrobe.current = wardrobe.Resolve(wardrobe.current, _catalog);
            if (wardrobe.presets == null || wardrobe.presets.Length != 2) Array.Resize(ref wardrobe.presets, 2);
            for (int i = 0; i < wardrobe.presets.Length; i++)
                wardrobe.presets[i] = wardrobe.Resolve(wardrobe.presets[i], _catalog);
            return wardrobe;
        }

        string DisplayWeapon(string skinId)
        {
            var skin = _catalog.Find<WeaponSkinDefinition>(skinId);
            return skin && skin.Weapon ? skin.Weapon.Id :
                _catalog.Find<WeaponDefinition>(skinId)?.Id ?? _presentation.lobbyWeapon?.Id;
        }

        internal LobbyMember DisplayMember(string id, string characterId, string skinId, WornItems worn = default)
        {
            var outfit = _catalog.Entries.OfType<OutfitDefinition>().FirstOrDefault(o =>
                !o.Archived && o.Character && o.Character.Id == characterId);
            return new LobbyMember(outfit ? outfit.Id : _catalog.Outfit.Id, DisplayWeapon(skinId), skinId, worn, id);
        }

        internal bool LobbyNamePosition(int index,out Vector2 viewport)
        {
            if (_preview != null) return _preview.NamePosition(index,out viewport);
            viewport = default;
            return false;
        }
    }
}