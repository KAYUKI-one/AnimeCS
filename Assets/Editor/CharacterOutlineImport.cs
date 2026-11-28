using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AnimeCS.Editor
{
    internal sealed class CharacterOutlineImport : AssetPostprocessor
    {
        internal const int SmoothNormalChannel = 3; // Unity UV4, TEXCOORD3 in the outline pass.

        private void OnPostprocessModel(GameObject model)
        {
            if (!assetPath.StartsWith("Assets/Content/Art/Characters/Ember_Firefly/") &&
                !assetPath.StartsWith("Assets/Content/Art/Characters/Ember_StarTraveler/")) return;
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Bake(skin.sharedMesh);
        }

        // Adapted from Danbaidong SmoothNormal 1989771, MIT (see SmoothNormal-LICENSE.txt).
        internal static void Bake(Mesh mesh)
        {
            if (!mesh) return;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            if (normals.Length != vertices.Length || tangents.Length != vertices.Length) return;
            var angles = new Dictionary<Vector3, Vector3>(vertices.Length);
            var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                var ab = (b - a).normalized;
                var ac = (c - a).normalized;
                var bc = (c - b).normalized;
                var face = Vector3.Cross(ab, ac).normalized;
                Accumulate(a, Vector3.Angle(ab, ac));
                Accumulate(b, Vector3.Angle(-ab, bc));
                Accumulate(c, Vector3.Angle(ac, bc));

                void Accumulate(Vector3 point, float angle)
                {
                    angles.TryGetValue(point, out var sum);
                    angles[point] = sum + face * angle;
                }
            }

            var encoded = new List<Vector3>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                var n = normals[i];
                Vector3 tangent = tangents[i];
                var bitangent = Vector3.Cross(n, tangent) * tangents[i].w;
                if (!angles.TryGetValue(vertices[i], out var sum) || sum.sqrMagnitude < 1e-8f) sum = n;
                var local = new Vector3(Vector3.Dot(tangent, sum), Vector3.Dot(bitangent, sum), Vector3.Dot(n, sum)).normalized;
                encoded.Add(local * .5f + Vector3.one * .5f);
            }
            mesh.SetUVs(SmoothNormalChannel, encoded);
        }
    }
}
