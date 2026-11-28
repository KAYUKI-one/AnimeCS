using System;
using System.IO;
using DanbaidongRP.CharacterPipeline.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimeCS.Editor
{
    internal sealed class CharacterOutlineImport : AssetPostprocessor
    {
        internal const int SmoothNormalChannel = DanbaidongCharacterSetupTool.SmoothNormalUVChannel;

        private void OnPostprocessModel(GameObject model)
        {
            if (!ShouldBakeSmoothNormals(assetPath)) return;
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin.sharedMesh) continue;
                // Write smooth normals into UV4 (SN_SmoothUV4) and also refine Vertex Color RGB to Unity's exact MikkTSpace tangents
                // while preserving any Blender-authored per-part outline width mask in Vertex Color Alpha (SN_VertColor).
                bool hasVertColor = skin.sharedMesh.HasVertexAttribute(VertexAttribute.Color);
                DanbaidongCharacterSetupTool.BakeSmoothNormals(skin.sharedMesh, alsoWriteVertexColorRgb: hasVertColor);
            }
        }

        private static bool ShouldBakeSmoothNormals(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith("Assets/Content/Art/Characters/Ember_Firefly/", StringComparison.Ordinal) ||
                path.StartsWith("Assets/Content/Art/Characters/Ember_StarTraveler/", StringComparison.Ordinal) ||
                path.StartsWith("Assets/Content/Art/Characters/Aegis_Lily/", StringComparison.Ordinal))
                return true;

            // Portable rule: any character folder containing a *FaceSDF.png automatically gets smooth normals baked.
            string folder = Path.GetDirectoryName(path);
            return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) &&
                   Directory.GetFiles(folder, "*FaceSDF.png", SearchOption.TopDirectoryOnly).Length > 0;
        }

        internal static void Bake(Mesh mesh) =>
            DanbaidongCharacterSetupTool.BakeSmoothNormals(mesh, alsoWriteVertexColorRgb: mesh && mesh.HasVertexAttribute(VertexAttribute.Color));
    }
}
