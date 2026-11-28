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
            if (UsesDanbaidong(name))
            {
                if (!material.shader.name.StartsWith("DanbaidongRP/PBRToon/", StringComparison.Ordinal))
                    ApplyAssignedPreset(name, material);
                return;
            }
            if (CharacterToonMaterial.IsPrepared(material)) return;
            ApplyAssignedPreset(name,material);
        }

        internal static void ApplyAssignedPreset(string name, Material material)
        {
            var roles = LoadRoles();
            var mode = roles.transparent.Contains(name) ? CharacterAlphaMode.Transparent :
                roles.cutoutPrefixes.Any(prefix => name.StartsWith(prefix,StringComparison.Ordinal)) ?
                CharacterAlphaMode.Cutout : CharacterAlphaMode.Opaque;
            if (UsesDanbaidong(name))
            {
                ApplyDanbaidongPreset(material, roles.Role(name), mode);
                return;
            }
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

        private static bool UsesDanbaidong(string materialName) =>
            materialName.StartsWith("StarTraveler_", StringComparison.Ordinal) ||
            materialName.StartsWith("Firefly_", StringComparison.Ordinal);

        private static void ApplyDanbaidongPreset(Material material, CharacterSurfaceRole role, CharacterAlphaMode mode)
        {
            bool face = role == CharacterSurfaceRole.Face;
            bool hair = role == CharacterSurfaceRole.Hair;
            bool cutout = mode == CharacterAlphaMode.Cutout;
            var faceMap = face ? FaceLightMap(material) : null;
            bool transparent = mode == CharacterAlphaMode.Transparent;
            var shader = Shader.Find("DanbaidongRP/PBRToon/" +
                (transparent ? "Transparent" : faceMap ? "Face" : hair ? "Hair" : "Base"));
            if (!shader) throw new InvalidOperationException("DanbaidongRP must be imported before styling the new characters.");
            var albedo = material.mainTexture;
            var tint = material.color;
            material.shader = shader;
            material.mainTexture = albedo;
            material.color = tint;
            material.renderQueue = -1;
            material.SetFloat("_Cull", (int)CullMode.Off);
            material.SetFloat("_Metallic", .02f);
            material.SetFloat("_Smoothness", hair ? .42f : face ? .25f : .28f);
            material.SetFloat("_ShadowOffset", face ? .48f : .49f);
            material.SetFloat("_ShadowSmoothNdotL", face ? .27f : .23f);
            material.SetColor("_ShadowColor", face ? new Color(.69f, .53f, .51f, 1) : new Color(.46f, .51f, .60f, 1));
            material.SetFloat("_MainLightColorLerp", .12f);
            material.SetFloat("_EnvColorLerp", .20f);
            material.SetFloat("_IndirDiffIntensity", .71f);
            material.SetFloat("_IndirSpecIntensity", hair ? .82f : .60f);
            material.SetFloat("_DirectRimWidth", hair ? 1.35f : .78f);
            material.SetFloat("_PunctualRimWidth", hair ? 1.25f : .72f);
            material.SetColor("_DirectRimFrontCol", new Color(.55f, .60f, .66f, .65f));
            material.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            material.SetFloat("_Cutoff", .24f);
            if (cutout) material.EnableKeyword("_ALPHATEST_ON");
            else material.DisableKeyword("_ALPHATEST_ON");
            bool eyeLayer = material.name is "StarTraveler_T01" or "StarTraveler_T02";
            bool outline = !face && !eyeLayer && !cutout && !transparent;
            material.SetShaderPassEnabled("CharacterOutline", outline);
            if (material.HasProperty("_FoldoutOutline"))
            {
                // Danbaidong's drawer stores the pass toggle in bit 1.
                material.SetFloat("_FoldoutOutline", outline ? 2f : 0f);
            }
            if (outline)
            {
                material.SetFloat("_OutLineNormalSource", 1f);
                material.EnableKeyword("SN_VertNormal");
                material.SetFloat("_OutlineWidth", hair ? .70f : .53f);
                material.SetColor("_OutlineColor", new Color(.10f, .085f, .11f, .88f));
            }
            if (faceMap)
            {
                material.SetTexture("_FaceLightMap", faceMap);
                material.SetShaderPassEnabled("CharacterTransparent", false);
                material.SetFloat("_ShadowStrength", .61f);
                material.SetFloat("_ShadowSmoothScene", .05f);
                material.SetColor("_ShadowColor", new Color(.75f, .62f, .60f, 1f));
            }
            if (transparent)
            {
                material.SetFloat("_Alpha", .85f);
                material.SetFloat("_BlendSrc", (int)BlendMode.SrcAlpha);
                material.SetFloat("_BlendDst", (int)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_BlendOp", (int)BlendOp.Add);
                material.SetShaderPassEnabled("TransparentBackface", false);
                material.SetShaderPassEnabled("DepthOnly", false);
                material.SetShaderPassEnabled("ShadowCaster", false);
            }
            EditorUtility.SetDirty(material);
        }

        private static Texture2D FaceLightMap(Material material)
        {
            if (material.name != "StarTraveler_T05") return null;
            string path = Path.GetDirectoryName(AssetDatabase.GetAssetPath(material)).Replace('\\', '/') + "/StarTravelerFaceSDF.png";
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return null;
            if (importer.sRGBTexture || importer.wrapMode != TextureWrapMode.Clamp ||
                importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.sRGBTexture = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
