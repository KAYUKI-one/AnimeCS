using System;
using System.Collections.Generic;
using System.Linq;
using AnimeCS.Content;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AnimeCS.Editor
{
    public sealed class FoundationBuild : IPreprocessBuildWithReport
    {
        public const string BootScene = "Assets/AnimeCS/Scenes/LocalMatch.unity";
        public int callbackOrder => 0;

        public static void SetMap(ContentCatalog catalog, MapDefinition map)
        {
            if (map == null || !catalog.Entries.Contains(map)) throw new ArgumentException("地图需要先登记到目录。");
            Sync(map);
            var problems = ValidateEntry(map, catalog).ToArray();
            if (problems.Length != 0) throw new InvalidOperationException(string.Join("\n", problems));
            if (!string.IsNullOrEmpty(map.ScenePath)) IncludeScene(map.ScenePath);
            var data = new SerializedObject(catalog);
            data.FindProperty("_map").objectReferenceValue = map;
            data.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(catalog);
        }

        internal static void IncludeScene(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            var found = scenes.Find(s => s.path == path);
            if (found != null && found.enabled) return;
            if (found == null) scenes.Add(new EditorBuildSettingsScene(path, true));
            else found.enabled = true;
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        internal static void Sync(MapDefinition map)
        {
            var data = new SerializedObject(map);
            var scene = data.FindProperty("_scene").objectReferenceValue as SceneAsset;
            string path = scene != null ? AssetDatabase.GetAssetPath(scene) : "";
            if (map.ScenePath == path) return;
            data.FindProperty("_scenePath").stringValue = path;
            data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(map);
        }

        public static IEnumerable<string> ValidateEntry(ContentDefinition entry, ContentCatalog catalog)
        {
            if (string.IsNullOrWhiteSpace(entry.Id)) yield return "缺少稳定 ID。";
            if (entry is MapDefinition map)
            {
                if (map.Geometry == null && string.IsNullOrEmpty(map.ScenePath)) yield return "请绑定地图场景或几何模型。";
                if (map.Geometry != null && !string.IsNullOrEmpty(map.ScenePath)) yield return "请通过「绑定素材」选择唯一的地图来源。";
                if (map.ScenePath == BootScene) yield return "启动场景不能用作地图。";
                if (!string.IsNullOrEmpty(map.ScenePath) && AssetDatabase.LoadAssetAtPath<SceneAsset>(map.ScenePath) == null)
                    yield return "地图场景已丢失，请重新绑定。";
                if (map.SpawnCount(Side.Attack) != 5 || map.SpawnCount(Side.Defend) != 5) yield return "每方需要五个出生位。";
                else
                {
                    var points = new HashSet<Vector3>(); bool overlap = false;
                    foreach (Side side in Enum.GetValues(typeof(Side)))
                        for (int i = 0; i < 5; i++) overlap |= !points.Add(map.Spawn(side, i));
                    if (overlap) yield return "出生位有重叠。";
                }
                if ((map.SitePosition(Site.A) - map.SitePosition(Site.B)).magnitude <= 2 * map.Radius)
                    yield return "A / B 安装区域重叠。";
            }
            if (entry is CharacterDefinition character && character.Model == null) yield return "请绑定基础角色模型。";
            if (entry is OutfitDefinition outfit)
            {
                if (outfit.Character == null || !catalog.Entries.Contains(outfit.Character)) yield return "请选择已登记的适配角色。";
                if (outfit.Model == null && outfit.Arms == null) yield return "请绑定服装或第一人称双臂。";
            }
            if (entry is WeaponDefinition weapon && weapon.WorldModel == null) yield return "请绑定第三人称 / 地面枪模。";
            if (entry is WeaponSkinDefinition skin)
            {
                if (skin.Weapon == null || !catalog.Entries.Contains(skin.Weapon)) yield return "请选择已登记的适配枪械。";
                if (skin.Material == null && skin.Texture == null) yield return "请绑定材质或基础色贴图。";
            }
        }

        public static IEnumerable<string> ValidateCatalog(ContentCatalog catalog, bool includeWorkInProgress = true)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in catalog.Entries)
            {
                if (entry == null) { yield return "目录有丢失的条目引用。"; continue; }
                if (!ids.Add(entry.Id)) yield return entry.DisplayName + " · 稳定 ID 重复";
                if (includeWorkInProgress || entry == catalog.Map || entry == catalog.Character || entry == catalog.Outfit ||
                    entry == catalog.StartingPistol(Side.Attack) || entry == catalog.StartingPistol(Side.Defend))
                    foreach (string problem in ValidateEntry(entry, catalog)) yield return entry.DisplayName + " · " + problem;
            }
            foreach (var entry in new ContentDefinition[] { catalog.Map, catalog.Character, catalog.Outfit,
                catalog.StartingPistol(Side.Attack), catalog.StartingPistol(Side.Defend) })
                if (entry == null || !catalog.Entries.Contains(entry)) yield return "请在目录设置中指定已登记的地图、角色、默认服装及双方手枪。";
            if (catalog.Outfit != null && catalog.Outfit.Character != catalog.Character) yield return "默认服装与默认角色不匹配。";
            foreach (Side side in Enum.GetValues(typeof(Side)))
            {
                var pistol = catalog.StartingPistol(side);
                if (pistol != null && (pistol.Class != WeaponClass.Pistol || pistol.Price != 0 || !pistol.CanBuy(side)))
                    yield return "免费起始手枪的类型、阵营或价格配置有误。";
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            var catalog = ContentLibrary.Catalog;
            if (catalog == null) return;
            foreach (var entry in catalog.Entries) if (entry is MapDefinition map) Sync(map);
            string[] problems = ValidateCatalog(catalog, false).ToArray();
            if (problems.Length > 0) throw new BuildFailedException(string.Join("\n", problems));
            if (catalog.Map != null && !string.IsNullOrEmpty(catalog.Map.ScenePath) &&
                !EditorBuildSettings.scenes.Any(s => s.enabled && s.path == catalog.Map.ScenePath))
                throw new BuildFailedException("启动地图场景未加入当前构建，请在资源管理器重新选择「设为启动地图」。");
        }
    }

    internal sealed class MapScenePostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!moved.Concat(deleted).Any(p => p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))) return;
            EditorApplication.delayCall += () =>
            {
                var catalog = ContentLibrary.Catalog;
                if (catalog == null) return;
                foreach (var entry in catalog.Entries) if (entry is MapDefinition map) FoundationBuild.Sync(map);
            };
        }
    }
}
