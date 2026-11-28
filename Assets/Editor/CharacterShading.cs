using System;
using System.IO;
using System.Linq;
using AnimeCS.Content;
using DanbaidongRP.CharacterPipeline.Editor;
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
            if (UsesDanbaidong(name))
            {
                if (!material.shader.name.StartsWith("DanbaidongRP/PBRToon/", StringComparison.Ordinal))
                    ApplyAssignedPreset(name, material);
                return;
            }
            if (CharacterToonMaterial.IsPrepared(material)) return;
            ApplyAssignedPreset(name, material);
        }

        internal static void ApplyAssignedPreset(string name, Material material, bool forceRegenerateMaps = false)
        {
            var roles = LoadRoles();
            var mode = roles.transparent.Contains(name) ? CharacterAlphaMode.Transparent :
                roles.cutoutPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)) ?
                CharacterAlphaMode.Cutout : CharacterAlphaMode.Opaque;
            if (UsesDanbaidong(name))
            {
                ApplyDanbaidongPreset(material, roles.Role(name), mode, forceRegenerateMaps);
                return;
            }
            CharacterToonMaterial.ApplyPreset(material, roles.Role(name), mode);
            bool shell = roles.singleSidedShell.Contains(name);
            material.SetFloat("_Cull", (int)(shell ? CullMode.Back : CullMode.Off));
            if (shell)
            {
                var tint = material.color;
                tint.a = .70f;
                material.color = tint;
            }
            if (mode == CharacterAlphaMode.Cutout) material.SetFloat("_Cutoff", .24f);
            EditorUtility.SetDirty(material);
        }

        internal static bool UsesDanbaidong(string materialName) =>
            materialName.StartsWith("Lily_", StringComparison.Ordinal) ||
            materialName.StartsWith("StarTraveler_", StringComparison.Ordinal) ||
            materialName.StartsWith("Firefly_", StringComparison.Ordinal);

        private static void ApplyDanbaidongPreset(
            Material material,
            CharacterSurfaceRole legacyRole,
            CharacterAlphaMode mode,
            bool forceRegenerateMaps)
        {
            string name = material.name;
            bool isLily = name.StartsWith("Lily_", StringComparison.Ordinal);
            bool isFirefly = name.StartsWith("Firefly_", StringComparison.Ordinal);
            string actor = isLily ? "Lily" : isFirefly ? "Firefly" : "StarTraveler";

            var role = ResolveDanbaidongRole(name, legacyRole, mode);

            var options = new DanbaidongApplyOptions
            {
                ActorName = actor,
                LookDevFolder = DanbaidongLookMaps.DefaultLookDevFolder,
                Cutout = mode == CharacterAlphaMode.Cutout,
                Cutoff = 0.24f,
                FaceSdfUsesUV3 = isFirefly,
                UseVertexColorOutline = isLily,
                EnableEyeSeeThrough = isLily,
                ForceRegenerateMaps = forceRegenerateMaps
            };

            if (isFirefly && role == DanbaidongSurfaceRole.Face)
                options.BaseColorOverride = new Color(1f, 0.96f, 0.94f, 1f);

            if (isLily)
            {
                // Silky lavender-silver hair angel-ring highlight (balanced so it gleams without blowing out!)
                options.HairSpecTintOverride = new Color(0.95f, 0.88f, 1.08f, 1f);
                // Sheer lavender stockings Fresnel tint (warm skin peak inside, violet-sheer outside)
                options.StockingsInsideOverride = new Color(1.00f, 0.94f, 0.96f, 1f);
                options.StockingsOutsideOverride = new Color(0.72f, 0.65f, 0.86f, 1f);
                options.OutlineWidthMultiplier = 1.0f;
            }

            DanbaidongMaterialPreset.Apply(material, role, options);
        }

        private static DanbaidongSurfaceRole ResolveDanbaidongRole(
            string name, CharacterSurfaceRole legacyRole, CharacterAlphaMode mode)
        {
            if (mode == CharacterAlphaMode.Transparent) return DanbaidongSurfaceRole.Transparent;
            if (name is "Lily_T00" or "StarTraveler_T02" or "Firefly_Eye") return DanbaidongSurfaceRole.Eye;
            if (name is "Lily_Lash" or "StarTraveler_Lash" or "Firefly_Lash" or "StarTraveler_T00") return DanbaidongSurfaceRole.Lash;
            if (name is "Lily_Skin") return DanbaidongSurfaceRole.Skin;
            if (name is "Lily_Stockings") return DanbaidongSurfaceRole.Stockings;
            if (name is "StarTraveler_T01") return DanbaidongSurfaceRole.Eye;
            if (legacyRole == CharacterSurfaceRole.Face) return DanbaidongSurfaceRole.Face;
            if (legacyRole == CharacterSurfaceRole.Hair) return DanbaidongSurfaceRole.Hair;
            if (legacyRole == CharacterSurfaceRole.Metal) return DanbaidongSurfaceRole.Metal;
            if (legacyRole == CharacterSurfaceRole.Satin) return DanbaidongSurfaceRole.Leather;
            return DanbaidongSurfaceRole.Cloth;
        }

        [MenuItem("AnimeCS/美术/重建 DanbaidongRP 角色材质与贴图")]
        public static void RebuildDanbaidongCast()
        {
            var folders = ContentLibrary.Catalog.Entries.OfType<CharacterDefinition>().Where(c => !c.Archived && c.Model)
                .Select(c => Path.GetDirectoryName(AssetDatabase.GetAssetPath(c.Model)).Replace('\\', '/')).Distinct().ToArray();
            foreach (var material in AssetDatabase.FindAssets("t:Material", folders).Select(AssetDatabase.GUIDToAssetPath)
                         .Where(path => path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                         .Select(AssetDatabase.LoadAssetAtPath<Material>)
                         .Where(m => m && UsesDanbaidong(m.name)))
            {
                ApplyAssignedPreset(material.name, material, forceRegenerateMaps: true);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("AnimeCS/美术/应用角色卡通材质")]
        public static void ApplyToCast()
        {
            var folders = ContentLibrary.Catalog.Entries.OfType<CharacterDefinition>().Where(c => !c.Archived && c.Model)
                .Select(c => Path.GetDirectoryName(AssetDatabase.GetAssetPath(c.Model)).Replace('\\', '/')).Distinct().ToArray();
            foreach (var material in AssetDatabase.FindAssets("t:Material", folders).Select(AssetDatabase.GUIDToAssetPath)
                         .Where(path => path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                         .Select(AssetDatabase.LoadAssetAtPath<Material>))
                Apply(material.name, material);
            ApplyToWearables();
            AssetDatabase.SaveAssets();
        }

        internal static void ApplyToWearables()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Content/Art/Wearables" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat && mat.shader.name != "AnimeCS/Hidden garment region" && !CharacterToonMaterial.IsPrepared(mat))
                    CharacterToonMaterial.Convert(mat, CharacterSurfaceRole.Fabric);
            }
        }

        [MenuItem("AnimeCS/美术/更新角色阴影与边缘光")]
        internal static void RefineCastLighting()
        {
            var roles = LoadRoles();
            var folders = ContentLibrary.Catalog.Entries.OfType<CharacterDefinition>().Where(c => !c.Archived && c.Model)
                .Select(c => Path.GetDirectoryName(AssetDatabase.GetAssetPath(c.Model)).Replace('\\', '/'))
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
