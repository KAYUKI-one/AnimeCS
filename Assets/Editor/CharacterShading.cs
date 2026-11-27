using System;
using System.IO;
using System.Linq;
using AnimeCS.Content;
using StylizedCharacters.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimeCS.Editor
{
    internal static class CharacterShading
    {
        [Serializable]
        sealed class AtlasRoles
        {
            public string[] face, hair, metal, satin, transparent, cutoutPrefixes, singleSidedShell;
            public CharacterSurfaceRole Role(string name) =>
                face.Contains(name) ? CharacterSurfaceRole.Face : hair.Contains(name) ? CharacterSurfaceRole.Hair :
                metal.Contains(name) ? CharacterSurfaceRole.Metal : satin.Contains(name) ? CharacterSurfaceRole.Satin :
                CharacterToonMaterial.InferRole(name);
        }

        static AtlasRoles LoadRoles() => JsonUtility.FromJson<AtlasRoles>(
            AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Editor/CastSurfaceAssignments.json").text);

        internal static void Apply(string name, Material material)
        {
            if (CharacterToonMaterial.IsPrepared(material)) return;
            ApplyAssignedPreset(name,material);
        }

        internal static void ApplyAssignedPreset(string name, Material material)
        {
            var roles = LoadRoles();
            var mode = roles.transparent.Contains(name) ? CharacterAlphaMode.Transparent :
                roles.cutoutPrefixes.Any(prefix => name.StartsWith(prefix,StringComparison.Ordinal)) ?
                CharacterAlphaMode.Cutout : CharacterAlphaMode.Opaque;
            CharacterToonMaterial.ApplyPreset(material,roles.Role(name),mode);
            bool shell = roles.singleSidedShell.Contains(name);
            material.SetFloat("_Cull",(int)(shell ? CullMode.Back : CullMode.Off));
            if (shell)
            {
                var tint = material.color;
                tint.a = .70f;
                material.color=tint;
            }
            if (mode == CharacterAlphaMode.Cutout) material.SetFloat("_Cutoff",.24f);
            EditorUtility.SetDirty(material);
        }

        [MenuItem("AnimeCS/美术/应用角色卡通材质")]
        public static void ApplyToCast()
        {
            // Body and first-person exports share these atlases. Some auxiliary slots exist in only one export.
            var folders = ContentLibrary.Catalog.Entries.OfType<CharacterDefinition>().Where(c => !c.Archived && c.Model)
                .Select(c => Path.GetDirectoryName(AssetDatabase.GetAssetPath(c.Model)).Replace('\\','/')).Distinct().ToArray();
            foreach (var material in AssetDatabase.FindAssets("t:Material",folders).Select(AssetDatabase.GUIDToAssetPath)
                         .Where(path => path.EndsWith(".mat",StringComparison.OrdinalIgnoreCase)).Select(AssetDatabase.LoadAssetAtPath<Material>))
                Apply(material.name,material);
            ApplyToWearables();
            AssetDatabase.SaveAssets();
        }

        internal static void ApplyToWearables()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Material",new[] {"Assets/Content/Art/Wearables"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".mat",StringComparison.OrdinalIgnoreCase)) continue;
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat && mat.shader.name != "AnimeCS/Hidden garment region" && !CharacterToonMaterial.IsPrepared(mat))
                    CharacterToonMaterial.Convert(mat,CharacterSurfaceRole.Fabric);
            }
        }

        [MenuItem("AnimeCS/美术/更新角色阴影与边缘光")]
        internal static void RefineCastLighting()
        {
            var roles = LoadRoles();
            var folders = ContentLibrary.Catalog.Entries.OfType<CharacterDefinition>().Where(c => !c.Archived && c.Model)
                .Select(c => Path.GetDirectoryName(AssetDatabase.GetAssetPath(c.Model)).Replace('\\','/'))
                .Append("Assets/Content/Art/Wearables").Distinct().ToArray();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", folders))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!CharacterToonMaterial.IsPrepared(material)) continue;
                CharacterToonMaterial.RefineLighting(material, roles.Role(material.name));
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
