using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AnimeCS.Content;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimeCS.Editor
{
    public static class ContentLibrary
    {
        public const string Root = "Assets/AnimeCS/Content";
        public const string CatalogPath = Root + "/ContentCatalog.asset";
        internal static readonly string[] Labels = { "地图", "角色", "服装", "枪械", "枪皮", "道具", "服饰配件" };
        private static readonly Type[] Types = { typeof(MapDefinition), typeof(CharacterDefinition),
            typeof(OutfitDefinition), typeof(WeaponDefinition), typeof(WeaponSkinDefinition), typeof(UtilityDefinition), typeof(WearableDefinition) };
        private static readonly string[] Folders = { "Maps", "Characters", "Outfits", "Weapons", "Skins", "Utilities", "Wearables" };
        public static ContentCatalog Catalog
        {
            get
            {
                var asset = AssetDatabase.LoadAssetAtPath<ContentCatalog>(CatalogPath);
                if (asset != null) return asset;
                string guid = AssetDatabase.FindAssets("t:ContentCatalog").FirstOrDefault();
                return guid == null ? null : AssetDatabase.LoadAssetAtPath<ContentCatalog>(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        internal static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int split = folder.LastIndexOf('/');
            if (split < 0) throw new ArgumentException("资源文件夹必须位于 Assets 下。");
            string parent = folder.Substring(0, split);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(split + 1));
        }

        public static ContentDefinition Create(ContentCatalog catalog, ContentKind kind, string label, Object source = null, Side? faction=null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (source != null) SourceField(kind, source);
            string clean = string.Concat((label ?? "").Trim().Where(c => !Path.GetInvalidFileNameChars().Contains(c))).TrimEnd('.', ' ');
            if (string.IsNullOrEmpty(clean)) clean = Labels[(int)kind];
            string catalogFolder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(catalog));
            if (string.IsNullOrEmpty(catalogFolder)) throw new InvalidOperationException("请先保存资源目录。");
            string folder = catalogFolder.Replace('\\', '/') + "/" + Folders[(int)kind];
            EnsureFolder(folder);
            var entry = (ContentDefinition)ScriptableObject.CreateInstance(Types[(int)kind]);
            var serialized = new SerializedObject(entry);
            serialized.FindProperty("_id").stringValue = kind.ToString().ToLowerInvariant() + "." + Guid.NewGuid().ToString("N");
            serialized.FindProperty("_displayName").stringValue = clean;
            if(entry is CharacterDefinition && faction.HasValue)serialized.FindProperty("_faction").enumValueIndex=(int)faction.Value;
            if (entry is OutfitDefinition) serialized.FindProperty("_character").objectReferenceValue = catalog.Character;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(entry, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + clean + ".asset"));
            if (source != null) Bind(entry, source);
            Register(catalog, entry);
            return entry;
        }

        public static void Register(ContentCatalog catalog, ContentDefinition entry)
        {
            if (entry == null || !AssetDatabase.Contains(entry) || string.IsNullOrWhiteSpace(entry.Id))
                throw new ArgumentException("请选择含稳定 ID 的项目资源条目。");
            foreach (var existing in catalog.Entries)
            {
                if (existing == entry) return;
                if (existing != null && existing.Id == entry.Id)
                    throw new InvalidOperationException("稳定 ID 重复。请使用「新建条目」创建独立配置。");
            }
            var serialized = new SerializedObject(catalog);
            var list = serialized.FindProperty("_entries");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = entry;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(entry);
        }

        public static void Unregister(ContentCatalog catalog, ContentDefinition entry)
        {
            if (entry == catalog.Knife || entry == catalog.Map || entry == catalog.Character || entry == catalog.Outfit ||
                entry == catalog.StartingPistol(Side.Attack) || entry == catalog.StartingPistol(Side.Defend))
                throw new InvalidOperationException("当前条目被用作项目默认值。请先在「目录设置」中更换。");
            foreach (var item in catalog.Entries)
                if (item is OutfitDefinition outfit && outfit.Character == entry ||
                    item is WeaponSkinDefinition skin && skin.Weapon == entry)
                    throw new InvalidOperationException("请先更改关联条目：" + item.DisplayName);
            var serialized = new SerializedObject(catalog);
            var list = serialized.FindProperty("_entries");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == entry)
                {
                    list.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    list.DeleteArrayElementAtIndex(i);
                    break;
                }
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(catalog);
        }

        internal static string SourceField(ContentKind kind, Object source)
        {
            if (source == null || !EditorUtility.IsPersistent(source)) throw new ArgumentException("请使用 Project 中的资源或磁盘文件。");
            if (kind == ContentKind.Map && source is SceneAsset)
            {
                if (AssetDatabase.GetAssetPath(source) == FoundationBuild.BootScene)
                    throw new ArgumentException("启动场景不能同时用作比赛地图。");
                return "_scene";
            }
            if ((kind == ContentKind.Skin || kind == ContentKind.Wearable) && source is Material) return "_material";
            if (kind == ContentKind.Skin && source is Texture2D) return "_texture";
            if (source is GameObject && kind != ContentKind.Skin)
                return kind == ContentKind.Map ? "_geometry" : kind == ContentKind.Weapon ? "_worldModel" : "_model";
            throw new ArgumentException(Labels[(int)kind] + (kind == ContentKind.Skin ? "需要材质或 Texture2D。" :
                kind == ContentKind.Map ? "需要场景、模型或预制体。" : "需要模型或预制体。"));
        }

        public static void Bind(ContentDefinition entry, Object source)
        {
            string field = SourceField(entry.Kind, source);
            var data = new SerializedObject(entry);
            var reference = data.FindProperty(field);
            if (entry is WeaponDefinition && field=="_worldModel" && reference.objectReferenceValue!=source)
            {
                if(data.FindProperty("_viewModel").objectReferenceValue==reference.objectReferenceValue)
                    data.FindProperty("_viewModel").objectReferenceValue=null;
                data.FindProperty("_handlingModel").objectReferenceValue=null;
                data.FindProperty("_muzzle").vector3Value=MuzzleMarker((GameObject)source);
                if (!data.FindProperty("_viewModel").objectReferenceValue) data.FindProperty("_viewMuzzle").vector3Value=Vector3.zero;
            }
            if (entry is CharacterDefinition && reference.objectReferenceValue!=source)
            {
                data.FindProperty("_faceAnchor").vector3Value=Vector3.zero;
                data.FindProperty("_standingEyes").floatValue=0;
            }
            reference.objectReferenceValue = source;
            if (entry is MapDefinition)
            {
                bool sceneSource = field == "_scene";
                data.FindProperty(sceneSource ? "_geometry" : "_scene").objectReferenceValue = null;
                data.FindProperty("_lightingScenePath").stringValue = "";
                data.FindProperty("_scenePath").stringValue = sceneSource ? AssetDatabase.GetAssetPath(source) : "";
            }
            data.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(entry);
            if (source is SceneAsset && entry is MapDefinition map && Catalog != null && Catalog.Map == map)
                FoundationBuild.IncludeScene(map.ScenePath);
        }

        // Model instances preserve their authored root TRS below the in-game Weapon mount.
        internal static Vector3 MuzzleMarker(GameObject model)
        {
            if (!model) return Vector3.zero;
            var marker=model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>
                t.name.Equals("Muzzle",StringComparison.OrdinalIgnoreCase) || t.name=="枪口");
            if (!marker) return Vector3.zero;
            var root=model.transform;
            var toMount=Matrix4x4.TRS(root.localPosition,root.localRotation,root.localScale)*root.worldToLocalMatrix;
            return toMount.MultiplyPoint3x4(marker.position);
        }

        public static ContentDefinition Import(ContentCatalog catalog, ContentKind kind, Object source, Side? faction=null)
        {
            if (source is ContentDefinition definition)
            {
                if (definition.Kind != kind) throw new ArgumentException("条目分类不匹配。");
                Register(catalog, definition);
                return definition;
            }
            string field = SourceField(kind, source);
            foreach (var entry in catalog.Entries)
                if (entry != null && entry.Kind == kind && new SerializedObject(entry).FindProperty(field).objectReferenceValue == source)
                    return entry;
            return Create(catalog, kind, source.name, source, faction);
        }

        [Serializable] private class GltfDependency { public string uri; }
        [Serializable] private class GltfDocument { public GltfDependency[] buffers,images; }
        private static List<string> ExternalFiles(string path)
        {
            var doc=JsonUtility.FromJson<GltfDocument>(File.ReadAllText(path));
            var result=new SortedSet<string>(StringComparer.Ordinal);
            string root=Path.GetFullPath(Path.GetDirectoryName(path))+Path.DirectorySeparatorChar;
            foreach(var dep in (doc.buffers??Array.Empty<GltfDependency>()).Concat(doc.images??Array.Empty<GltfDependency>()))
            {
                if(string.IsNullOrEmpty(dep.uri)||dep.uri.StartsWith("data:",StringComparison.OrdinalIgnoreCase))continue;
                if(Uri.TryCreate(dep.uri,UriKind.Absolute,out _))throw new ArgumentException("请将 glTF 引用的贴图和缓冲区放在模型目录内，或导出为 GLB。");
                string decoded=Uri.UnescapeDataString(dep.uri);
                string full=Path.GetFullPath(Path.Combine(root,decoded));
                if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase) || !File.Exists(full))throw new FileNotFoundException("glTF 缺少本地依赖",decoded);
                result.Add(full.Substring(root.Length).Replace('\\','/'));
            }
            return result.ToList();
        }

        public static Object ImportFile(ContentKind kind, string path, Side? faction=null,string englishName=null)
        {
            string full = Path.GetFullPath(path);
            if (!File.Exists(full)) throw new FileNotFoundException("源文件不存在。", full);
            string localPath = FileUtil.GetProjectRelativePath(full.Replace('\\', '/'));
            if (!string.IsNullOrEmpty(localPath) && (localPath.StartsWith("Assets/") || localPath.StartsWith("Packages/")))
            {
                var existing = AssetDatabase.LoadMainAssetAtPath(localPath);
                if (!(existing is ContentDefinition)) SourceField(kind, existing);
                return existing;
            }
            string ext = Path.GetExtension(full).ToLowerInvariant();
            var supported = kind == ContentKind.Skin ? new[] { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff" } :
                new[] { ".fbx", ".obj", ".dae", ".glb", ".gltf" };
            if (!supported.Contains(ext)) throw new ArgumentException("支持 " + string.Join(" / ", supported) +
                "。Unity 原生场景、材质和预制体请从 Project 拖入，以保留依赖。");
            var files = ext==".gltf" ? ExternalFiles(full) : new List<string>();
            string hash;
            using (var input = File.OpenRead(full))
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").Substring(0, 16);
            if(files.Count>0)
            {
                using var checksum=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                checksum.AppendData(System.Text.Encoding.UTF8.GetBytes(hash));
                foreach(var file in files)
                {
                    checksum.AppendData(System.Text.Encoding.UTF8.GetBytes(file));using var input=File.OpenRead(Path.Combine(Path.GetDirectoryName(full),file));
                    using var sha=SHA256.Create();checksum.AppendData(sha.ComputeHash(input));
                }
                hash=BitConverter.ToString(checksum.GetHashAndReset()).Replace("-","").Substring(0,16);
            }
            string folder = "Assets/AnimeCS/Imported/" + Folders[(int)kind] + "/" + hash;
            if(kind==ContentKind.Character && faction.HasValue)
            {
                string key=string.IsNullOrWhiteSpace(englishName)?Path.GetFileNameWithoutExtension(full):englishName.Trim();
                if(!System.Text.RegularExpressions.Regex.IsMatch(key,@"^[A-Za-z][A-Za-z0-9]*$"))
                    throw new ArgumentException("请填写角色英文名，例如 BlackMiku。");
                folder="Assets/AnimeCS/Art/Characters/"+(faction==Side.Attack?"Ember_":"Aegis_")+key+"/"+hash;
            }
            EnsureFolder(folder);
            foreach(var file in files)
            {
                string dep=folder+"/"+file;EnsureFolder(Path.GetDirectoryName(dep).Replace('\\','/'));
                if(!File.Exists(dep))File.Copy(Path.Combine(Path.GetDirectoryName(full),file),dep);
                AssetDatabase.ImportAsset(dep,ImportAssetOptions.ForceSynchronousImport);
            }
            string destination = folder + "/" + Path.GetFileName(full);
            if (!File.Exists(destination)) File.Copy(full, destination, false);
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadMainAssetAtPath(destination);
            SourceField(kind, asset);
            return asset;
        }
    }
}
