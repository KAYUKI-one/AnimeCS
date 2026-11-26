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
        public const string BootScene = "Assets/Scenes/Gameplay/LocalMatch.unity";
        private const string SavedImportWorkers = "AnimeCS.Build.SavedImportWorkers";
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
            data.FindProperty("_scenePath").stringValue = path;
            var lighting = data.FindProperty("_lightingScenePath");
            var lightingGuid = data.FindProperty("_lightingSceneGuid");
            if (string.IsNullOrEmpty(lightingGuid.stringValue) && !string.IsNullOrEmpty(lighting.stringValue))
                lightingGuid.stringValue = AssetDatabase.AssetPathToGUID(lighting.stringValue);
            string bakedPath = AssetDatabase.GUIDToAssetPath(lightingGuid.stringValue);
            if (!string.IsNullOrEmpty(bakedPath))
            {
                lighting.stringValue = bakedPath;
                var geometry = data.FindProperty("_geometry");
                if (geometry.objectReferenceValue)
                {
                    data.FindProperty("_geometrySourceGuid").stringValue =
                        AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(geometry.objectReferenceValue));
                    geometry.objectReferenceValue = null;
                }
            }
            if (!data.ApplyModifiedPropertiesWithoutUndo()) return;
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
                if (!catalog.OutfitForSide(side)) yield return CharacterDefinition.FactionName(side)+"需要至少一套可用的对局角色。";
                var pistol = catalog.StartingPistol(side);
                if (pistol != null && (pistol.Class != WeaponClass.Pistol || pistol.Price != 0 || !pistol.CanBuy(side)))
                    yield return "免费起始手枪的类型、阵营或价格配置有误。";
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            // Keep idle importer processes out of the packing peak, including refreshes during the build.
            if (SessionState.GetInt(SavedImportWorkers, -1) < 0)
                SessionState.SetInt(SavedImportWorkers, AssetDatabase.DesiredWorkerCount);
            ScheduleWorkerRestore();
            AssetDatabase.DesiredWorkerCount = 1;
            AssetDatabase.ForceToDesiredWorkerCount();
            var catalog = ContentLibrary.Catalog;
            if (catalog == null) return;
            foreach (var entry in catalog.Entries) if (entry is MapDefinition map) Sync(map);
            string[] problems = ValidateCatalog(catalog, false).ToArray();
            if (problems.Length > 0) throw new BuildFailedException(string.Join("\n", problems));
            foreach(var map in catalog.Entries.OfType<MapDefinition>())
                if(!map.Archived && !string.IsNullOrEmpty(map.PlayScenePath) && !EditorBuildSettings.scenes.Any(scene=>scene.enabled && scene.path==map.PlayScenePath))
                    throw new BuildFailedException(map.DisplayName+" 的场景未加入当前构建，请重新绑定或烘焙地图。");
            EditorUtility.UnloadUnusedAssetsImmediate();
        }

        [InitializeOnLoadMethod]
        private static void ScheduleWorkerRestore()
        {
            if (SessionState.GetInt(SavedImportWorkers, -1) < 0) return;
            EditorApplication.update -= RestoreWorkersWhenIdle;
            EditorApplication.update += RestoreWorkersWhenIdle;
        }

        private static void RestoreWorkersWhenIdle()
        {
            // Postprocess callbacks can be skipped if a build fails or is cancelled.
            if (BuildPipeline.isBuildingPlayer) return;
            EditorApplication.update -= RestoreWorkersWhenIdle;
            int previousCount = SessionState.GetInt(SavedImportWorkers, -1);
            if (previousCount < 0) return;
            AssetDatabase.DesiredWorkerCount = previousCount;
            SessionState.EraseInt(SavedImportWorkers);
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
