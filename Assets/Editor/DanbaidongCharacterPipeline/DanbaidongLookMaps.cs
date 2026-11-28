using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DanbaidongRP.CharacterPipeline.Editor
{
    public enum DanbaidongSurfaceRole
    {
        Face,
        Lash,
        Eye,
        Hair,
        Skin,
        Cloth,
        Leather,
        Metal,
        Stockings,
        Transparent
    }

    /// <summary>
    /// Portable look-development map generator for DanbaidongRP PBRToon shaders:
    /// - sRGB 4-band Shadow & Specular Ramp textures (matching DanbaidongRP SampleDirectShadowRamp / SampleDirectSpecularRamp)
    /// - Anisotropic HairSpecTex (angel-ring highlight band)
    /// - Albedo-aware PBRMask (R=Metallic, G=Smoothness, B=AO, A=1-Emission) separating matte fabric, satin leather, and metallic trims
    /// - Subtle cloth/metal micro-detail normal maps
    /// </summary>
    public static class DanbaidongLookMaps
    {
        public const string DefaultLookDevFolder = "Assets/Content/Art/Characters/LookDevelopment";

        /// <summary>
        /// Generates an sRGB 256x16 DanbaidongRP Ramp texture:
        ///   Band 0 (y = 0..3, sampled at y = 0.125): Direct Shadow Ramp with anime SSS terminator band.
        ///   Band 1 (y = 4..7, sampled at y = 0.375): Direct Specular Ramp (pure black for matte surfaces to avoid plastic look).
        ///   Band 2-3 (y = 8..15): Auxiliary copy of Band 0.
        /// </summary>
        public static Texture2D GetOrCreateRamp(
            DanbaidongSurfaceRole role,
            string folder = DefaultLookDevFolder,
            bool forceRegenerate = false)
        {
            Directory.CreateDirectory(folder);
            string name = role switch
            {
                DanbaidongSurfaceRole.Face => "FaceRamp",
                DanbaidongSurfaceRole.Lash => "FaceRamp",
                DanbaidongSurfaceRole.Skin => "SkinRamp",
                DanbaidongSurfaceRole.Eye => "EyeRamp",
                DanbaidongSurfaceRole.Hair => "HairRamp",
                DanbaidongSurfaceRole.Metal => "MetalRamp",
                DanbaidongSurfaceRole.Leather => "LeatherRamp",
                DanbaidongSurfaceRole.Stockings => "StockingsRamp",
                _ => "ClothRamp"
            };
            string path = $"{folder}/{name}.png";
            if (!forceRegenerate)
            {
                var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (existing) return existing;
            }

            // Palette tuned in sRGB space (DanbaidongRP Ramps use sRGBTexture = true, matching DirectionalShadowRamp.png).
            Color deepShadow, midShadow, sssBand, litColor;
            float specPeak, specPower;
            Color specTint = Color.white;

            switch (role)
            {
                case DanbaidongSurfaceRole.Face:
                case DanbaidongSurfaceRole.Lash:
                    // Airy translucent porcelain anime face (Azur Promilia / Suomi reference):
                    // ultra-soft cool-rosy shadow (zero harsh orange/salmon contrast!) + subtle peach SSS terminator.
                    deepShadow = new Color(0.90f, 0.80f, 0.83f, 1f);
                    midShadow  = new Color(0.95f, 0.87f, 0.88f, 1f);
                    sssBand    = new Color(1.00f, 0.91f, 0.89f, 1f);
                    litColor   = Color.white;
                    specPeak   = 0.0f;
                    specPower  = 4.0f;
                    break;

                case DanbaidongSurfaceRole.Skin:
                    // Powdery soft anime skin: gentle rose-lavender shadow + creamy peach rolloff, zero burnt-orange or plastic spec.
                    deepShadow = new Color(0.86f, 0.75f, 0.78f, 1f);
                    midShadow  = new Color(0.93f, 0.84f, 0.85f, 1f);
                    sssBand    = new Color(0.99f, 0.90f, 0.87f, 1f);
                    litColor   = Color.white;
                    specPeak   = 0.0f;
                    specPower  = 4.0f;
                    break;

                case DanbaidongSurfaceRole.Hair:
                    // Luminous cool-lavender anime hair depth (keeps silver/pastel hair airy & volumetric, never muddy dark blocks).
                    deepShadow = new Color(0.62f, 0.60f, 0.76f, 1f);
                    midShadow  = new Color(0.77f, 0.75f, 0.88f, 1f);
                    sssBand    = new Color(0.93f, 0.90f, 0.97f, 1f);
                    litColor   = Color.white;
                    specPeak   = 0.0f;
                    specPower  = 4.0f;
                    break;

                case DanbaidongSurfaceRole.Eye:
                    // Crisp luminous anime eyes.
                    deepShadow = new Color(0.82f, 0.81f, 0.91f, 1f);
                    midShadow  = new Color(0.91f, 0.90f, 0.96f, 1f);
                    sssBand    = new Color(0.98f, 0.97f, 1.00f, 1f);
                    litColor   = Color.white;
                    specPeak   = 0.06f;
                    specPower  = 3.0f;
                    break;

                case DanbaidongSurfaceRole.Stockings:
                    // Sheer stockings: soft violet shadow + gentle silky satin highlight.
                    deepShadow = new Color(0.58f, 0.54f, 0.74f, 1f);
                    midShadow  = new Color(0.75f, 0.71f, 0.86f, 1f);
                    sssBand    = new Color(0.92f, 0.87f, 0.95f, 1f);
                    litColor   = Color.white;
                    specPeak   = 0.05f;
                    specPower  = 3.2f;
                    specTint   = new Color(0.96f, 0.91f, 1.0f, 1f);
                    break;

                case DanbaidongSurfaceRole.Metal:
                    // High-contrast metallic shadow with crisp stylized specular boost.
                    deepShadow = new Color(0.42f, 0.45f, 0.58f, 1f);
                    midShadow  = new Color(0.64f, 0.66f, 0.78f, 1f);
                    sssBand    = new Color(0.88f, 0.88f, 0.94f, 1f);
                    litColor   = Color.white;
                    specPeak   = 0.30f;
                    specPower  = 2.5f;
                    specTint   = new Color(1.0f, 0.97f, 0.90f, 1f);
                    break;

                case DanbaidongSurfaceRole.Leather:
                    // Figurine satin leather: rich sculpted shadow + broad soft sheen.
                    deepShadow = new Color(0.50f, 0.47f, 0.59f, 1f);
                    midShadow  = new Color(0.70f, 0.67f, 0.78f, 1f);
                    sssBand    = new Color(0.90f, 0.87f, 0.92f, 1f);
                    litColor   = Color.white;
                    specPeak   = 0.12f;
                    specPower  = 2.8f;
                    break;

                default: // Cloth
                    // Clean harmonious anime fabric shadow: bright cream/white cloth stays airy & pastel, rich colors stay crisp.
                    deepShadow = new Color(0.64f, 0.66f, 0.78f, 1f);
                    midShadow  = new Color(0.79f, 0.81f, 0.90f, 1f);
                    sssBand    = new Color(0.94f, 0.93f, 0.97f, 1f);
                    litColor   = Color.white;
                    specPeak   = 0.0f;
                    specPower  = 4.0f;
                    break;
            }

            const int width = 256;
            const int height = 16; // 4 bands of 4px each: band 0 [0..3] (y=0.125), band 1 [4..7] (y=0.375)
            var pixels = new Color[width * height];

            for (int x = 0; x < width; x++)
            {
                float t = x / (width - 1f);
                Color shadowCol;
                if (t < 0.35f)
                {
                    shadowCol = Color.Lerp(deepShadow, midShadow, Mathf.SmoothStep(0f, 1f, t / 0.35f));
                }
                else if (t < 0.68f)
                {
                    shadowCol = Color.Lerp(midShadow, sssBand, Mathf.SmoothStep(0f, 1f, (t - 0.35f) / 0.33f));
                }
                else
                {
                    shadowCol = Color.Lerp(sssBand, litColor, Mathf.SmoothStep(0f, 1f, (t - 0.68f) / 0.32f));
                }
                shadowCol.a = 1f;

                float specAmt = specPeak > 0f ? specPeak * Mathf.Pow(Mathf.SmoothStep(0.15f, 1f, t), specPower) : 0f;
                var specCol = new Color(specTint.r * specAmt, specTint.g * specAmt, specTint.b * specAmt, 1f);

                for (int y = 0; y < height; y++)
                {
                    pixels[y * width + x] = (y >= 4 && y < 8) ? specCol : shadowCol;
                }
            }

            return SaveTexture(path, width, height, pixels, sRgb: true, mipmaps: false,
                TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Bilinear);
        }

        /// <summary>
        /// Generates a portable 512x512 anisotropic anime hair angel-ring highlight texture (_HairSpecTex).
        /// Sampled in PBRToonHair at float2(UV1.x, UV1.y - viewDirWS.y * _AnisotropicSlide + _AnisotropicOffset).
        /// </summary>
        public static Texture2D GetOrCreateHairSpecTexture(
            string folder = DefaultLookDevFolder,
            bool forceRegenerate = false)
        {
            Directory.CreateDirectory(folder);
            string path = $"{folder}/AnimeHairSpec.png";
            if (!forceRegenerate)
            {
                var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (existing) return existing;
            }

            const int width = 512;
            const int height = 512;
            var pixels = new Color[width * height];

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float tau = u * Mathf.PI * 2f;

                    // Multi-frequency stylized zigzag angel-ring wave across head circumference (seamless in U).
                    float wave =
                        0.036f * Mathf.Sin(tau * 6f + 0.4f) +
                        0.022f * Mathf.Sin(tau * 13f + 1.7f) +
                        0.013f * Mathf.Sin(tau * 27f + 0.9f) +
                        0.007f * Mathf.Sin(tau * 53f + 2.3f);

                    // Primary luminous ribbon centered near v = 0.52.
                    float primaryCenter = 0.52f + wave;
                    float primaryHalfWidth = 0.068f + 0.018f * Mathf.Sin(tau * 11f + 0.3f);
                    float distPrimary = Mathf.Abs(v - primaryCenter) / primaryHalfWidth;
                    float primaryBand = Mathf.Pow(Mathf.Clamp01(1f - distPrimary), 2.1f);

                    // Delicate secondary upper glint ribbon for silky multi-tier anime hair feel.
                    float secondaryCenter = 0.65f + wave * 0.75f;
                    float distSecondary = Mathf.Abs(v - secondaryCenter) / 0.034f;
                    float secondaryBand = 0.42f * Mathf.Pow(Mathf.Clamp01(1f - distSecondary), 2.4f);

                    // Fine vertical strand breakup so the ring reads as silky hair strands, not a flat plastic bar.
                    float strandA = 0.5f + 0.5f * Mathf.Sin(tau * 42f + Mathf.Sin(tau * 7f) * 1.4f);
                    float strandB = 0.5f + 0.5f * Mathf.Sin(tau * 96f + 1.1f);
                    float strandMod = Mathf.Lerp(0.42f, 1.0f, strandA * 0.65f + strandB * 0.35f);

                    // Taper gently at very top/bottom so clamping never produces a hard edge.
                    float vFade = Mathf.SmoothStep(0.05f, 0.22f, v) * Mathf.SmoothStep(0.95f, 0.78f, v);

                    float value = Mathf.Clamp01((primaryBand + secondaryBand) * strandMod * vFade);
                    pixels[y * width + x] = new Color(value, value, value, 1f);
                }
            }

            return SaveTexture(path, width, height, pixels, sRgb: false, mipmaps: true,
                TextureWrapMode.Repeat, TextureWrapMode.Clamp, FilterMode.Bilinear);
        }

        /// <summary>
        /// Generates an albedo-aware Linear PBRMask (R=Metallic, G=Smoothness, B=AO, A=1-Emission).
        /// Automatically detects gold/brass/silver metal accents and dark leather regions inside mixed cloth atlases,
        /// while keeping matte fabric and skin genuinely matte to prevent hard-plastic reflections.
        /// </summary>
        public static Texture2D GetOrCreatePBRMask(
            Material material,
            DanbaidongSurfaceRole role,
            bool forceRegenerate = false)
        {
            string matPath = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(matPath)) return null;
            string folder = Path.GetDirectoryName(matPath).Replace('\\', '/');
            string path = $"{folder}/{material.name}_PBRMask.png";
            if (!forceRegenerate)
            {
                var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (existing) return existing;
            }

            // 1. Check for native GFL2/PBR RMO map (R=Roughness, G=Metallic, B=AO) in the character folder!
            string rmoPath = $"{folder}/{material.name}_RMO.png";
            bool hasRmo = File.Exists(rmoPath);

            // 2. Use explicit art ILM map when present (e.g. StarTravelerClothILM.png).
            bool hasStarIlm = !hasRmo && material.name is "StarTraveler_T07" or "StarTraveler_Metal";
            string ilmPath = hasStarIlm ? $"{folder}/StarTravelerClothILM.png" : null;
            string sourcePath = hasRmo
                ? rmoPath
                : (!string.IsNullOrEmpty(ilmPath) && File.Exists(ilmPath)
                    ? ilmPath
                    : AssetDatabase.GetAssetPath(GetAlbedoTexture(material)));

            var input = new Texture2D(2, 2, TextureFormat.RGBA32, false, hasRmo || hasStarIlm);
            try
            {
                bool hasSource = !string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath) &&
                                 input.LoadImage(File.ReadAllBytes(sourcePath));
                int size = hasSource ? Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Min(input.width, input.height)), 128, hasRmo ? 1024 : 512) : 64;
                var pixels = new Color[size * size];

                for (int y = 0; y < size; y++)
                {
                    float v = (y + 0.5f) / size;
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) / size;
                        Color c = hasSource ? input.GetPixelBilinear(u, v) : Color.white;
                        Color.RGBToHSV(c, out float h, out float s, out float val);
                        float lum = c.grayscale;

                        float metallic = 0f;
                        float smoothness = 0.12f;
                        float ao = 0.94f;
                        const float invEmission = 1f; // A=1 means emission=0

                        if (hasRmo && hasSource)
                        {
                            // Native GFL2 RMO packing: R=Roughness, G=Metallic, B=AO -> DanbaidongRP PBRMask (R=Metallic, G=Smoothness, B=AO, A=1)
                            metallic = c.g;
                            smoothness = Mathf.Clamp01(1f - c.r);
                            ao = Mathf.Clamp(c.b, 0.15f, 1f);
                        }
                        else if (hasStarIlm && hasSource)
                        {
                            // Repack StarTraveler ILM: R=AO, G=Specular/Gloss, B=Metal mask
                            bool isMetal = role == DanbaidongSurfaceRole.Metal || c.b > 0.65f;
                            metallic = isMetal ? Mathf.Lerp(0.75f, 0.96f, c.b) : 0f;
                            smoothness = isMetal
                                ? Mathf.Lerp(0.55f, 0.85f, c.g)
                                : Mathf.Lerp(0.10f, 0.34f, c.g);
                            ao = Mathf.Lerp(0.80f, 1.0f, c.r);
                        }
                        else
                        {
                            switch (role)
                            {
                                case DanbaidongSurfaceRole.Skin:
                                case DanbaidongSurfaceRole.Face:
                                case DanbaidongSurfaceRole.Lash:
                                    metallic = 0f;
                                    smoothness = 0.08f;
                                    ao = Mathf.Lerp(0.90f, 1.0f, Mathf.SmoothStep(0.25f, 0.85f, lum));
                                    break;

                                case DanbaidongSurfaceRole.Hair:
                                    // Low GGX smoothness so hair isn't plastic; anisotropic _HairSpecTex handles highlights.
                                    metallic = 0f;
                                    smoothness = Mathf.Lerp(0.14f, 0.24f, lum);
                                    ao = Mathf.Lerp(0.80f, 1.0f, Mathf.SmoothStep(0.15f, 0.80f, lum));
                                    break;

                                case DanbaidongSurfaceRole.Stockings:
                                    metallic = 0f;
                                    smoothness = Mathf.Lerp(0.26f, 0.38f, lum);
                                    ao = Mathf.Lerp(0.86f, 1.0f, lum);
                                    break;

                                case DanbaidongSurfaceRole.Metal:
                                    // Mixed metal emblems (e.g. Lily_T02) may have dark cloth backing + bright silver/gold hardware.
                                    bool brightHardware = val > 0.36f && (s < 0.28f || (h >= 0.06f && h <= 0.17f));
                                    if (brightHardware || !hasSource)
                                    {
                                        metallic = Mathf.Lerp(0.78f, 0.96f, Mathf.SmoothStep(0.36f, 0.82f, val));
                                        smoothness = Mathf.Lerp(0.62f, 0.88f, Mathf.SmoothStep(0.36f, 0.88f, val));
                                    }
                                    else
                                    {
                                        metallic = 0.08f;
                                        smoothness = 0.22f;
                                    }
                                    ao = Mathf.Lerp(0.78f, 1.0f, Mathf.SmoothStep(0.10f, 0.75f, lum));
                                    break;

                                case DanbaidongSurfaceRole.Leather:
                                    metallic = 0.02f;
                                    smoothness = Mathf.Lerp(0.38f, 0.56f, lum);
                                    ao = Mathf.Lerp(0.80f, 1.0f, lum);
                                    break;

                                case DanbaidongSurfaceRole.Transparent:
                                    metallic = 0f;
                                    smoothness = 0.12f;
                                    ao = 0.96f;
                                    break;

                                default: // Cloth (mixed anime costume atlas: matte cloth + gold clasps + dark leather straps)
                                    // 1. Detect gold / brass metallic trims & buttons (warm yellow-ochre hue, rich saturation)
                                    bool isGold = h >= 0.065f && h <= 0.155f && s >= 0.34f && val >= 0.32f && val <= 0.96f;
                                    // 2. Detect dark brown/charcoal leather collars, belts, boot straps
                                    bool isLeather = !isGold && val < 0.26f && val > 0.03f && s < 0.62f;
                                    if (isGold)
                                    {
                                        float goldConfidence = Mathf.SmoothStep(0.34f, 0.58f, s) * Mathf.SmoothStep(0.32f, 0.52f, val);
                                        metallic = Mathf.Lerp(0.25f, 0.92f, goldConfidence);
                                        smoothness = Mathf.Lerp(0.45f, 0.86f, goldConfidence);
                                    }
                                    else if (isLeather)
                                    {
                                        metallic = 0.02f;
                                        smoothness = Mathf.Lerp(0.36f, 0.50f, Mathf.InverseLerp(0.04f, 0.26f, val));
                                    }
                                    else
                                    {
                                        // Matte cloth (white nurse uniform, pastel blue cloth, soft ribbons): ZERO plastic shine!
                                        metallic = 0f;
                                        smoothness = Mathf.Lerp(0.08f, 0.16f, Mathf.SmoothStep(0.20f, 0.90f, lum));
                                    }
                                    ao = Mathf.Lerp(0.82f, 1.0f, Mathf.SmoothStep(0.08f, 0.70f, lum));
                                    break;
                            }
                        }

                        pixels[y * size + x] = new Color(metallic, smoothness, ao, invEmission);
                    }
                }

                return SaveTexture(path, size, size, pixels, sRgb: false, mipmaps: true,
                    TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Bilinear);
            }
            finally
            {
                Object.DestroyImmediate(input);
            }
        }

        /// <summary>
        /// Generates a subtle tangent-space micro-detail normal map from albedo luminance for Cloth/Metal/Leather.
        /// Adds crisp stitch/fold/bevel tactile relief without muddying anime cel planes.
        /// </summary>
        public static Texture2D GetOrCreateDetailNormal(
            Material material,
            DanbaidongSurfaceRole role,
            bool forceRegenerate = false)
        {
            if (role is not (DanbaidongSurfaceRole.Cloth or DanbaidongSurfaceRole.Metal or DanbaidongSurfaceRole.Leather))
                return null;

            string matPath = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(matPath)) return null;
            string folder = Path.GetDirectoryName(matPath).Replace('\\', '/');

            // Respect artist-authored normal map if one exists.
            string authoredPath = $"{folder}/{material.name}_Normal.png";
            if (File.Exists(authoredPath))
            {
                EnsureNormalImporter(authoredPath);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(authoredPath);
            }

            string path = $"{folder}/{material.name}_DetailNormal.png";
            if (!forceRegenerate)
            {
                var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (existing) return existing;
            }

            string sourcePath = AssetDatabase.GetAssetPath(GetAlbedoTexture(material));
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return null;

            var input = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                if (!input.LoadImage(File.ReadAllBytes(sourcePath))) return null;
                int size = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Min(input.width, input.height)), 256, 512);
                var heightMap = new float[size * size];
                for (int y = 0; y < size; y++)
                {
                    float v = (y + 0.5f) / size;
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) / size;
                        heightMap[y * size + x] = input.GetPixelBilinear(u, v).grayscale;
                    }
                }

                float strength = role == DanbaidongSurfaceRole.Metal ? 1.8f : 1.15f;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    int ym = Mathf.Max(0, y - 1);
                    int yp = Mathf.Min(size - 1, y + 1);
                    for (int x = 0; x < size; x++)
                    {
                        int xm = Mathf.Max(0, x - 1);
                        int xp = Mathf.Min(size - 1, x + 1);

                        float tl = heightMap[yp * size + xm], t = heightMap[yp * size + x], tr = heightMap[yp * size + xp];
                        float l  = heightMap[y  * size + xm],                                r  = heightMap[y  * size + xp];
                        float bl = heightMap[ym * size + xm], b = heightMap[ym * size + x], br = heightMap[ym * size + xp];

                        float dx = (tr + 2f * r + br) - (tl + 2f * l + bl);
                        float dy = (tl + 2f * t + tr) - (bl + 2f * b + br);

                        var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                        pixels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                    }
                }

                return SaveNormalTexture(path, size, size, pixels);
            }
            finally
            {
                Object.DestroyImmediate(input);
            }
        }

        /// <summary>
        /// Computes an albedo-harmonized colored anime ink outline (Azur Promilia / Genshin style):
        /// - Skin/Face get warm delicate rose-cocoa line-art
        /// - Hair gets rich hue-matched deeper-tinted ink (steel-lavender for silver hair, plum for violet hair, cocoa for blonde)
        /// - Cloth/Stockings/Leather/Metal harmonize with the material's main albedo (soft lavender-taupe on cream/white dresses!)
        /// </summary>
        public static Color EstimateOutlineColor(Material material, DanbaidongSurfaceRole role)
        {
            if (role == DanbaidongSurfaceRole.Face)
                return new Color(0.56f, 0.36f, 0.36f, 0.86f);
            if (role == DanbaidongSurfaceRole.Skin)
                return new Color(0.54f, 0.34f, 0.34f, 0.88f);

            Color fallback = role switch
            {
                DanbaidongSurfaceRole.Hair => new Color(0.30f, 0.27f, 0.42f, 0.92f),
                DanbaidongSurfaceRole.Stockings => new Color(0.28f, 0.23f, 0.38f, 0.88f),
                DanbaidongSurfaceRole.Metal => new Color(0.22f, 0.20f, 0.24f, 0.92f),
                DanbaidongSurfaceRole.Leather => new Color(0.22f, 0.19f, 0.24f, 0.92f),
                _ => new Color(0.34f, 0.32f, 0.40f, 0.90f)
            };

            var albedoTex = GetAlbedoTexture(material);
            string sourcePath = albedoTex ? AssetDatabase.GetAssetPath(albedoTex) : null;
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return fallback;

            var input = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                if (!input.LoadImage(File.ReadAllBytes(sourcePath))) return fallback;

                // Sample 24x24 grid ignoring transparent pixels and near-black UV padding
                double rSum = 0, gSum = 0, bSum = 0, wSum = 0;
                const int grid = 24;
                for (int y = 0; y < grid; y++)
                {
                    float v = (y + 0.5f) / grid;
                    for (int x = 0; x < grid; x++)
                    {
                        float u = (x + 0.5f) / grid;
                        Color c = input.GetPixelBilinear(u, v);
                        if (c.a < 0.25f) continue;
                        float lum = c.grayscale;
                        if (lum < 0.04f) continue;
                        rSum += c.r;
                        gSum += c.g;
                        bSum += c.b;
                        wSum += 1.0;
                    }
                }
                if (wSum < 4.0) return fallback;

                var avg = new Color((float)(rSum / wSum), (float)(gSum / wSum), (float)(bSum / wSum), 1f);
                Color.RGBToHSV(avg, out float h, out float s, out float val);

                if (role == DanbaidongSurfaceRole.Hair)
                {
                    // Harmonious anime hair line-art: slightly deeper & richer with subtle cool-lavender ink poise
                    float outS = Mathf.Clamp(s * 1.25f + 0.14f, 0.16f, 0.52f);
                    float outV = Mathf.Clamp(val * 0.44f, 0.24f, 0.44f);
                    // For near-achromatic silver/white hair, steer hue toward soft cool slate-lavender
                    float outH = s < 0.12f ? 0.68f : h;
                    Color ink = Color.HSVToRGB(outH, outS, outV);
                    return new Color(ink.r, ink.g, ink.b, 0.92f);
                }

                // Cloth / Stockings / Leather / Metal: cream/white cloth gets delicate taupe-lavender line,
                // dark fabrics get rich deep ink, colorful fabrics get hue-matched deeper lines!
                float cS = Mathf.Clamp(s * 1.15f + 0.10f, 0.10f, 0.46f);
                float cV = Mathf.Clamp(val * 0.45f, 0.18f, 0.46f);
                float cH = s < 0.10f ? 0.67f : h;
                Color clothInk = Color.HSVToRGB(cH, cS, cV);
                return new Color(clothInk.r, clothInk.g, clothInk.b, 0.90f);
            }
            finally
            {
                Object.DestroyImmediate(input);
            }
        }

        public static Texture GetAlbedoTexture(Material material)
        {
            if (!material) return null;
            if (material.HasProperty("_BaseMap"))
            {
                var baseMap = material.GetTexture("_BaseMap");
                if (baseMap) return baseMap;
            }
            return material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
        }

        private static Texture2D SaveTexture(
            string path, int width, int height, Color[] pixels,
            bool sRgb, bool mipmaps,
            TextureWrapMode wrapU, TextureWrapMode wrapV, FilterMode filterMode)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, !sRgb);
            try
            {
                texture.SetPixels(pixels);
                texture.Apply();
                byte[] data = texture.EncodeToPNG();
                bool unchanged = File.Exists(path) && data.SequenceEqual(File.ReadAllBytes(path));
                if (!unchanged) File.WriteAllBytes(path, data);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool dirty = importer.sRGBTexture != sRgb ||
                         importer.mipmapEnabled != mipmaps ||
                         importer.npotScale != TextureImporterNPOTScale.None ||
                         importer.textureCompression != TextureImporterCompression.Uncompressed ||
                         importer.wrapModeU != wrapU ||
                         importer.wrapModeV != wrapV ||
                         importer.filterMode != filterMode;
            if (dirty)
            {
                importer.sRGBTexture = sRgb;
                importer.mipmapEnabled = mipmaps;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapModeU = wrapU;
                importer.wrapModeV = wrapV;
                importer.filterMode = filterMode;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D SaveNormalTexture(string path, int width, int height, Color[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                texture.SetPixels(pixels);
                texture.Apply();
                byte[] data = texture.EncodeToPNG();
                if (!File.Exists(path) || !data.SequenceEqual(File.ReadAllBytes(path)))
                    File.WriteAllBytes(path, data);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            EnsureNormalImporter(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void EnsureNormalImporter(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            if (importer.textureType != TextureImporterType.NormalMap || importer.sRGBTexture)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
        }
    }
}
