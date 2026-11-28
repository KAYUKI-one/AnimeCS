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
            bool metal = role == CharacterSurfaceRole.Metal;
            bool eye = material.name is "StarTraveler_T02" or "Firefly_Eye";
            bool lash = material.name is "StarTraveler_Lash" or "Firefly_Lash" or "StarTraveler_T00";
            bool firefly = material.name.StartsWith("Firefly_", StringComparison.Ordinal);
            bool cutout = mode == CharacterAlphaMode.Cutout;
            var faceMap = face || eye || lash ? FaceLightMap(material, firefly ? "Firefly" : "StarTraveler") : null;
            bool transparent = mode == CharacterAlphaMode.Transparent;
            var shader = Shader.Find("DanbaidongRP/PBRToon/" +
                (transparent ? "Transparent" : eye ? "Eye" : faceMap ? "Face" : hair ? "Hair" : "Base"));
            if (!shader) throw new InvalidOperationException("DanbaidongRP must be imported before styling the new characters.");
            var albedo = material.mainTexture;
            var tint = material.color;
            material.shader = shader;
            material.mainTexture = albedo;
            material.color = firefly && face ? new Color(1f, .94f, .91f, tint.a) : tint;
            material.renderQueue = -1;
            material.SetFloat("_Cull", (int)CullMode.Off);
            material.SetFloat("_Metallic", metal ? .47f : 0f);
            material.SetFloat("_Smoothness", eye ? .52f : metal ? .62f : hair ? .49f : face ? .19f : .29f);
            material.SetFloat("_ShadowOffset", faceMap ? .54f : .44f);
            material.SetFloat("_ShadowSmoothNdotL", faceMap ? .13f : .105f);
            var shade = face || lash ? new Color(.83f, .65f, .61f, 1f) : eye ? new Color(.78f, .75f, .84f, 1f) :
                hair ? new Color(.68f, .66f, .74f, 1f) : metal ? new Color(.57f, .61f, .69f, 1f) : new Color(.73f, .75f, .82f, 1f);
            var surface = eye ? "Eye" : face || lash ? "Skin" : hair ? "Hair" : metal ? "Metal" : "Cloth";
            material.SetColor("_ShadowColor", shade);
            if (!transparent)
            {
                material.SetTexture("_ShadowRampTex", CharacterLookMaps.Ramp(surface, shade, metal ? .10f : hair ? .045f : .016f));
                material.SetFloat("_FoldoutShadowRamp", 2f);
                material.SetFloat("_SHADOW_RAMP", 1f);
                material.EnableKeyword("_SHADOW_RAMP");
            }
            if (material.HasProperty("_PBRMask"))
                material.SetTexture("_PBRMask", CharacterLookMaps.MaterialMask(material, hair, metal));
            material.SetFloat("_MainLightColorLerp", face || eye ? .24f : .15f);
            material.SetFloat("_EnvColorLerp", .25f);
            material.SetFloat("_IndirDiffIntensity", eye ? .59f : .78f);
            material.SetFloat("_IndirSpecIntensity", hair || metal ? .70f : .38f);
            if (material.HasProperty("_DirectRimWidth"))
            {
                material.SetFloat("_DirectRimWidth", hair ? .87f : .53f);
                material.SetFloat("_PunctualRimWidth", hair ? .88f : .50f);
                material.SetColor("_DirectRimFrontCol", new Color(.40f, .43f, .49f, .65f));
            }
            material.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            material.SetFloat("_Cutoff", .24f);
            if (cutout) material.EnableKeyword("_ALPHATEST_ON");
            else material.DisableKeyword("_ALPHATEST_ON");
            bool eyeLayer = eye || lash || material.name == "StarTraveler_T01";
            bool outline = !face && !eyeLayer && (!cutout || hair) && !transparent;
            material.SetShaderPassEnabled("CharacterOutline", outline);
            if (material.HasProperty("_FoldoutOutline"))
            {
                // Danbaidong's drawer stores the pass toggle in bit 1.
                material.SetFloat("_FoldoutOutline", outline ? 2f : 0f);
            }
            if (outline)
            {
                material.SetFloat("_OutLineNormalSource", 2f);
                material.DisableKeyword("SN_VertNormal");
                material.DisableKeyword("SN_VertColor");
                material.EnableKeyword("SN_SmoothUV4");
                material.SetFloat("_OutlineWidth", hair ? .40f : .36f);
                material.SetFloat("_OutlineStencilComp", (int)CompareFunction.NotEqual);
                material.SetColor("_OutlineColor", hair ? new Color(.28f, .22f, .25f, .78f) :
                    new Color(.19f, .16f, .18f, .78f));
            }
            if (faceMap)
            {
                material.SetTexture("_FaceLightMap", faceMap);
                material.SetFloat("_FaceSdfUV3", firefly ? 1f : 0f);
                material.SetFloat("_ShadowStrength", eye ? .40f : .71f);
                material.SetFloat("_ShadowSmoothScene", .038f);
                material.SetFloat("_SceneShadowWeight", eye ? .21f : .33f);
                material.SetFloat("_FringeShadowWeight", eye ? .19f : .48f);
                if (material.HasProperty("_FaceFormWeight"))
                    material.SetFloat("_FaceFormWeight", lash ? 0f : .29f);
                material.SetShaderPassEnabled("CharacterTransparent", lash);
                material.SetFloat("_FoldoutEyelash", lash ? 2f : 0f);
                if (lash)
                {
                    material.SetTexture("_BaseMapOutter", albedo);
                    material.SetColor("_BaseColorOutter", tint);
                    material.SetFloat("_AlphaOutter", .46f);
                }
            }
            if (eye) material.SetFloat("_ParallaxScale", firefly ? 0f : .006f);
            ConfigureToonFlags(material, hair ? 2 : eye || lash ? 1 : face ? 3 : -1);
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

        private static void ConfigureToonFlags(Material material, int value)
        {
            if (!material.HasProperty("_ToonFlagsKeywords")) return;
            string[] flags = { "FLAG_HAIRSHADOW", "FLAG_EYELASH", "FLAG_HAIRMASK", "FLAG_FACE" };
            material.SetFloat("_ToonFlagsKeywords", value);
            for (int i = 0; i < flags.Length; i++)
                if (i == value) material.EnableKeyword(flags[i]);
                else material.DisableKeyword(flags[i]);
        }

        private static Texture2D FaceLightMap(Material material, string actor)
        {
            string path = Path.GetDirectoryName(AssetDatabase.GetAssetPath(material)).Replace('\\', '/') + $"/{actor}FaceSDF.png";
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
