using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DanbaidongRP.CharacterPipeline.Editor
{
    public struct DanbaidongApplyOptions
    {
        public string ActorName;
        public string LookDevFolder;
        public bool Cutout;
        public bool MultiplyOverlay;
        public bool UseMeshNormals;
        public float Cutoff;
        public bool FaceSdfUsesUV3;
        public bool UseVertexColorOutline;
        public bool EnableEyeSeeThrough;
        public bool ForceRegenerateMaps;
        public Color? BaseColorOverride;
        public Color? HairSpecTintOverride;
        public Color? StockingsInsideOverride;
        public Color? StockingsOutsideOverride;
        public float? OutlineWidthMultiplier;
    }

    /// <summary>
    /// Portable material preset configurator for DanbaidongRP PBRToon shaders (Base, Face, Eye, Hair, Stockings, Transparent).
    /// Calibrated to the official DanbaidongRP specification (SigmoidSharp crisp cel transitions, Lambert 1/PI physical lighting,
    /// sRGB shadow ramps, anisotropic HairSpecTex, PerObjectShadow + FringeShadow + EyelashOutter see-through).
    /// </summary>
    public static class DanbaidongMaterialPreset
    {
        private static readonly string[] ToonFlagKeywords =
        {
            "FLAG_HAIRSHADOW",
            "FLAG_EYELASH",
            "FLAG_HAIRMASK",
            "FLAG_FACE"
        };

        /// <summary>
        /// Infers a DanbaidongSurfaceRole from arbitrary English/Chinese/Japanese anime or MMD material names.
        /// </summary>
        public static DanbaidongSurfaceRole InferRole(string materialName)
        {
            string n = (materialName ?? string.Empty).ToLowerInvariant();
            if (ContainsAny(n, "eyeblend", "eyeglow", "eyehighlight", "veil", "lace", "trans", "glass", "transparent", "alpha", "透明", "蕾丝", "头纱"))
                return DanbaidongSurfaceRole.Transparent;
            if (ContainsAny(n, "eyelash", "lash", "brow", "eyebrow", "睫", "眉"))
                return DanbaidongSurfaceRole.Lash;
            if (ContainsAny(n, "eye", "iris", "pupil", "瞳", "目", "眼"))
                return DanbaidongSurfaceRole.Eye;
            if (ContainsAny(n, "face", "head", "mouth", "cheek", "脸", "顔", "面"))
                return DanbaidongSurfaceRole.Face;
            if (ContainsAny(n, "hair", "bang", "fringe", "braid", "ponytail", "发", "髪"))
                return DanbaidongSurfaceRole.Hair;
            if (ContainsAny(n, "stocking", "tight", "pantyhose", "hose", "sock", "丝袜", "连裤袜", "タイツ"))
                return DanbaidongSurfaceRole.Stockings;
            if (ContainsAny(n, "skin", "body", "arm", "leg", "hand", "肌", "皮肤", "素体"))
                return DanbaidongSurfaceRole.Skin;
            if (ContainsAny(n, "metal", "gold", "silver", "armor", "buckle", "emblem", "brooch", "金属"))
                return DanbaidongSurfaceRole.Metal;
            if (ContainsAny(n, "leather", "boot", "shoe", "belt", "strap", "皮", "靴"))
                return DanbaidongSurfaceRole.Leather;
            return DanbaidongSurfaceRole.Cloth;
        }

        public static void Apply(Material material, DanbaidongSurfaceRole role, DanbaidongApplyOptions options = default)
        {
            if (!material) throw new ArgumentNullException(nameof(material));
            string lookDevFolder = string.IsNullOrEmpty(options.LookDevFolder)
                ? DanbaidongLookMaps.DefaultLookDevFolder
                : options.LookDevFolder;

            string shaderSuffix = role switch
            {
                DanbaidongSurfaceRole.Transparent => "Transparent",
                DanbaidongSurfaceRole.Eye => "Eye",
                DanbaidongSurfaceRole.Face => "Face",
                DanbaidongSurfaceRole.Lash => "Face",
                DanbaidongSurfaceRole.Hair => "Hair",
                DanbaidongSurfaceRole.Stockings => "Stockings",
                _ => "Base"
            };

            string shaderName = "DanbaidongRP/PBRToon/" + shaderSuffix;
            var shader = Shader.Find(shaderName);
            if (!shader)
                throw new InvalidOperationException($"Shader '{shaderName}' not found. Ensure DanbaidongRP is installed.");

            var albedo = DanbaidongLookMaps.GetAlbedoTexture(material);
            var tint = options.BaseColorOverride ?? (material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white);
            var average = DanbaidongLookMaps.SampleAlbedo(albedo, out float lightFraction);
            bool skin = role is DanbaidongSurfaceRole.Face or DanbaidongSurfaceRole.Skin;
            if (average.HasValue && (skin || role is DanbaidongSurfaceRole.Hair or DanbaidongSurfaceRole.Cloth))
            {
                // Cap the authored tint so repeated imports converge to the same colour.
                float protection = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.72f, .84f, average.Value.grayscale));
                protection = Mathf.Max(protection, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.045f, .25f, lightFraction)));
                Color cap = Color.Lerp(Color.white, role == DanbaidongSurfaceRole.Face ? new Color(.965f, .98f, .97f) : skin ? new Color(.94f, .90f, .88f) : new Color(.91f, .91f, .92f), protection);
                tint = new Color(Mathf.Min(tint.r, cap.r), Mathf.Min(tint.g, cap.g), Mathf.Min(tint.b, cap.b), tint.a);
            }

            material.shader = shader;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", albedo);
            else if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", albedo);
            material.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, 1f));
            material.renderQueue = -1;
            material.SetFloat("_Cull", (int)CullMode.Off);

            // Physical lighting balance calibrated to soft anime cel/PBR harmony (Azur Promilia / Suomi reference):
            material.SetColor("_SelfLight", new Color(1.40f, 1.38f, 1.36f, 1f));
            material.SetFloat("_MainLightColorLerp", role is DanbaidongSurfaceRole.Face or DanbaidongSurfaceRole.Eye or DanbaidongSurfaceRole.Lash ? 0.24f : 0.18f);
            material.SetFloat("_DirectOcclusion", role is DanbaidongSurfaceRole.Face or DanbaidongSurfaceRole.Eye ? 0.04f : 0.30f);

            // Indirect lighting: let DanbaidongRP's directional SkySystem SH (_AmbientProbeData) lead (85%) instead of flat washout!
            material.SetColor("_SelfEnvColor", new Color(0.16f, 0.18f, 0.25f, 1f));
            material.SetFloat("_EnvColorLerp", 0.15f);
            material.SetFloat("_IndirDiffUpDirSH", role is DanbaidongSurfaceRole.Face or DanbaidongSurfaceRole.Skin ? 0.18f : 0.08f);
            material.SetFloat("_IndirDiffIntensity", role switch
            {
                DanbaidongSurfaceRole.Face => 0.45f,
                DanbaidongSurfaceRole.Skin => 0.42f,
                DanbaidongSurfaceRole.Eye => 0.52f,
                DanbaidongSurfaceRole.Lash => 0.44f,
                _ => 0.40f
            });
            material.SetFloat("_IndirSpecIntensity", role switch
            {
                DanbaidongSurfaceRole.Face => 0.015f,
                DanbaidongSurfaceRole.Lash => 0.04f,
                DanbaidongSurfaceRole.Skin => 0.02f,
                DanbaidongSurfaceRole.Hair => 0.16f,
                DanbaidongSurfaceRole.Transparent => 0.08f,
                DanbaidongSurfaceRole.Stockings => 0.22f,
                DanbaidongSurfaceRole.Eye => 0.42f,
                DanbaidongSurfaceRole.Metal => 0.78f,
                DanbaidongSurfaceRole.Leather => 0.35f,
                _ => 0.38f // Cloth: PBRMask.g keeps matte cloth ~0.10 while gold clasps reach ~0.85!
            });
            material.SetFloat("_Occlusion", role is DanbaidongSurfaceRole.Face or DanbaidongSurfaceRole.Eye ? 0.20f : 0.75f);

            // PBR Metallic & Smoothness (gated by _PBRMask.r and _PBRMask.g on Base/Hair/Stockings/Transparent)
            switch (role)
            {
                case DanbaidongSurfaceRole.Face:
                case DanbaidongSurfaceRole.Lash:
                    material.SetFloat("_Metallic", 0f);
                    material.SetFloat("_Smoothness", role == DanbaidongSurfaceRole.Face ? 0.02f : 0.05f);
                    break;
                case DanbaidongSurfaceRole.Skin:
                    material.SetFloat("_Metallic", 0f);
                    material.SetFloat("_Smoothness", 0.02f);
                    break;
                case DanbaidongSurfaceRole.Eye:
                    material.SetFloat("_Metallic", 0f);
                    material.SetFloat("_Smoothness", 0.62f);
                    break;
                case DanbaidongSurfaceRole.Hair:
                    material.SetFloat("_Metallic", 0f);
                    material.SetFloat("_Smoothness", 0.22f);
                    break;
                case DanbaidongSurfaceRole.Stockings:
                    material.SetFloat("_Metallic", 0f);
                    material.SetFloat("_Smoothness", 0.42f);
                    break;
                case DanbaidongSurfaceRole.Metal:
                    material.SetFloat("_Metallic", 0.92f);
                    material.SetFloat("_Smoothness", 0.80f);
                    break;
                case DanbaidongSurfaceRole.Leather:
                    material.SetFloat("_Metallic", 0.05f);
                    material.SetFloat("_Smoothness", 0.56f);
                    break;
                case DanbaidongSurfaceRole.Transparent:
                    material.SetFloat("_Metallic", 0f);
                    material.SetFloat("_Smoothness", 0.10f);
                    break;
                default: // Cloth (mixed costume atlas: PBRMask.r/g gates matte cloth vs gold clasps vs leather)
                    material.SetFloat("_Metallic", 0.90f);
                    material.SetFloat("_Smoothness", 0.80f);
                    break;
            }

            // Cel shadow transition: remember SigmoidSharp(x, center, _ShadowSmoothNdotL * 5) — HIGHER value = CRISPER cel edge!
            // Official Feishu doc uses 0.70–0.76 for clean anime cel terminator.
            // Soft sculpted figurine cel transition (Suomi/Vepley PVC figurine softness, not harsh flat 2-tone color blocks!)
            material.SetFloat("_ShadowOffset", role switch
            {
                DanbaidongSurfaceRole.Face or DanbaidongSurfaceRole.Lash or DanbaidongSurfaceRole.Eye => 0.46f,
                DanbaidongSurfaceRole.Skin => 0.40f,
                DanbaidongSurfaceRole.Hair => 0.43f,
                DanbaidongSurfaceRole.Metal => 0.46f,
                _ => 0.42f
            });
            material.SetFloat("_ShadowSmoothNdotL", role switch
            {
                DanbaidongSurfaceRole.Face or DanbaidongSurfaceRole.Lash => 0.48f,
                DanbaidongSurfaceRole.Skin => 0.24f, // Ultra-soft creamy skin gradient on shoulders/arms!
                DanbaidongSurfaceRole.Hair => 0.38f,
                DanbaidongSurfaceRole.Stockings => 0.32f,
                DanbaidongSurfaceRole.Leather => 0.30f,
                DanbaidongSurfaceRole.Metal => 0.55f,
                _ => 0.36f
            });
            // Keep _ShadowSmoothScene smooth (0.16) so PCSS/PCF screen-space & per-object shadows stay clean without dithering stipple!
            material.SetFloat("_ShadowSmoothScene", 0.16f);
            material.SetFloat("_ShadowStrength", role switch
            {
                DanbaidongSurfaceRole.Eye => 0.38f,
                DanbaidongSurfaceRole.Face => 0.88f,
                DanbaidongSurfaceRole.Lash => 0.72f,
                DanbaidongSurfaceRole.Skin => 0.88f,
                DanbaidongSurfaceRole.Hair => 0.82f,
                DanbaidongSurfaceRole.Transparent => 0.42f,
                _ => 0.84f
            });

            // Fallback _ShadowColor (also used when _SHADOW_RAMP is disabled) + sRGB Ramp texture
            Color fallbackShadow = role switch
            {
                DanbaidongSurfaceRole.Face or DanbaidongSurfaceRole.Lash => new Color(0.86f, 0.72f, 0.70f, 1f),
                DanbaidongSurfaceRole.Skin => new Color(0.81f, 0.65f, 0.63f, 1f),
                DanbaidongSurfaceRole.Hair => new Color(0.66f, 0.62f, 0.76f, 1f),
                DanbaidongSurfaceRole.Eye => new Color(0.78f, 0.76f, 0.86f, 1f),
                DanbaidongSurfaceRole.Stockings => new Color(0.68f, 0.62f, 0.76f, 1f),
                DanbaidongSurfaceRole.Metal => new Color(0.50f, 0.53f, 0.64f, 1f),
                _ => new Color(0.68f, 0.71f, 0.80f, 1f)
            };
            material.SetColor("_ShadowColor", fallbackShadow);

            if (role != DanbaidongSurfaceRole.Transparent)
            {
                var ramp = DanbaidongLookMaps.GetOrCreateRamp(role, lookDevFolder, options.ForceRegenerateMaps);
                material.SetTexture("_ShadowRampTex", ramp);
                material.SetFloat("_FoldoutShadowRamp", 2f);
                material.SetFloat("_SHADOW_RAMP", 1f);
                material.EnableKeyword("_SHADOW_RAMP");
            }
            else
            {
                material.SetFloat("_FoldoutShadowRamp", 0f);
                material.SetFloat("_SHADOW_RAMP", 0f);
                material.DisableKeyword("_SHADOW_RAMP");
            }

            // PBRMask & subtle micro-detail NormalMap
            if (material.HasProperty("_PBRMask"))
            {
                var pbrMask = DanbaidongLookMaps.GetOrCreatePBRMask(material, role, options.ForceRegenerateMaps);
                if (pbrMask) material.SetTexture("_PBRMask", pbrMask);
            }
            if (material.HasProperty("_NormalMap"))
            {
                var detailNormal = DanbaidongLookMaps.GetOrCreateDetailNormal(material, role, options.ForceRegenerateMaps);
                material.SetTexture("_NormalMap", detailNormal);
                material.SetFloat("_NormalScale", detailNormal
                    ? (role == DanbaidongSurfaceRole.Metal ? 0.34f : 0.24f)
                    : 0f);
            }

            // Depth-based anime Rim Light (soft atmospheric rim on hair & cloth; restrained on skin/face to prevent glowing shoulder edges!)
            if (material.HasProperty("_DirectRimWidth"))
            {
                float directRim = role switch
                {
                    DanbaidongSurfaceRole.Face => 0.25f,
                    DanbaidongSurfaceRole.Lash => 0f,
                    DanbaidongSurfaceRole.Skin => 0.30f,
                    DanbaidongSurfaceRole.Hair => 1.55f,
                    DanbaidongSurfaceRole.Stockings => 0.70f,
                    DanbaidongSurfaceRole.Transparent => 0.90f,
                    DanbaidongSurfaceRole.Leather => 1.50f,
                    DanbaidongSurfaceRole.Metal => 1.50f,
                    DanbaidongSurfaceRole.Cloth when lightFraction > .045f => .35f,
                    _ => 1.20f
                };
                float punctualRim = directRim * (role == DanbaidongSurfaceRole.Cloth ? .55f : 1.05f);
                material.SetFloat("_DirectRimWidth", directRim);
                material.SetFloat("_PunctualRimWidth", punctualRim);
                material.SetColor("_DirectRimFrontCol", skin ? new Color(.24f, .25f, .27f, .25f) : new Color(.35f, .38f, .43f, .35f));
                material.SetColor("_DirectRimBackCol", new Color(0.32f, 0.38f, 0.54f, 0.45f));
            }

            // Hair anisotropic angel-ring specular (supports native GFL2 HairSpecTex if present in folder!)
            if (role == DanbaidongSurfaceRole.Hair && material.HasProperty("_HairSpecTex"))
            {
                string matFolder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(material))?.Replace('\\', '/');
                string nativeHairSpecPath = !string.IsNullOrEmpty(matFolder) ? $"{matFolder}/{material.name}_HairSpec.png" : null;
                Texture2D hairSpec = !string.IsNullOrEmpty(nativeHairSpecPath) && File.Exists(nativeHairSpecPath)
                    ? AssetDatabase.LoadAssetAtPath<Texture2D>(nativeHairSpecPath)
                    : DanbaidongLookMaps.GetOrCreateHairSpecTexture(lookDevFolder, options.ForceRegenerateMaps);
                material.SetTexture("_HairSpecTex", hairSpec);
                var hairSpecTint = options.HairSpecTintOverride ?? new Color(0.95f, 0.90f, 1.05f, 1f);
                material.SetColor("_SpecColor", hairSpecTint);
                material.SetFloat("_AnisotropicSlide", 0.14f);
                material.SetFloat("_AnisotropicOffset", 0.00f);
                material.SetFloat("_BlinnPhongPow", 5.0f);
                material.SetFloat("_SpecMinimum", 0.12f);
            }

            // Stockings Fresnel silk shading
            if (role == DanbaidongSurfaceRole.Stockings && material.HasProperty("_StockingsPow"))
            {
                material.SetFloat("_StockingsPow", 1.85f);
                material.SetColor("_StockingsColorInside", options.StockingsInsideOverride ?? new Color(1.00f, 0.94f, 0.96f, 1f));
                material.SetColor("_StockingsColorOutside", options.StockingsOutsideOverride ?? new Color(0.70f, 0.64f, 0.82f, 1f));
            }

            // Face / Lash / Eye SDF + Fringe Shadow + EyelashOutter see-through
            if (role is DanbaidongSurfaceRole.Face or DanbaidongSurfaceRole.Lash or DanbaidongSurfaceRole.Eye)
            {
                var faceMap = FindFaceLightMap(material, options.ActorName);
                if (faceMap && material.HasProperty("_FaceLightMap"))
                {
                    material.SetTexture("_FaceLightMap", faceMap);
                    material.SetFloat("_FaceSdfUV3", options.FaceSdfUsesUV3 ? 1f : 0f);
                }

                // Disable blotchy scene shadow reception on Face/Eye/Lash — use clean Face SDF + Hair Fringe Shadow exclusively!
                if (material.HasProperty("_SceneShadowWeight"))
                    material.SetFloat("_SceneShadowWeight", 0.0f);
                if (material.HasProperty("_FringeShadowWeight"))
                    material.SetFloat("_FringeShadowWeight", role == DanbaidongSurfaceRole.Eye ? 0.18f : 0.55f);
                if (material.HasProperty("_FaceFormWeight"))
                    material.SetFloat("_FaceFormWeight", role == DanbaidongSurfaceRole.Face ? 0.10f : 0f);

                if (material.HasProperty("_FringeShadowSource"))
                {
                    material.SetFloat("_FringeShadowSource", 0f);
                    material.EnableKeyword("FS_Mesh");
                    material.DisableKeyword("FS_RayTracing");
                }

                if (material.HasProperty("_NoseSpecColor"))
                {
                    material.SetColor("_NoseSpecColor", role == DanbaidongSurfaceRole.Face
                        ? new Color(1.0f, 0.96f, 0.94f, 0.08f)
                        : Color.clear);
                    material.SetFloat("_NoseSpecMin", 0.05f);
                    material.SetFloat("_NoseSpecMax", 0.55f);
                }

                // Eyelash / Eye see-through over hair bangs via CharacterTransparent pass (Stencil test with Hair)
                bool enableOutter = role == DanbaidongSurfaceRole.Lash ||
                                    (role == DanbaidongSurfaceRole.Eye && options.EnableEyeSeeThrough);
                material.SetShaderPassEnabled("CharacterTransparent", enableOutter);
                if (material.HasProperty("_FoldoutEyelash"))
                    material.SetFloat("_FoldoutEyelash", enableOutter ? 2f : 0f);
                if (enableOutter)
                {
                    material.SetTexture("_BaseMapOutter", albedo);
                    material.SetColor("_BaseColorOutter", Color.white);
                    material.SetFloat("_AlphaOutter", role == DanbaidongSurfaceRole.Lash ? 0.72f : 0.26f);
                }
            }

            if (role == DanbaidongSurfaceRole.Eye && material.HasProperty("_ParallaxScale"))
            {
                // Keep parallax 0 by default so off-center eye atlases (e.g. GFL2/MMD) never shift downward or clip!
                material.SetFloat("_ParallaxScale", 0f);
                material.SetFloat("_ParallaxMaskEdge", 0.78f);
                material.SetFloat("_ParallaxMaskEdgeOffset", 0.22f);
            }

            // Alpha Clip
            material.SetFloat("_AlphaClip", options.Cutout ? 1f : 0f);
            material.SetFloat("_Cutoff", options.Cutoff > 0f ? options.Cutoff : 0.24f);
            if (options.Cutout) material.EnableKeyword("_ALPHATEST_ON");
            else material.DisableKeyword("_ALPHATEST_ON");

            // Transparent shader settings (airy sheer scalloped lace veil, eye highlights)
            if (role == DanbaidongSurfaceRole.Transparent)
            {
                material.SetFloat("_Alpha", options.MultiplyOverlay ? 1f : 0.52f);
                material.SetFloat("_BlendSrc", (int)(options.MultiplyOverlay ? BlendMode.DstColor : BlendMode.SrcAlpha));
                material.SetFloat("_BlendDst", (int)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_BlendOp", (int)BlendOp.Add);
                material.SetFloat("_ZWrite", 0f);
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetShaderPassEnabled("TransparentBackface", false);
                if (material.HasProperty("_FoldoutTransBackface"))
                    material.SetFloat("_FoldoutTransBackface", 0f);
                material.SetShaderPassEnabled("DepthOnly", false);
                material.SetShaderPassEnabled("ShadowCaster", false);
            }

            // Material ToonFlags (for GBuffer0 Stencil / HairMask / FringeShadow / EyelashOutter)
            int toonFlag = role switch
            {
                DanbaidongSurfaceRole.Face => 3, // FLAG_FACE
                DanbaidongSurfaceRole.Hair => 2, // FLAG_HAIRMASK
                DanbaidongSurfaceRole.Eye or DanbaidongSurfaceRole.Lash => 1, // FLAG_EYELASH
                _ => -1
            };
            ConfigureToonFlags(material, toonFlag);

            // Crisp continuous figurine ink outlines (calibrated to DanbaidongRP Suomi/Vepley reference!)
            bool enableOutline = role is DanbaidongSurfaceRole.Face or DanbaidongSurfaceRole.Hair
                or DanbaidongSurfaceRole.Skin or DanbaidongSurfaceRole.Cloth or DanbaidongSurfaceRole.Leather
                or DanbaidongSurfaceRole.Metal or DanbaidongSurfaceRole.Stockings
                && (!options.Cutout || role == DanbaidongSurfaceRole.Hair);

            material.SetShaderPassEnabled("CharacterOutline", enableOutline);
            if (material.HasProperty("_FoldoutOutline"))
                material.SetFloat("_FoldoutOutline", enableOutline ? 2f : 0f);

            if (enableOutline)
            {
                float widthScale = options.OutlineWidthMultiplier ?? 1f;
                float baseWidth = role switch
                {
                    DanbaidongSurfaceRole.Face => 0.52f,
                    DanbaidongSurfaceRole.Skin => 0.68f,
                    DanbaidongSurfaceRole.Stockings => 0.68f,
                    DanbaidongSurfaceRole.Hair => 0.82f,
                    DanbaidongSurfaceRole.Metal => 0.84f,
                    DanbaidongSurfaceRole.Leather => 0.88f,
                    _ => 0.85f
                };
                // Albedo-harmonized colored anime line-art (warm rose-cocoa on skin/face,
                // steel-lavender on silver hair, plum on violet hair, soft taupe-lavender on white/cream dresses!)
                Color outlineCol = DanbaidongLookMaps.EstimateOutlineColor(average, role);

                if (options.UseMeshNormals)
                {
                    material.SetFloat("_OutLineNormalSource", 1f);
                    material.EnableKeyword("SN_VertNormal");
                    material.DisableKeyword("SN_VertColor");
                    material.DisableKeyword("SN_SmoothUV4");
                }
                else if (options.UseVertexColorOutline)
                {
                    material.SetFloat("_OutLineNormalSource", 0f);
                    material.EnableKeyword("SN_VertColor");
                    material.DisableKeyword("SN_VertNormal");
                    material.DisableKeyword("SN_SmoothUV4");
                }
                else
                {
                    material.SetFloat("_OutLineNormalSource", 2f);
                    material.DisableKeyword("SN_VertColor");
                    material.DisableKeyword("SN_VertNormal");
                    material.EnableKeyword("SN_SmoothUV4");
                }

                material.SetFloat("_OutlineWidth", baseWidth * widthScale);
                material.SetFloat("_OutlineClampScale", 0.62f);
                material.SetFloat("_OutlineStencilComp", (int)CompareFunction.NotEqual);
                material.SetColor("_OutlineColor", outlineCol);
                material.SetColor("_OutlineDirectLightingColor", new Color(1.00f, 0.98f, 1.00f, 0.38f));
                material.SetFloat("_OutlineDirectLightingOffset", -0.20f);
            }

            EditorUtility.SetDirty(material);
        }

        public static void ConfigureToonFlags(Material material, int flagIndex)
        {
            if (!material.HasProperty("_ToonFlagsKeywords")) return;
            material.SetFloat("_ToonFlagsKeywords", flagIndex);
            for (int i = 0; i < ToonFlagKeywords.Length; i++)
            {
                if (i == flagIndex) material.EnableKeyword(ToonFlagKeywords[i]);
                else material.DisableKeyword(ToonFlagKeywords[i]);
            }
        }

        public static Texture2D FindFaceLightMap(Material material, string actorName)
        {
            string matPath = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(matPath)) return null;
            string folder = Path.GetDirectoryName(matPath).Replace('\\', '/');

            string[] candidates =
            {
                !string.IsNullOrEmpty(actorName) ? $"{folder}/{actorName}FaceSDF.png" : null,
                $"{folder}/FaceSDF.png"
            };

            string foundPath = null;
            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                {
                    foundPath = candidate;
                    break;
                }
            }

            if (foundPath == null)
            {
                // Portable fallback: find any *FaceSDF.png in the same character folder
                var files = Directory.GetFiles(folder, "*FaceSDF.png", SearchOption.TopDirectoryOnly);
                if (files.Length > 0) foundPath = files[0].Replace('\\', '/');
            }

            if (string.IsNullOrEmpty(foundPath)) return null;

            AssetDatabase.ImportAsset(foundPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(foundPath) is TextureImporter importer)
            {
                if (importer.sRGBTexture || importer.wrapMode != TextureWrapMode.Clamp ||
                    importer.textureCompression != TextureImporterCompression.Uncompressed ||
                    importer.mipmapEnabled)
                {
                    importer.sRGBTexture = false;
                    importer.mipmapEnabled = false;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(foundPath);
        }

        private static bool ContainsAny(string text, params string[] terms)
        {
            for (int i = 0; i < terms.Length; i++)
                if (text.Contains(terms[i], StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
