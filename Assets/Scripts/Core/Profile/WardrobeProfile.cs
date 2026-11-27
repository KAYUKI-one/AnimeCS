using System;
using System.Collections.Generic;
using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;

namespace AnimeCS.Profile
{
    [Serializable]
    public sealed class SkinChoice
    {
        public string weaponId;
        public string skinId;
    }

    [Serializable]
    public sealed class WardrobeLoadout
    {
        public string outfitId, attackOutfitId, defendOutfitId;
        public string OutfitFor(Side side) => side == Side.Attack ? attackOutfitId : defendOutfitId;
        public void Select(OutfitDefinition outfit)
        {
            outfitId=outfit.Id;
            if(outfit.Character.Faction==Side.Attack) attackOutfitId=outfit.Id;
            else defendOutfitId=outfit.Id;
        }
        public string primaryId, sidearmId, knifeId;
        public WornItems worn;
        public bool hasFactionGear;
        public WardrobeSideGear attackGear, defendGear;
        public List<SkinChoice> skins = new List<SkinChoice>();

        public WardrobeSideGear GearFor(Side side) =>
            !hasFactionGear ? null : side == Side.Attack ? attackGear : defendGear;
        public WornItems WornFor(Side side) => GearFor(side)?.worn ?? worn;

        public string SkinFor(string weaponId)
        {
            foreach (var choice in skins)
                if (choice != null && choice.weaponId == weaponId) return choice.skinId;
            return null; // Empty = the weapon's factory appearance.
        }
    }

    [Serializable]
    public sealed class WardrobeProfile
    {
        public int version = 2;
        public List<string> owned = new List<string>();
        public WardrobeLoadout current = new WardrobeLoadout();
        public WardrobeLoadout[] presets = new WardrobeLoadout[2];

        internal bool Owns(ContentDefinition item) => item && !item.Archived && owned?.Contains(item.Id) == true;

        internal bool CanEquip(ContentDefinition item, Side side, CharacterDefinition character)
        {
            if (!Owns(item)) return false;
            if (item is WearableDefinition wear) return wear.Fits(character);
            var weapon = item is WeaponSkinDefinition skin ? skin.Weapon : item as WeaponDefinition;
            return Owns(weapon) && WardrobeSideGear.Available(weapon, side);
        }

        internal bool TryEquip(WardrobeLoadout draft, ContentDefinition item, Side side, CharacterDefinition character,
            out WeaponDefinition weapon)
        {
            weapon = null;
            var gear = draft?.GearFor(side);
            if (gear == null || !CanEquip(item, side, character)) return false;
            if (item is WearableDefinition wear)
            {
                gear.worn.Set(wear.Slot, wear.Id);
                return true;
            }

            var skin = item as WeaponSkinDefinition;
            weapon = skin ? skin.Weapon : (WeaponDefinition)item;
            string weaponId = weapon.Id;
            gear.SetWeapon(weapon.Class, weaponId);
            draft.skins ??= new List<SkinChoice>();
            draft.skins.RemoveAll(choice => choice?.weaponId == weaponId);
            if (skin) draft.skins.Add(new SkinChoice { weaponId = weaponId, skinId = skin.Id });
            return true;
        }

        public static WardrobeProfile Create(ContentCatalog catalog)
        {
            var profile = new WardrobeProfile();
            foreach (var entry in catalog.Entries)
                if (entry && !entry.Archived && (entry is OutfitDefinition || entry is WeaponSkinDefinition)) profile.owned.Add(entry.Id);
            profile.Repair(catalog);
            return profile;
        }

        public void Repair(ContentCatalog catalog)
        {
            owned ??= new List<string>();
            owned.RemoveAll(id=>{var item=catalog.Find<ContentDefinition>(id); return !item || item.Archived;});
            foreach (var item in catalog.Entries)
                if (item && !item.Archived && !owned.Contains(item.Id) && (item is WeaponDefinition || item is CharacterDefinition ||
                    item is OutfitDefinition outfit && outfit.Included || item is WearableDefinition wear && wear.Included ||
                    item is WeaponSkinDefinition skin && skin.Weapon && (skin.Weapon.DefaultSkin==skin || skin.Included))) owned.Add(item.Id);
            current = Resolve(current, catalog);
            if (presets == null || presets.Length != 2) Array.Resize(ref presets, 2);
            for (int i = 0; i < presets.Length; i++) presets[i] = Resolve(presets[i], catalog);
        }


        public WardrobeLoadout Resolve(WardrobeLoadout source, ContentCatalog catalog)
        {
            var result = new WardrobeLoadout { outfitId = catalog.Outfit.Id };
            var outfit = catalog.Find<OutfitDefinition>(source?.outfitId);
            bool Allowed(OutfitDefinition f) => f && !f.Archived && f.Character && !f.Character.Archived && owned.Contains(f.Id);
            if (Allowed(outfit)) result.outfitId = outfit.Id;
            else if (!Allowed(catalog.Outfit))
                foreach (var entry in catalog.Entries)
                    if (entry is OutfitDefinition ownedOutfit && Allowed(ownedOutfit)) { result.outfitId=ownedOutfit.Id; break; }
            string ResolveSide(Side side)
            {
                var saved = catalog.Find<OutfitDefinition>(source?.OutfitFor(side));
                if (Allowed(saved) && saved.Character.Faction==side) return saved.Id;
                if (Allowed(outfit) && outfit.Character.Faction==side) return outfit.Id;
                var fallback = catalog.OutfitForSide(side);
                if (Allowed(fallback)) return fallback.Id;
                foreach (var entry in catalog.Entries)
                    if (entry is OutfitDefinition candidate && Allowed(candidate) && candidate.Character.Faction==side) return candidate.Id;
                return null;
            }
            result.attackOutfitId=ResolveSide(Side.Attack);
            result.defendOutfitId=ResolveSide(Side.Defend);
            string ResolveWeapon(string id, HeldSlot kind)
            {
                bool Fits(WeaponDefinition w) => w && !w.Archived && owned.Contains(w.Id) &&
                    (kind==HeldSlot.Knife ? w.Class==WeaponClass.Knife : kind==HeldSlot.Sidearm ? w.Class==WeaponClass.Pistol :
                        w.Class!=WeaponClass.Knife && w.Class!=WeaponClass.Pistol);
                var selected=catalog.Find<WeaponDefinition>(id);
                if(Fits(selected)) return selected.Id;
                foreach(var entry in catalog.Entries) if(entry is WeaponDefinition gun && Fits(gun)) return gun.Id;
                return null;
            }
            result.primaryId=ResolveWeapon(source?.primaryId,HeldSlot.Primary);
            result.sidearmId=ResolveWeapon(source?.sidearmId ?? catalog.StartingPistol(Side.Attack)?.Id,HeldSlot.Sidearm);
            result.knifeId=ResolveWeapon(source?.knifeId ?? catalog.Knife?.Id,HeldSlot.Knife);
            WornItems ResolveWear(WornItems options, CharacterDefinition character)
            {
                var resolved = new WornItems();
                foreach (WearSlot slot in Enum.GetValues(typeof(WearSlot)))
                {
                    var item = catalog.Find<WearableDefinition>(options.Get(slot));
                    if (item && !item.Archived && item.Slot == slot && item.Fits(character) && owned.Contains(item.Id))
                        resolved.Set(slot, item.Id);
                }
                return resolved;
            }

            WardrobeSideGear ResolveGear(Side side)
            {
                var saved = source?.GearFor(side);
                var fit = catalog.Find<OutfitDefinition>(result.OutfitFor(side));
                var gear = new WardrobeSideGear
                {
                    worn = ResolveWear(saved?.worn ?? source?.worn ?? default, fit ? fit.Character : null)
                };
                foreach (WeaponClass category in Enum.GetValues(typeof(WeaponClass)))
                {
                    if (category == WeaponClass.HeavySniper) continue;
                    bool Eligible(WeaponDefinition w) => w && !w.Archived && owned.Contains(w.Id) &&
                        WardrobeSideGear.Category(w.Class) == category && WardrobeSideGear.Available(w, side);
                    var legacy = category == WeaponClass.Knife ? source?.knifeId :
                        category == WeaponClass.Pistol ? source?.sidearmId : source?.primaryId;
                    var selected = catalog.Find<WeaponDefinition>(saved?.Weapon(category) ?? legacy);
                    if (!Eligible(selected) && category == WeaponClass.Pistol)
                        selected = catalog.StartingPistol(side);
                    if (!Eligible(selected))
                        foreach (var entry in catalog.Entries)
                            if (entry is WeaponDefinition candidate && Eligible(candidate)) { selected = candidate; break; }
                    if (Eligible(selected)) gear.SetWeapon(category, selected.Id);
                }
                return gear;
            }
            result.attackGear = ResolveGear(Side.Attack);
            result.defendGear = ResolveGear(Side.Defend);
            result.hasFactionGear = true;
            var activeFit = catalog.Find<OutfitDefinition>(result.outfitId);
            result.worn = ResolveWear(source?.worn ?? default, activeFit ? activeFit.Character : null);
            if (source?.skins == null) return result;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var choice in source.skins)
            {
                if (choice == null) continue;
                var skin = catalog.Find<WeaponSkinDefinition>(choice.skinId);
                if (skin == null || skin.Weapon == null || skin.Weapon.Id != choice.weaponId ||
                    !owned.Contains(skin.Id) || !seen.Add(choice.weaponId)) continue;
                result.skins.Add(new SkinChoice { weaponId = choice.weaponId, skinId = skin.Id });
            }
            return result;
        }
    }
}
