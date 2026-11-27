using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimeCS.Content;
using AnimeCS.Match;
using AnimeCS.Networking;
using AnimeCS.Play;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimeCS.Editor
{
    public static class BattleServerBuild
    {
        // Called in a separate batch editor so the player's active target and open scene are preserved.
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64);
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64);

        private static void Build(BuildTarget target)
        {
            if (!Application.isBatchMode) throw new BuildFailedException("Run the dedicated build through Servers/build.ps1.");
            string output = Environment.GetEnvironmentVariable("ANIMECS_BATTLE_OUTPUT")
                ?? Path.GetFullPath(target == BuildTarget.StandaloneLinux64 ? "Builds/Debian13/Battle/AnimeCS.Battle.x86_64" : "Builds/Windows/Services/Battle/AnimeCS.Battle.exe");
            var source = ContentLibrary.Catalog;
            if (!source)
                throw new BuildFailedException("Cannot load ContentCatalog at " + ContentLibrary.CatalogPath + " (imported type: " +
                    AssetDatabase.GetMainAssetTypeAtPath(ContentLibrary.CatalogPath) + ").");
            if (!source.Map) throw new BuildFailedException("Missing default map in catalog " + AssetDatabase.GetAssetPath(source));
            var args = Environment.GetCommandLineArgs();
            int mapArg = Array.IndexOf(args, "-battleMap");
            var map = mapArg < 0 ? source.Map : mapArg + 1 < args.Length ? source.Find<MapDefinition>(args[mapArg + 1]) : null;
            if (!map || map.Practice || map.Archived) throw new BuildFailedException("Select an active competitive map ID.");
            string folder = "Assets/BattleBake_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            var boot = EditorSceneManager.OpenScene(FoundationBuild.BootScene, OpenSceneMode.Single);
            var local = boot.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<LocalGame>(true)).Single();
            var config = new SerializedObject(local);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var root = new GameObject("Battle authority");
                var server = root.AddComponent<BattleServer>();
                var catalog = BakeDefinitions(source, map, folder);
                var serialized = new SerializedObject(server);
                serialized.FindProperty("_catalog").objectReferenceValue = catalog;
                serialized.FindProperty("_rules").objectReferenceValue = config.FindProperty("_rules").objectReferenceValue;
                var objective = config.FindProperty("_objectiveModel").objectReferenceValue as GameObject;
                serialized.FindProperty("_objectiveBounds").boundsValue = ArenaWorld.ModelBounds(objective);

                var drops = source.Entries.Where(item => item is WeaponDefinition || item is UtilityDefinition).ToArray();
                var items = serialized.FindProperty("_dropItems");
                var bounds = serialized.FindProperty("_dropBounds");
                items.arraySize = bounds.arraySize = drops.Length;
                for (int i = 0; i < drops.Length; i++)
                {
                    var item = drops[i];
                    items.GetArrayElementAtIndex(i).objectReferenceValue = catalog.Find<ContentDefinition>(item.Id);
                    bounds.GetArrayElementAtIndex(i).boundsValue = ArenaWorld.ModelBounds(item.DropModel);
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                string scenePath = folder + "/Battle.unity";
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new BuildFailedException("Could not create battle scene.");
                BakeCollisions(map, scene);
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new BuildFailedException("Could not save battle scene.");
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { scenePath }, locationPathName = output, target = target,
                    subtarget = (int)StandaloneBuildSubtarget.Server, options = BuildOptions.None
                });
                if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString());
            }
            finally
            {
                if (boot.IsValid() && boot.isLoaded) SceneManager.SetActiveScene(boot);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        // Build an independent graph. Only gameplay definitions can be referenced from this catalog.
        private static ContentCatalog BakeDefinitions(ContentCatalog source, MapDefinition map, string folder)
        {
            var copy = Object.Instantiate(source);
            copy.name = "Battle catalog";
            copy.SelectMap(map);
            AssetDatabase.CreateAsset(copy, folder + "/BattleCatalog.asset");
            var copies = new Dictionary<Object, Object>();
            foreach (var original in source.Entries)
            {
                if (!original || original is MapDefinition other && other != map) continue;
                var definition = Object.Instantiate(original);
                definition.name = original.name;
                AssetDatabase.AddObjectToAsset(definition, copy);
                copies.Add(original, definition);
            }
            void Remap(Object asset)
            {
                var data = new SerializedObject(asset);
                var it = data.GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.propertyPath == "m_Script") continue;
                    var referenced = it.objectReferenceValue;
                    if (asset is MapDefinition && referenced is UnityEngine.AI.NavMeshData) continue;
                    if (referenced) it.objectReferenceValue = copies.TryGetValue(referenced, out var replacement) ? replacement : null;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var definition in copies.Values) Remap(definition);
            Remap(copy);
            var data = new SerializedObject(copy);
            var entries = data.FindProperty("_entries");
            for (int i = entries.arraySize - 1; i >= 0; i--)
                if (!entries.GetArrayElementAtIndex(i).objectReferenceValue) entries.DeleteArrayElementAtIndex(i);
            data.ApplyModifiedPropertiesWithoutUndo();
            return copy;
        }

        private static void BakeCollisions(MapDefinition map, Scene target)
        {
            Scene mapScene = default;
            GameObject instance = null;
            var holder = new GameObject("Map collision");
            SceneManager.MoveGameObjectToScene(holder, target);
            try
            {
                GameObject[] roots;
                if (!string.IsNullOrEmpty(map.PlayScenePath))
                {
                    mapScene = EditorSceneManager.OpenScene(map.PlayScenePath, OpenSceneMode.Additive);
                    roots = mapScene.GetRootGameObjects();
                }
                else
                {
                    instance = Object.Instantiate(map.Geometry);
                    roots = new[] { instance };
                }
                // Preserve the transform hierarchy, including non-uniform scale on nested parents.
                var transforms = new Dictionary<Transform, Transform>();
                Transform CopyNode(Transform original)
                {
                    if (transforms.TryGetValue(original, out var found)) return found;
                    var node = new GameObject(original.name);
                    node.layer = original.gameObject.layer;
                    node.transform.SetParent(original.parent ? CopyNode(original.parent) : holder.transform, false);
                    node.transform.SetLocalPositionAndRotation(original.localPosition, original.localRotation);
                    node.transform.localScale = original.localScale;
                    transforms.Add(original, node.transform);
                    return node.transform;
                }
                var colliders = roots.SelectMany(go => go.GetComponentsInChildren<Collider>()).Where(c => c.enabled).ToArray();
                if (colliders.Length == 0)
                {
                    foreach (var mesh in roots.SelectMany(go => go.GetComponentsInChildren<MeshFilter>()))
                        if (mesh.sharedMesh) CopyNode(mesh.transform).gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
                    if (transforms.Count == 0) throw new BuildFailedException("Map has no collision geometry.");
                }
                foreach (var collider in colliders)
                {
                    var destination = CopyNode(collider.transform).gameObject;
                    EditorUtility.CopySerialized(collider, destination.AddComponent(collider.GetType()));
                }
            }
            finally
            {
                if (mapScene.IsValid()) EditorSceneManager.CloseScene(mapScene, true);
                if (instance) Object.DestroyImmediate(instance);
                SceneManager.SetActiveScene(target);
            }
        }
    }
}
