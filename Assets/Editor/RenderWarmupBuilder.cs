using System;
using System.Collections.Generic;
using System.Linq;
using AnimeCS.Play;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AnimeCS.Editor
{
    internal static class RenderWarmupBuilder
    {
        private const string Settings = "Assets/Settings/";
        private static readonly ShaderTagId LightMode = new("LightMode");
        private static readonly PassType[] Untagged =
            { PassType.Normal, PassType.ScriptableRenderPipelineDefaultUnlit, PassType.ScriptableRenderPipeline };
        private static readonly PassType[] Scriptable =
            { PassType.ScriptableRenderPipeline, PassType.ScriptableRenderPipelineDefaultUnlit, PassType.Normal };

        [MenuItem("AnimeCS/渲染/从实机采集生成着色器预热清单")]
        internal static void Rebuild()
        {
            var presentation = AssetDatabase.LoadAssetAtPath<MatchPresentation>("Assets/Content/UI/Competitive.asset");
            presentation.lobbyShaders = Build("LobbyD3D12", "LobbyShaders");
            presentation.matchShaders = Build("DesktopD3D12", "MatchShaders");
            EditorUtility.SetDirty(presentation);
            AssetDatabase.SaveAssetIfDirty(presentation);
        }

        private static ShaderVariantCollection Build(string trace, string name)
        {
            var source = AssetDatabase.LoadAssetAtPath<GraphicsStateCollection>(Settings + trace + ".graphicsstate");
            if (!source) throw new InvalidOperationException("缺少实机渲染采集 " + trace);
            var recorded = new List<GraphicsStateCollection.ShaderVariant>();
            source.GetVariants(recorded);
            var result = new ShaderVariantCollection { name = name };
            try
            {
                foreach (var variant in recorded)
                {
                    var shader = variant.shader;
                    // Unity's native framebuffer clear has no material pass to store in an SVC.
                    if (shader.name == "Hidden/InternalClear") continue;
                    var pass = variant.passId;
                    string mode = shader.FindPassTagValue((int)pass.SubshaderIndex, (int)pass.PassIndex, LightMode).name;
                    string[] keywords = variant.keywords.Select(k => k.name).ToArray();
                    if (AddPass(result, shader, mode, keywords)) continue;
                    var allowed = new HashSet<string>(ShaderUtil.GetPassKeywords(shader, pass).Select(k => k.name));
                    if (!AddPass(result, shader, mode, keywords.Where(allowed.Contains).ToArray()))
                        throw new InvalidOperationException("无法生成预热变体 " + shader.name + " / " + mode);
                }
                if (result.variantCount == 0) throw new InvalidOperationException("采集没有着色器变体 " + trace);
                string path = Settings + name + ".shadervariants";
                var asset = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(path);
                if (!asset)
                {
                    AssetDatabase.CreateAsset(result, path);
                    return result;
                }
                EditorUtility.CopySerialized(result, asset);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
                return asset;
            }
            finally
            {
                if (!EditorUtility.IsPersistent(result)) Object.DestroyImmediate(result);
            }
        }

        private static bool AddPass(ShaderVariantCollection result, Shader shader, string mode, string[] keywords)
        {
            if (Enum.TryParse(mode, true, out PassType builtIn) && Add(result, shader, builtIn, keywords)) return true;
            foreach (var pass in string.IsNullOrEmpty(mode) ? Untagged : Scriptable)
                if (Add(result, shader, pass, keywords)) return true;
            return false;
        }

        private static bool Add(ShaderVariantCollection result, Shader shader, PassType pass, string[] keywords)
        {
            try { result.Add(new ShaderVariantCollection.ShaderVariant(shader, pass, keywords)); }
            catch (ArgumentException) { return false; }
            return true;
        }
    }
}
