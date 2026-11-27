using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StylizedCharacters.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimeCS.Tests
{
    public sealed class CharacterModelMaterialsTests
    {
        private const string FbxFixture = "Assets/Content/Art/Wearables/TeamMarks/RoverTeamPatch.fbx";
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            var name = "AnimeCS_ToonImportTest_" + Guid.NewGuid().ToString("N");
            _folder = "Assets/" + name;
            Assert.That(AssetDatabase.CreateFolder("Assets", name), Is.Not.Empty);
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(_folder))
                AssetDatabase.DeleteAsset(_folder);
        }

        [Test]
        public void PreparePrefabCreatesVariantAndReusesItsArtistEditedMaterial()
        {
            var sourceMaterial = CreateSourceMaterial("Hair");
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject source;
            try
            {
                var root = new GameObject("Character");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.transform.SetParent(root.transform);
                body.GetComponent<Renderer>().sharedMaterial = sourceMaterial;
                var inactive = Object.Instantiate(body, root.transform);
                inactive.SetActive(false);
                source = PrefabUtility.SaveAsPrefabAsset(root, _folder + "/Character.prefab");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }

            var variant = CharacterModelMaterials.Prepare(source);
            Assert.That(variant, Is.Not.Null);
            Assert.That(PrefabUtility.GetPrefabAssetType(variant), Is.EqualTo(PrefabAssetType.Variant));
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(variant), Is.EqualTo(source));
            Assert.That(Materials(source), Is.All.EqualTo(sourceMaterial));
            Assert.That(CharacterToonMaterial.IsPrepared(sourceMaterial), Is.False);

            var slots = Materials(variant);
            Assert.That(slots, Has.Length.EqualTo(2));
            Assert.That(slots[1], Is.EqualTo(slots[0]));
            Assert.That(CharacterToonMaterial.IsPrepared(slots[0]), Is.True);
            AssertColorPreserved(slots[0].color, sourceMaterial.color);
            var variantPath = AssetDatabase.GetAssetPath(variant);
            var materialPath = AssetDatabase.GetAssetPath(slots[0]);
            Assert.That(materialPath, Does.StartWith(_folder + "/Character Toon Materials/"));
            SaveArtistEdit(slots[0]);

            Assert.That(AssetDatabase.GetAssetPath(CharacterModelMaterials.Prepare(source)), Is.EqualTo(variantPath));
            Assert.That(AssetDatabase.GetAssetPath(CharacterModelMaterials.Prepare(variant)), Is.EqualTo(variantPath));
            Assert.That(GeneratedMaterialPaths("Character Toon Materials"), Is.EqualTo(new[] {materialPath}));
            var persisted = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
            Assert.That(Materials(persisted).Select(AssetDatabase.GetAssetPath), Is.All.EqualTo(materialPath));
            Assert.That(Materials(persisted)[0].GetFloat("_ShadowStrength"), Is.EqualTo(.41f).Within(.001f));
        }

        [Test]
        public void PrepareFbxPersistsImporterRemapAndReusesAnArtistEditedMaterial()
        {
            var path = _folder + "/Character.fbx";
            File.Copy(FbxFixture, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var embedded = Materials(source).Where(material => AssetDatabase.GetAssetPath(material) == path).Distinct().ToArray();
            Assert.That(embedded, Has.Length.EqualTo(1));
            var sourceIdentifier = new AssetImporter.SourceAssetIdentifier(embedded[0]);
            var external = CreateSourceMaterial("Original remap");
            importer.AddRemap(sourceIdentifier, external);
            importer.SaveAndReimport();
            source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(Materials(source), Does.Contain(external));

            var prepared = CharacterModelMaterials.Prepare(source);
            Assert.That(AssetDatabase.GetAssetPath(prepared), Is.EqualTo(path));
            Assert.That(PrefabUtility.GetPrefabAssetType(prepared), Is.EqualTo(PrefabAssetType.Model));
            var remap = ReadMaterialRemap(path, sourceIdentifier);
            Assert.That(Materials(prepared), Does.Contain(remap));
            Assert.That(CharacterToonMaterial.IsPrepared(remap), Is.True);
            Assert.That(CharacterToonMaterial.IsPrepared(external), Is.False);
            AssertColorPreserved(remap.color, external.color);
            var materialPath = AssetDatabase.GetAssetPath(remap);
            Assert.That(materialPath, Does.StartWith(_folder + "/Character Toon Materials/"));
            SaveArtistEdit(remap);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            prepared = CharacterModelMaterials.Prepare(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var persistedRemap = ReadMaterialRemap(path, sourceIdentifier);
            Assert.That(AssetDatabase.GetAssetPath(persistedRemap), Is.EqualTo(materialPath));
            Assert.That(Materials(prepared), Does.Contain(persistedRemap));
            Assert.That(GeneratedMaterialPaths("Character Toon Materials"), Is.EqualTo(new[] {materialPath}));
            Assert.That(persistedRemap.GetFloat("_ShadowStrength"), Is.EqualTo(.41f).Within(.001f));
        }

        private Material CreateSourceMaterial(string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader) { name = name, color = new Color(.42f, .31f, .23f, 1) };
            AssetDatabase.CreateAsset(material, _folder + "/" + name + ".mat");
            return material;
        }

        private static Material[] Materials(GameObject model) => model.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials).Where(material => material).ToArray();

        private string[] GeneratedMaterialPaths(string relativeFolder) => AssetDatabase
            .FindAssets("t:Material", new[] {_folder + "/" + relativeFolder}).Select(AssetDatabase.GUIDToAssetPath).ToArray();

        private static Material ReadMaterialRemap(string path, AssetImporter.SourceAssetIdentifier sourceIdentifier)
        {
            var mappings = AssetImporter.GetAtPath(path).GetExternalObjectMap();
            Assert.That(mappings.TryGetValue(sourceIdentifier, out var material), Is.True);
            Assert.That(material, Is.TypeOf<Material>());
            return (Material)material;
        }

        private static void SaveArtistEdit(Material material)
        {
            material.SetFloat("_ShadowStrength", .41f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }

        private static void AssertColorPreserved(Color actual, Color expected)
        {
            // Material serialization can round the least significant digits.
            const float serializationTolerance = .00001f;
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(serializationTolerance), "red");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(serializationTolerance), "green");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(serializationTolerance), "blue");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(serializationTolerance), "alpha");
        }
    }
}
