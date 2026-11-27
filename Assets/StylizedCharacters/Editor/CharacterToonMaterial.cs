using System;
using lilToon;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace StylizedCharacters.Editor
{
    public enum CharacterSurfaceRole { Fabric, Face, Hair, Metal, Satin }
    public enum CharacterAlphaMode { Opaque, Cutout, Transparent }

    public static class CharacterToonMaterial
    {
        public static bool IsPrepared(Material material) => material && material.HasProperty("_lilToonVersion");

        public static CharacterSurfaceRole InferRole(string name)
        {
            string lower = name.ToLowerInvariant();
            if (lower.Contains("face") || lower.Contains("顔") || lower.Contains("脸")) return CharacterSurfaceRole.Face;
            if (lower.Contains("hair") || lower.Contains("髪") || lower.Contains("头发")) return CharacterSurfaceRole.Hair;
            if (lower.Contains("metal") || lower.Contains("金属")) return CharacterSurfaceRole.Metal;
            if (lower.Contains("satin") || lower.Contains("silk") || lower.Contains("丝绸")) return CharacterSurfaceRole.Satin;
            return CharacterSurfaceRole.Fabric;
        }

        static CharacterAlphaMode ReadSurface(Material source)
        {
            if (IsPrepared(source) && lilShaderUtils.IsTransparentShaderName(source.shader.name)) return CharacterAlphaMode.Transparent;
            string tag = source.GetTag("RenderType",false);
            if (tag == "Transparent" || source.renderQueue >= (int)RenderQueue.Transparent) return CharacterAlphaMode.Transparent;
            if (tag == "TransparentCutout" || source.IsKeywordEnabled("_ALPHATEST_ON") ||
                source.HasFloat("_AlphaClip") && source.GetFloat("_AlphaClip")>.5f) return CharacterAlphaMode.Cutout;
            return CharacterAlphaMode.Opaque;
        }

        public static void Convert(Material material, CharacterSurfaceRole role, CharacterAlphaMode? alpha = null)
        {
            if (IsPrepared(material)) return;
            ApplyPreset(material,role,alpha);
        }

        public static void ApplyPreset(Material material, CharacterSurfaceRole role, CharacterAlphaMode? alpha = null)
        {
            string name = role switch {
                CharacterSurfaceRole.Face => "Skin-Anime", CharacterSurfaceRole.Hair => "Hair-Anime",
                CharacterSurfaceRole.Metal => "Inorganic-Metal", _ => "Cloth-Anime"
            };
            var preset = AssetDatabase.LoadAssetAtPath<lilToonPreset>($"Packages/jp.lilxyzw.liltoon/Presets/{name}.asset");
            if (!preset || !lilShaderManager.lts) throw new InvalidOperationException($"Missing lilToon shader or preset {name}.");
            var surface = alpha ?? ReadSurface(material);
            bool face = role == CharacterSurfaceRole.Face, hair = role == CharacterSurfaceRole.Hair;
            bool transparent = surface == CharacterAlphaMode.Transparent;
            var styled = new Material(lilShaderManager.lts) { name = material.name };
            try
            {
                lilToonPreset.ApplyPreset(styled,preset,false);
                var mode = transparent ? RenderingMode.Transparent :
                    surface == CharacterAlphaMode.Cutout ? RenderingMode.Cutout : RenderingMode.Opaque;
                float cull=material.HasFloat("_Cull") ? material.GetFloat("_Cull") : (float)CullMode.Back;
                // Two-sided hair sheets have an inked silhouette in their albedo. Hulls also trace the internal cut edges.
                bool outline=!transparent && !(hair && cull==(float)CullMode.Off);
                lilToonInspector.SetupMaterialWithRenderingMode(styled,mode,TransparentMode.Normal,outline,false,false,false);
                styled.renderQueue = transparent ? (int)RenderQueue.Transparent : -1;
                styled.SetOverrideTag("RenderType",transparent ? "Transparent" : surface==CharacterAlphaMode.Cutout ? "TransparentCutout" : "Opaque");
                styled.SetFloat("_Cull",cull);
                styled.SetFloat("_ZWrite",transparent ? 0 : 1);
                styled.SetFloat("_Cutoff",transparent ? .001f : material.HasFloat("_Cutoff") ? material.GetFloat("_Cutoff") : .35f);
                styled.SetShaderPassEnabled("ShadowCaster",!transparent);
                styled.mainTexture = material.mainTexture;
                styled.mainTextureScale = material.mainTextureScale;
                styled.mainTextureOffset = material.mainTextureOffset;
                styled.color = material.color;
                styled.doubleSidedGI = material.doubleSidedGI;
                styled.enableInstancing = material.enableInstancing;
                var normal = material.HasTexture("_BumpMap") ? material.GetTexture("_BumpMap") : null;
                styled.SetTexture("_BumpMap",normal);
                styled.SetFloat("_UseBumpMap",normal ? 1 : 0);
                styled.SetFloat("_BumpScale",material.HasFloat("_BumpScale") ? Mathf.Min(.45f,material.GetFloat("_BumpScale")) : .35f);
                styled.SetTexture("_OutlineTex",styled.mainTexture);
                styled.SetTextureScale("_OutlineTex",styled.mainTextureScale);
                styled.SetTextureOffset("_OutlineTex",styled.mainTextureOffset);
                styled.SetFloat("_OutlineWidth",face ? .020f : hair ? .024f : .055f);
                styled.SetFloat("_OutlineZBias",face || hair ? .005f : .002f);
                styled.SetColor("_OutlineColor",face ? new Color(.73f,.53f,.55f) : new Color(.57f,.51f,.62f));
                RefineLighting(styled, role);
                if (role == CharacterSurfaceRole.Metal)
                {
                    // Keep painted jewelry readable in a preview room without a reflection probe.
                    styled.SetFloat("_Metallic",.33f);
                    styled.SetColor("_ReflectionColor",new Color(.85f,.84f,.9f,.42f));
                }
                if (hair || role == CharacterSurfaceRole.Satin)
                {
                    styled.SetFloat("_UseReflection",1);
                    styled.SetFloat("_Smoothness",hair ? .42f : .34f);
                    styled.SetFloat("_ApplyReflection",0);
                    styled.SetFloat("_SpecularToon",0);
                    styled.SetFloat("_SpecularBlur",.30f);
                    styled.SetColor("_ReflectionColor",new Color(.81f,.78f,.85f,hair ? .13f : .075f));
                    styled.SetFloat("_UseAnisotropy",hair ? 1 : 0);
                    styled.SetFloat("_Anisotropy2Reflection",hair ? 1 : 0);
                    styled.SetFloat("_AnisotropyScale",.55f);
                }
                EditorUtility.CopySerialized(styled,material);
                EditorUtility.SetDirty(material);
            }
            finally { UnityEngine.Object.DestroyImmediate(styled); }
        }

        /// <summary>Applies the shared shadow and rim palette without changing surface modes or texture settings.</summary>
        public static void RefineLighting(Material material, CharacterSurfaceRole role)
        {
            if (!IsPrepared(material)) return;
            bool face = role == CharacterSurfaceRole.Face;
            bool hair = role == CharacterSurfaceRole.Hair;
            material.SetFloat("_UseShadow", 1);
            material.SetFloat("_LightMinLimit", face ? .36f : .29f);
            material.SetFloat("_LightMaxLimit", 1.15f);
            material.SetFloat("_MonochromeLighting", .12f);
            material.SetFloat("_ShadowStrength", face ? .63f : .79f);
            material.SetFloat("_ShadowBorder", face ? .35f : .50f);
            material.SetFloat("_ShadowBlur", face ? .31f : hair ? .22f : .19f);
            material.SetFloat("_Shadow2ndBorder", face ? .21f : .30f);
            material.SetFloat("_Shadow2ndBlur", .24f);
            material.SetFloat("_ShadowReceive", face ? .23f : .65f);
            material.SetFloat("_Shadow2ndReceive", .50f);
            material.SetColor("_ShadowColor", face ? new Color(.98f,.83f,.88f) :
                hair ? new Color(.80f,.84f,.98f) : new Color(.84f,.86f,.96f));
            material.SetColor("_Shadow2ndColor", face ? new Color(.78f,.72f,.88f,.43f) : new Color(.73f,.76f,.93f,.44f));
            material.SetFloat("_ShadowBorderRange", face ? .018f : .013f);
            material.SetColor("_ShadowBorderColor", new Color(.95f,.81f,.79f));
            material.SetFloat("_ShadowEnvStrength", .23f);
            material.SetFloat("_UseRim", 1);
            material.SetColor("_RimColor", new Color(.83f,.90f,.98f, face ? .055f : .145f));
            material.SetFloat("_RimBorder", .84f);
            material.SetFloat("_RimBlur", .16f);
            material.SetFloat("_RimMainStrength", .42f);
        }
    }
}
