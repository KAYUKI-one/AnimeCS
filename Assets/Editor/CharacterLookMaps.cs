using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AnimeCS.Editor
{
    internal static class CharacterLookMaps
    {
        private const string Ramps = "Assets/Content/Art/Characters/LookDevelopment";

        internal static Texture2D Ramp(string surface, Color shade, float specular)
        {
            Directory.CreateDirectory(Ramps);
            string path = $"{Ramps}/{surface}Ramp.png";
            const int width = 128, height = 8;
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float t = x / (width - 1f);
                    float transition = Mathf.SmoothStep(0f, 1f, t);
                    var highlight = new Color(specular, specular, specular, 1f) * Mathf.Pow(t, 2f);
                    pixels[y * width + x] = y is 2 or 3 ? highlight : Color.Lerp(shade, Color.white, transition);
                }
            return Save(path, width, height, pixels);
        }

        internal static Texture2D MaterialMask(Material material, bool hair, bool metal)
        {
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(material)).Replace('\\', '/');
            string path = $"{folder}/{material.name}_PBRMask.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing) return existing;
            bool hasIlm = material.name is "StarTraveler_T07" or "StarTraveler_Metal";
            string source = hasIlm ? $"{folder}/StarTravelerClothILM.png" : AssetDatabase.GetAssetPath(material.mainTexture);
            var input = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (string.IsNullOrEmpty(source) || !File.Exists(source) || !input.LoadImage(File.ReadAllBytes(source))) return null;
                const int size = 128;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        var hint = input.GetPixelBilinear((x + .5f) / size, (y + .5f) / size);
                        // The source ILM is an art mask. Repack into Danbaidong metal, smoothness, AO, emission order.
                        float detail = hasIlm ? hint.g : hint.grayscale;
                        float smoothness = Mathf.Lerp(hair ? .76f : .59f, .98f, detail);
                        float occlusion = Mathf.Lerp(.85f, 1f, hasIlm ? hint.r : detail);
                        pixels[y * size + x] = new Color(metal ? Mathf.Lerp(.68f, 1f, hint.b) : 0f,
                            smoothness, occlusion, 1f);
                    }
                return Save(path, size, size, pixels);
            }
            finally { Object.DestroyImmediate(input); }
        }

        private static Texture2D Save(string path, int width, int height, Color[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                texture.SetPixels(pixels);
                texture.Apply();
                var data = texture.EncodeToPNG();
                if (File.Exists(path) && data.SequenceEqual(File.ReadAllBytes(path)))
                {
                    var previous = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (previous) return previous;
                }
                File.WriteAllBytes(path, data);
            }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false;
            importer.mipmapEnabled = path.EndsWith("_PBRMask.png", System.StringComparison.Ordinal);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
