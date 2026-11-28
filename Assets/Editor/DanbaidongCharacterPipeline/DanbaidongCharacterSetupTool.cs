using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace DanbaidongRP.CharacterPipeline.Editor
{
    /// <summary>
    /// Portable 1-click setup tool for ANY anime character FBX in ANY Unity 6 + DanbaidongRP project.
    /// No project-specific dependencies: drop Assets/Editor/DanbaidongCharacterPipeline/ into another Unity project
    /// and run DanbaidongRP/Character Setup/Prepare Selected Character FBX.
    /// </summary>
    public static class DanbaidongCharacterSetupTool
    {
        public const int SmoothNormalUVChannel = 3; // Unity UV4 -> TEXCOORD3 (SN_SmoothUV4)

        [MenuItem("DanbaidongRP/Character Setup/Prepare Selected Character FBX")]
        public static void PrepareSelectedCharacter()
        {
            var selected = Selection.activeObject;
            string path = selected ? AssetDatabase.GetAssetPath(selected) : null;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog(
                    "DanbaidongRP Character Setup",
                    "Please select a character .fbx asset in the Project window first.",
                    "OK");
                return;
            }

            PrepareCharacterModel(path, forceRegenerateMaps: true);
            AssetDatabase.SaveAssets();
            Debug.Log($"[DanbaidongRP] Prepared character '{path}' with PBRToon materials, smooth normals, and fringe shadow.");
        }

        [MenuItem("DanbaidongRP/Character Setup/Bake Smooth Normals On Mesh (Selected FBX)")]
        public static void ReimportSelectedWithSmoothNormals()
        {
            var selected = Selection.activeObject;
            string path = selected ? AssetDatabase.GetAssetPath(selected) : null;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>
        /// End-to-end portable setup for an anime character FBX:
        /// 1) Ensures normals=Import and tangents=CalculateMikk on the ModelImporter.
        /// 2) Extracts/remaps material slots and applies DanbaidongRP PBRToon presets + Ramps + HairSpec + PBRMasks + DetailNormals.
        /// 3) Extracts a front-bangs Fringe Shadow mesh + HairFringe.mat when a Face.txt anchor exists.
        /// </summary>
        public static void PrepareCharacterModel(
            string fbxPath,
            string lookDevFolder = DanbaidongLookMaps.DefaultLookDevFolder,
            Func<string, DanbaidongSurfaceRole?> roleResolver = null,
            bool forceRegenerateMaps = false)
        {
            if (AssetImporter.GetAtPath(fbxPath) is not ModelImporter importer)
                throw new InvalidOperationException($"Not a valid ModelImporter: {fbxPath}");

            bool importerChanged = false;
            if (importer.importNormals != ModelImporterNormals.Import)
            {
                importer.importNormals = ModelImporterNormals.Import;
                importerChanged = true;
            }
            if (importer.importTangents != ModelImporterTangents.CalculateMikk)
            {
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importerChanged = true;
            }
            if (importerChanged) importer.SaveAndReimport();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (!prefab) throw new InvalidOperationException($"Failed to load model asset: {fbxPath}");

            string folder = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
            string actor = Path.GetFileNameWithoutExtension(fbxPath);

            var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            bool hasVertColors = skin && skin.sharedMesh && skin.sharedMesh.HasVertexAttribute(VertexAttribute.Color);

            var existingRemaps = importer.GetExternalObjectMap();
            var materials = prefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials)
                .Where(m => m)
                .Distinct()
                .ToArray();

            bool remapDirty = false;
            foreach (var original in materials)
            {
                string matPath = $"{folder}/{original.name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (!mat)
                {
                    mat = new Material(Shader.Find("DanbaidongRP/PBRToon/Base")) { name = original.name };
                    var tex = FindAlbedoTexture(folder, original.name, actor);
                    if (tex) mat.mainTexture = tex;
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                else if (!mat.mainTexture)
                {
                    var tex = FindAlbedoTexture(folder, original.name, actor);
                    if (tex) mat.mainTexture = tex;
                }

                var role = roleResolver?.Invoke(original.name) ?? DanbaidongMaterialPreset.InferRole(original.name);
                DanbaidongMaterialPreset.Apply(mat, role, new DanbaidongApplyOptions
                {
                    ActorName = actor,
                    LookDevFolder = lookDevFolder,
                    UseVertexColorOutline = hasVertColors,
                    EnableEyeSeeThrough = true,
                    ForceRegenerateMaps = forceRegenerateMaps
                });

                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), original.name);
                if (!existingRemaps.TryGetValue(id, out var current) || current != mat)
                {
                    importer.AddRemap(id, mat);
                    remapDirty = true;
                }
            }

            if (remapDirty) importer.SaveAndReimport();

            // Extract Fringe Shadow mesh if Face.txt exists in folder
            string faceTxt = $"{folder}/{actor}Face.txt";
            if (File.Exists(faceTxt))
            {
                BakeFringeMesh(fbxPath, lookDevFolder);
            }
        }

        /// <summary>
        /// Bakes angle-weighted tangent-space smooth normals into BOTH UV4 (SN_SmoothUV4) and Vertex Color RGB (SN_VertColor),
        /// while preserving any Blender-authored outline width mask stored in Vertex Color Alpha!
        /// Because this runs inside Unity using Unity's actual MikkTSpace tangents, handedness is 100% guaranteed to match CharacterOutline.shader.
        /// </summary>
        public static void BakeSmoothNormals(Mesh mesh, bool alsoWriteVertexColorRgb = true)
        {
            if (!mesh) return;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            if (vertices.Length == 0 || normals.Length != vertices.Length || tangents.Length != vertices.Length) return;

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

            var existingColors = mesh.colors;
            bool hasColors = existingColors != null && existingColors.Length == vertices.Length;
            var newColors = alsoWriteVertexColorRgb ? new Color[vertices.Length] : null;
            var encodedUV4 = new List<Vector3>(vertices.Length);

            for (int i = 0; i < vertices.Length; i++)
            {
                var n = normals[i];
                Vector3 tangent = tangents[i];
                var bitangent = Vector3.Cross(n, tangent) * tangents[i].w;
                if (!angles.TryGetValue(vertices[i], out var sum) || sum.sqrMagnitude < 1e-8f) sum = n;
                var local = new Vector3(
                    Vector3.Dot(tangent, sum),
                    Vector3.Dot(bitangent, sum),
                    Vector3.Dot(n, sum)).normalized;
                var enc = local * 0.5f + Vector3.one * 0.5f;
                encodedUV4.Add(enc);

                if (newColors != null)
                {
                    // Preserve Blender-authored alpha mask, but ensure Face silhouette isn't zeroed out if alpha == 0
                    float alpha = hasColors ? existingColors[i].a : 1f;
                    newColors[i] = new Color(enc.x, enc.y, enc.z, alpha);
                }
            }

            mesh.SetUVs(SmoothNormalUVChannel, encodedUV4);
            if (newColors != null) mesh.colors = newColors;
        }

        /// <summary>
        /// Portable front-bangs Fringe Shadow extractor. Automatically finds front-hair submesh(es)
        /// and extracts triangles near the forehead around the Face.txt eye anchor.
        /// </summary>
        public static (Mesh fringeMesh, Material fringeMaterial) BakeFringeMesh(
            string modelPath,
            string lookDevFolder = DanbaidongLookMaps.DefaultLookDevFolder,
            string explicitHairMaterialName = null)
        {
            string name = Path.GetFileNameWithoutExtension(modelPath);
            string folder = Path.GetDirectoryName(modelPath).Replace('\\', '/');
            string faceTxt = $"{folder}/{name}Face.txt";
            if (!File.Exists(faceTxt)) return (null, null);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var skin = model ? model.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (!skin || !skin.sharedMesh) return (null, null);

            var source = skin.sharedMesh;
            var candidateSlots = new List<int>();
            if (!string.IsNullOrEmpty(explicitHairMaterialName))
            {
                int slot = Array.FindIndex(skin.sharedMaterials, m => m && m.name == explicitHairMaterialName);
                if (slot >= 0) candidateSlots.Add(slot);
            }
            else
            {
                for (int i = 0; i < skin.sharedMaterials.Length; i++)
                {
                    var m = skin.sharedMaterials[i];
                    if (m && (m.shader.name == "DanbaidongRP/PBRToon/Hair" ||
                              DanbaidongMaterialPreset.InferRole(m.name) == DanbaidongSurfaceRole.Hair))
                    {
                        candidateSlots.Add(i);
                    }
                }
            }

            if (candidateSlots.Count == 0) return (null, null);

            var parts = File.ReadAllText(faceTxt).Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return (null, null);
            var eye = new Vector3(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture));

            var sourceVertices = source.vertices;
            var sourceNormals = source.normals;
            var sourceWeights = source.boneWeights;
            var toModel = model.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var triangles = new List<int>();
            var remap = new Dictionary<int, int>();

            foreach (int slot in candidateSlots)
            {
                if (slot >= source.subMeshCount) continue;
                var hairTriangles = source.GetTriangles(slot);
                for (int i = 0; i < hairTriangles.Length; i += 3)
                {
                    Vector3 center = (sourceVertices[hairTriangles[i]] +
                                      sourceVertices[hairTriangles[i + 1]] +
                                      sourceVertices[hairTriangles[i + 2]]) / 3f;
                    var relative = toModel.MultiplyPoint3x4(center) - eye;
                    if (Mathf.Abs(relative.x) > 0.13f || relative.y < -0.012f || relative.y > 0.18f || relative.z < -0.015f)
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
            }

            if (triangles.Count == 0) return (null, null);

            var baked = new Mesh { name = name + " Fringe", bindposes = source.bindposes };
            baked.SetVertices(vertices);
            baked.SetNormals(normals);
            baked.boneWeights = weights.ToArray();
            baked.SetTriangles(triangles, 0);

            string meshPath = $"{folder}/{name}Fringe.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh)
            {
                mesh = baked;
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            else
            {
                EditorUtility.CopySerialized(baked, mesh);
                Object.DestroyImmediate(baked);
                EditorUtility.SetDirty(mesh);
            }

            return (mesh, GetOrCreateFringeMaterial(lookDevFolder));
        }

        public static Material GetOrCreateFringeMaterial(string lookDevFolder = DanbaidongLookMaps.DefaultLookDevFolder)
        {
            string path = $"{lookDevFolder}/HairFringe.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                var shader = Shader.Find("DanbaidongRP/Helpers/CharacterFringe");
                if (!shader) throw new InvalidOperationException("Shader 'DanbaidongRP/Helpers/CharacterFringe' not found.");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                material = new Material(shader) { name = "HairFringe" };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetFloat("_FriStencilComp", (int)CompareFunction.Equal);
            material.SetFloat("_FriStencilOp", (int)StencilOp.IncrementSaturate);
            material.SetFloat("_ScreenOffsetScaleX", 0.60f);
            material.SetFloat("_ScreenOffsetScaleY", 0.65f);
            material.SetFloat("_CamViewDirWorldOffsetScale", 0.45f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D FindAlbedoTexture(string folder, string materialName, string actor)
        {
            string direct = $"{folder}/{materialName}.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(direct);
            if (tex) return tex;

            // Split sub-materials (e.g. Lily_Lash -> Lily_T01, Lily_Skin/Lily_Stockings -> Lily_T03)
            if (materialName.EndsWith("_Lash", StringComparison.OrdinalIgnoreCase))
            {
                var faceMat = AssetDatabase.LoadAssetAtPath<Material>($"{folder}/{actor}_T01.mat");
                if (faceMat && faceMat.mainTexture is Texture2D faceTex) return faceTex;
            }
            if (materialName.EndsWith("_Skin", StringComparison.OrdinalIgnoreCase) ||
                materialName.EndsWith("_Stockings", StringComparison.OrdinalIgnoreCase))
            {
                var bodyMat = AssetDatabase.LoadAssetAtPath<Material>($"{folder}/{actor}_T03.mat");
                if (bodyMat && bodyMat.mainTexture is Texture2D bodyTex) return bodyTex;
            }
            return null;
        }
    }
}
