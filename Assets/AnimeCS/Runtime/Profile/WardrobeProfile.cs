using System;
using System.Collections.Generic;
using System.IO;
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
        public List<SkinChoice> skins = new List<SkinChoice>();

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
            string ResolveSide(Side side)
            {
                var saved = catalog.Find<OutfitDefinition>(source?.OutfitFor(side));
                if (Allowed(saved) && saved.Character.Faction==side) return saved.Id;
                if (Allowed(outfit) && outfit.Character.Faction==side) return outfit.Id;
                return catalog.OutfitForSide(side)?.Id;
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
            if (source == null) return result;
            var character=catalog.Find<OutfitDefinition>(result.outfitId).Character;
            foreach (WearSlot slot in Enum.GetValues(typeof(WearSlot)))
            {
                var item=catalog.Find<WearableDefinition>(source.worn.Get(slot));
                if (item && item.Slot==slot && item.Fits(character) && owned.Contains(item.Id)) result.worn.Set(slot,item.Id);
            }
            if (source.skins == null) return result;
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

    public sealed class LocalProfileStore
    {
        private readonly string _path;
        public LocalProfileStore(string path) => _path = Path.GetFullPath(path);

        public WardrobeProfile Load(ContentCatalog catalog)
        {
            foreach (var path in new[] { _path, _path + ".bak" })
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var result = JsonUtility.FromJson<WardrobeProfile>(File.ReadAllText(path));
                    if (result == null) continue;
                    if (result.version > 2) throw new NotSupportedException("衣柜档案版本高于当前游戏版本。");
                    result.version = 2;
                    result.Repair(catalog);
                    return result;
                }
                catch (ArgumentException) { /* Try the previous valid write. */ }
            }
            return WardrobeProfile.Create(catalog);
        }

        public void Save(WardrobeProfile profile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonUtility.ToJson(profile, true));
                if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak");
                else File.Move(temp, _path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
