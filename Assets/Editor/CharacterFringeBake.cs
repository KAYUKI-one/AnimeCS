using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimeCS.Editor
{
    internal static class CharacterFringeBake
    {
        internal static void Bind(SerializedObject character, string modelPath)
        {
            string name = Path.GetFileNameWithoutExtension(modelPath);
            if (name is not ("Firefly" or "StarTraveler")) return;
            string folder = Path.GetDirectoryName(modelPath).Replace('\\', '/');
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var source = skin.sharedMesh;
            string hair = name == "Firefly" ? "Firefly_T02" : "StarTraveler_T12";
            int slot = System.Array.FindIndex(skin.sharedMaterials, material => material && material.name == hair);
            if (slot < 0) throw new System.InvalidOperationException($"找不到 {name} 的刘海材质。");
            var parts = File.ReadAllText($"{folder}/{name}Face.txt").Split(' ');
            var eye = new Vector3(Scalar(0), Scalar(1), Scalar(2));
            float Scalar(int i) => float.Parse(parts[i], CultureInfo.InvariantCulture);
            var sourceVertices = source.vertices;
            var sourceNormals = source.normals;
            var sourceWeights = source.boneWeights;
            var toModel = model.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var triangles = new List<int>();
            var remap = new Dictionary<int, int>();
            var hairTriangles = source.GetTriangles(slot);
            for (int i = 0; i < hairTriangles.Length; i += 3)
            {
                Vector3 center = (sourceVertices[hairTriangles[i]] + sourceVertices[hairTriangles[i + 1]] + sourceVertices[hairTriangles[i + 2]]) / 3f;
                var relative = toModel.MultiplyPoint3x4(center) - eye;
                if (Mathf.Abs(relative.x) > .125f || relative.y < -.008f || relative.y > .175f || relative.z < -.012f)
                    continue;
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = hairTriangles[i + corner];
                    if (!remap.TryGetValue(index, out int mapped))
                    {
                        mapped = vertices.Count;
                        remap.Add(index, mapped);
                        vertices.Add(sourceVertices[index]);
                        normals.Add(sourceNormals[index]);
                        weights.Add(sourceWeights[index]);
                    }
                    triangles.Add(mapped);
                }
            }
            if (triangles.Count == 0) throw new System.InvalidOperationException($"无法从 {name} 提取刘海投影。");
            var baked = new Mesh { name = name + " Fringe", bindposes = source.bindposes };
            baked.SetVertices(vertices);
            baked.SetNormals(normals);
            baked.boneWeights = weights.ToArray();
            baked.SetTriangles(triangles, 0);
            string path = $"{folder}/{name}Fringe.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh)
            {
                mesh = baked;
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.CopySerialized(baked, mesh);
                Object.DestroyImmediate(baked);
                EditorUtility.SetDirty(mesh);
            }
            character.FindProperty("_fringeMesh").objectReferenceValue = mesh;
            character.FindProperty("_fringeMaterial").objectReferenceValue = Material();
        }

        private static Material Material()
        {
            const string path = "Assets/Content/Art/Characters/LookDevelopment/HairFringe.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            material = new Material(Shader.Find("DanbaidongRP/Helpers/CharacterFringe")) { name = "HairFringe" };
            material.SetFloat("_FriStencilComp", (int)CompareFunction.Equal);
            material.SetFloat("_FriStencilOp", (int)StencilOp.IncrementSaturate);
            material.SetFloat("_ScreenOffsetScaleX", .77f);
            material.SetFloat("_ScreenOffsetScaleY", .81f);
            material.SetFloat("_CamViewDirWorldOffsetScale", .46f);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
