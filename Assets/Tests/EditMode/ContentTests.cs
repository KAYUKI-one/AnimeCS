using System;
using System.IO;
using System.Linq;
using AnimeCS.Content;
using AnimeCS.Editor;
using AnimeCS.Match;
using AnimeCS.Play;
using AnimeCS.Profile;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AnimeCS.Tests
{
    public sealed class ContentTests
    {
        [Test] public void CatalogDefaultsAndDesignRosterAreUsable()
        {
            var catalog = ContentLibrary.Catalog;
            Assert.That(FoundationBuild.ValidateCatalog(catalog), Is.Empty);
            Assert.That(catalog.Entries.OfType<WeaponDefinition>().Count(), Is.GreaterThanOrEqualTo(12));
            Assert.That(catalog.Entries.OfType<UtilityDefinition>().Select(x => x.Effect).Distinct().Count(), Is.EqualTo(3));
            Assert.That(catalog.SeatOutfits.Where(outfit => outfit.Character.Faction == Side.Attack)
                .Select(outfit => outfit.Character.Id), Is.EquivalentTo(new[] { "character.flor", "character.startraveler", "character.firefly", "character.nora" }));
            Assert.That(catalog.SeatOutfits.Where(outfit => outfit.Character.Faction == Side.Defend)
                .Select(outfit => outfit.Character.Id), Is.EquivalentTo(new[] { "character.mornye", "character.rover", "character.lily", "character.michele" }));
            Assert.That(catalog.Entries.Any(entry => entry && !entry.Archived &&
                (entry.Id == "character.endmin" || entry.Id == "outfit.endmin")), Is.False);
            Assert.That(catalog.OutfitForSide(Side.Attack, "outfit.lily").Character.Faction, Is.EqualTo(Side.Attack));
            Assert.That(catalog.OutfitForSide(Side.Defend, "outfit.flor").Character.Faction, Is.EqualTo(Side.Defend));
            Assert.That(catalog.Map.SpawnCount(Side.Attack), Is.EqualTo(5));
            Assert.That(catalog.Map.SpawnCount(Side.Defend), Is.EqualTo(5));
            Assert.That(EditorBuildSettings.scenes.First(s => s.enabled).path, Is.EqualTo(FoundationBuild.BootScene));
        }

        [Test] public void ImportsAreIdempotentAndRebindingPreservesIdentityAndSourceFiles()
        {
            string path = "Assets/AnimeCS_Test_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(path));
            try
            {
                var local = ScriptableObject.CreateInstance<ContentCatalog>();
                AssetDatabase.CreateAsset(local, path + "/Catalog.asset");
                var source = ContentLibrary.Catalog;
                var map = ContentLibrary.Import(local, ContentKind.Map, source.Map.Geometry);
                Assert.That(ContentLibrary.Import(local, ContentKind.Map, source.Map.Geometry), Is.SameAs(map));
                var character = ContentLibrary.Import(local, ContentKind.Character, source.Character.Model);
                var gun = (WeaponDefinition)ContentLibrary.Import(local, ContentKind.Weapon, source.StartingPistol(Side.Attack).WorldModel);
                var material = source.Entries.OfType<WeaponSkinDefinition>().First().Material;
                var skin = ContentLibrary.Import(local, ContentKind.Skin, material);
                string id = gun.Id, guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(gun));
                ContentLibrary.Bind(gun, source.Find<WeaponDefinition>("weapon.ember47").WorldModel);
                Assert.That(gun.Id, Is.EqualTo(id));
                Assert.That(local.Find<WeaponDefinition>(id), Is.SameAs(gun));
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(gun)), Is.EqualTo(guid));
                Assert.Throws<ArgumentException>(() => ContentLibrary.Bind(gun, material));
                Assert.That(local.Entries.Count, Is.EqualTo(4));
                ContentLibrary.Unregister(local, skin);
                Assert.That(local.Entries.Count, Is.EqualTo(3));
                Assert.That(AssetDatabase.Contains(skin) && AssetDatabase.Contains(material) && AssetDatabase.Contains(character), Is.True);
                ContentLibrary.Import(local, ContentKind.Skin, skin); Assert.That(local.Entries.Count, Is.EqualTo(4));
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }

        [Test] public void WardrobeRoundTripsIndependentPresetsAndRepairsOnlyWrongSkin()
        {
            var catalog = ContentLibrary.Catalog;
            var profile = WardrobeProfile.Create(catalog);
            var skins = catalog.Entries.OfType<WeaponSkinDefinition>().Take(2).ToArray();
            profile.current.outfitId = "outfit.startraveler";
            profile.current.attackOutfitId = "outfit.startraveler";
            profile.current.defendOutfitId = "outfit.lily";
            profile.presets[0].attackOutfitId = "outfit.firefly";
            profile.presets[0].defendOutfitId = "outfit.rover";
            profile.current.skins.Add(new SkinChoice { weaponId = skins[0].Weapon.Id, skinId = skins[0].Id });
            profile.current.skins.Add(new SkinChoice { weaponId = skins[1].Weapon.Id, skinId = skins[0].Id });
            var temp = Path.Combine(Path.GetTempPath(), "AnimeCS-profile-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = new LocalProfileStore(temp); store.Save(profile); store.Save(profile);
                string legacy=File.ReadAllText(temp);
                File.WriteAllText(temp,legacy.Insert(legacy.IndexOf('{')+1,"\"displayMask\":true,"));
                var copy = store.Load(catalog);
                Assert.That(copy.current.outfitId, Is.EqualTo(profile.current.outfitId));
                Assert.That(copy.current.OutfitFor(Side.Attack), Is.EqualTo("outfit.startraveler"));
                Assert.That(copy.current.OutfitFor(Side.Defend), Is.EqualTo("outfit.lily"));
                Assert.That(copy.current.SkinFor(skins[0].Weapon.Id), Is.EqualTo(skins[0].Id));
                Assert.That(copy.current.SkinFor(skins[1].Weapon.Id), Is.Null);
                Assert.That(copy.presets.Length, Is.EqualTo(2)); Assert.That(copy.presets[0], Is.Not.SameAs(copy.presets[1]));
                Assert.That(copy.presets[0].OutfitFor(Side.Attack), Is.EqualTo("outfit.firefly"));
                Assert.That(copy.presets[0].OutfitFor(Side.Defend), Is.EqualTo("outfit.rover"));
                File.WriteAllText(temp, "{"); Assert.That(store.Load(catalog).current.outfitId, Is.EqualTo(profile.current.outfitId));
            }
            finally { if (File.Exists(temp)) File.Delete(temp); if (File.Exists(temp + ".bak")) File.Delete(temp + ".bak"); }
        }

        [Test] public void FactionGearMigratesLegacyWearAndPreservesEachSideOnReload()
        {
            var catalog = ContentLibrary.Catalog;
            var profile = WardrobeProfile.Create(catalog);
            var legacy = JsonUtility.FromJson<WardrobeLoadout>(
                "{\"outfitId\":\"outfit.nora\",\"primaryId\":\"weapon.ember47\",\"worn\":{\"headwear\":\"wear.catband\"}}");
            var resolved = profile.Resolve(legacy, catalog);
            Assert.That(resolved.WornFor(Side.Attack).headwear, Is.EqualTo("wear.catband"));
            Assert.That(resolved.WornFor(Side.Defend).headwear, Is.EqualTo("wear.catband"));
            Assert.That(resolved.attackGear.rifleId, Is.EqualTo("weapon.ember47"));
            Assert.That(resolved.defendGear.rifleId, Is.Not.EqualTo("weapon.ember47"));

            resolved.defendGear.worn.headwear = null;
            resolved.defendGear.rifleId = "weapon.fold44";
            var loaded = profile.Resolve(JsonUtility.FromJson<WardrobeLoadout>(JsonUtility.ToJson(resolved)), catalog);
            Assert.That(loaded.WornFor(Side.Attack).headwear, Is.EqualTo("wear.catband"));
            Assert.That(loaded.WornFor(Side.Defend).headwear, Is.Null.Or.Empty);
            Assert.That(loaded.defendGear.rifleId, Is.EqualTo("weapon.fold44"));
        }

        [Test] public void EquipReplacesOccupiedSlotAndChecksFactionAndOwnership()
        {
            var catalog = ContentLibrary.Catalog;
            var profile = WardrobeProfile.Create(catalog);
            var draft = profile.Resolve(profile.current, catalog);
            var character = catalog.Find<OutfitDefinition>(draft.OutfitFor(Side.Attack)).Character;
            var pistol = catalog.Find<WeaponDefinition>("weapon.heavy50");
            var defenderOnly = catalog.Find<WeaponDefinition>("weapon.verdict57");
            string oldPistol = draft.attackGear.pistolId, defenderPistol = draft.defendGear.pistolId;
            Assert.That(oldPistol, Is.Not.Null.And.Not.EqualTo(pistol.Id));
            Assert.That(profile.TryEquip(draft, defenderOnly, Side.Attack, character, out _), Is.False);
            Assert.That(draft.attackGear.pistolId, Is.EqualTo(oldPistol));
            Assert.That(profile.TryEquip(draft, pistol, Side.Attack, character, out var selected), Is.True);
            Assert.That(selected, Is.SameAs(pistol));
            Assert.That(draft.attackGear.pistolId, Is.EqualTo(pistol.Id));
            Assert.That(draft.defendGear.pistolId, Is.EqualTo(defenderPistol));

            var skin = catalog.Find<WeaponSkinDefinition>("skin.ember47.case");
            Assert.That(profile.TryEquip(draft, skin, Side.Attack, character, out selected), Is.True);
            Assert.That(draft.SkinFor(selected.Id), Is.EqualTo(skin.Id));
            profile.owned.Remove(skin.Id);
            Assert.That(profile.TryEquip(draft, skin, Side.Attack, character, out _), Is.False);
            Assert.That(profile.TryEquip(draft, skin.Weapon, Side.Attack, character, out selected), Is.True);
            Assert.That(draft.SkinFor(selected.Id), Is.Null);
        }

        [Test] public void LegacyProfileReplacesRetiredOutfitsAndPreservesWeaponsAndSkins()
        {
            var catalog = ContentLibrary.Catalog;
            var skin = catalog.Entries.OfType<WeaponSkinDefinition>().First(item => !item.Archived);
            const string legacy = @"{
                ""version"": 1,
                ""owned"": [""character.endmin"", ""outfit.endmin"", ""outfit.lily"", ""SKIN""],
                ""current"": {
                    ""outfitId"": ""outfit.endmin"",
                    ""primaryId"": ""weapon.ember47"",
                    ""skins"": [{""weaponId"": ""SKIN_WEAPON"", ""skinId"": ""SKIN""}]
                },
                ""presets"": [{""outfitId"": ""outfit.lily""}, {""outfitId"": ""outfit.endmin""}]
            }";
            var temp = Path.Combine(Path.GetTempPath(), "AnimeCS-legacy-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(temp, legacy.Replace("SKIN_WEAPON", skin.Weapon.Id).Replace("SKIN", skin.Id));
                var store = new LocalProfileStore(temp);
                var profile = store.Load(catalog);
                Assert.That(profile.version, Is.EqualTo(2));
                Assert.That(profile.current.primaryId, Is.EqualTo("weapon.ember47"));
                Assert.That(profile.current.SkinFor(skin.Weapon.Id), Is.EqualTo(skin.Id));
                Assert.That(profile.presets.Length, Is.EqualTo(2));
                Assert.That(profile.presets[0], Is.Not.SameAs(profile.presets[1]));
                Assert.That(profile.presets[0].OutfitFor(Side.Defend), Is.EqualTo("outfit.lily"));
                foreach (var loadout in profile.presets.Prepend(profile.current))
                {
                    Assert.That(loadout.outfitId, Is.Not.EqualTo("outfit.endmin"));
                    var outfit = catalog.Find<OutfitDefinition>(loadout.outfitId);
                    Assert.That(outfit, Is.Not.Null, loadout.outfitId);
                    Assert.That(outfit.Archived, Is.False);
                    Assert.That(outfit.Character, Is.Not.Null);
                    Assert.That(outfit.Character.Archived, Is.False);
                    foreach (Side side in Enum.GetValues(typeof(Side)))
                    {
                        var factionOutfit = catalog.Find<OutfitDefinition>(loadout.OutfitFor(side));
                        Assert.That(factionOutfit, Is.Not.Null, side.ToString());
                        Assert.That(factionOutfit.Character.Faction, Is.EqualTo(side));
                        Assert.That(factionOutfit.Character.Id, Is.Not.EqualTo("character.endmin"));
                    }
                }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        [Test] public void ExternalTextureUsesUnityImporterAndRepeatKeepsItsGuid()
        {
            string name = "AnimeCS-import-" + Guid.NewGuid().ToString("N");
            string source = Path.Combine(Path.GetTempPath(), name + ".png");
            var texture = new Texture2D(1, 1); string imported = null;
            const string importRoot = "Assets/Content/Imported";
            const string skinRoot = importRoot + "/Skins";
            bool hadRoot = AssetDatabase.IsValidFolder(importRoot), hadSkins = AssetDatabase.IsValidFolder(skinRoot);
            try
            {
                byte[] unique = Guid.NewGuid().ToByteArray();
                texture.SetPixel(0, 0, new Color32(unique[0], unique[1], unique[2], unique[3])); texture.Apply();
                File.WriteAllBytes(source, texture.EncodeToPNG());
                var asset = ContentLibrary.ImportFile(ContentKind.Skin, source);
                imported = AssetDatabase.GetAssetPath(asset);
                Assert.That(asset, Is.InstanceOf<Texture2D>()); Assert.That(imported, Does.StartWith("Assets/Content/Imported/Skins/"));
                string guid = AssetDatabase.AssetPathToGUID(imported);
                var second = ContentLibrary.ImportFile(ContentKind.Skin, source);
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(second)), Is.EqualTo(guid));
                Assert.Throws<ArgumentException>(() => ContentLibrary.ImportFile(ContentKind.Character, source));
                Assert.That(File.Exists(source), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
                if (imported != null && Path.GetFileNameWithoutExtension(imported) == name)
                {
                    AssetDatabase.DeleteAsset(imported);
                    string folder = Path.GetDirectoryName(imported).Replace('\\', '/');
                    if (!Directory.EnumerateFileSystemEntries(folder).Any()) AssetDatabase.DeleteAsset(folder);
                }
                if (!hadSkins && AssetDatabase.IsValidFolder(skinRoot) && !Directory.EnumerateFileSystemEntries(skinRoot).Any())
                    AssetDatabase.DeleteAsset(skinRoot);
                if (!hadRoot && AssetDatabase.IsValidFolder(importRoot) && !Directory.EnumerateFileSystemEntries(importRoot).Any())
                    AssetDatabase.DeleteAsset(importRoot);
                if (File.Exists(source)) File.Delete(source);
            }
        }

        [Test] public void CompatibleClothingUsesTheCharacterBones()
        {
            var body = new GameObject("Body"); var clothes = new GameObject("Jacket");
            try
            {
                var arm = new GameObject("Arm").transform; arm.SetParent(body.transform);
                var sleeve = new GameObject("Arm").transform; sleeve.SetParent(clothes.transform);
                var skin = clothes.AddComponent<SkinnedMeshRenderer>(); skin.bones = new[] { sleeve }; skin.rootBone = sleeve;
                var rig = new CharacterRig(body, null);
                Assert.That(rig.BindClothing(clothes), Is.True); Assert.That(skin.bones[0], Is.EqualTo(arm));
                Assert.That(skin.rootBone, Is.EqualTo(arm));
            }
            finally { UnityEngine.Object.DestroyImmediate(body); UnityEngine.Object.DestroyImmediate(clothes); }
        }

        [Test] public void BotRequiresFreshContinuousSightBeforeEachFirstShot()
        {
            var bot = new AnimeCS.Play.ObjectiveBot(ContentLibrary.Catalog, 172);
            var view = new SeatView { Seat = 3, Health = 100, Magazine = 20, Side = Side.Attack,
                Match = new PublicMatchView { Round = 1, Phase = RoundPhase.Contest } };
            for (int pass = 0; pass < 2; pass++)
            {
                view.VisibleEnemy = new Contact(7, new Vector3(0, 1.62f, 12));
                int start = view.Match.Tick; bool fired = false;
                for (int i = 0; i < 26; i++)
                {
                    var command = bot.Decide(view);
                    Assert.That(Mathf.Abs(Mathf.DeltaAngle(command.Yaw, view.Yaw)), Is.LessThanOrEqualTo(4.81f));
                    if (command.Fire) { Assert.That(view.Match.Tick - start, Is.GreaterThanOrEqualTo(12)); fired = true; break; }
                    view.Match.Tick++; view.Yaw = command.Yaw; view.Pitch = command.Pitch;
                }
                Assert.That(fired, Is.True);
                view.VisibleEnemy = null; view.Match.Tick++; Assert.That(bot.Decide(view).Fire, Is.False); view.Match.Tick++;
            }
        }
    }
}
