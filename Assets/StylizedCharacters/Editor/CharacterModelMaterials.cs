using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StylizedCharacters.Editor
{
    public static class CharacterModelMaterials
    {
        const string MenuPath = "Assets/Stylized Characters/Prepare Toon Materials";

        [MenuItem(MenuPath,true)]
        static bool CanPrepare() => Selection.objects.OfType<GameObject>().Any(EditorUtility.IsPersistent);

        [MenuItem(MenuPath)]
        static void PrepareSelection()
        {
            foreach (var model in Selection.objects.OfType<GameObject>().Where(EditorUtility.IsPersistent))
                Prepare(model);
        }

        /// <summary>Prepare model-local materials and persist remaps on the source importer.</summary>
        public static GameObject Prepare(GameObject model)
        {
            string path = AssetDatabase.GetAssetPath(model);
            if (!model.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials)
                    .Any(m=>m && !CharacterToonMaterial.IsPrepared(m))) return model;
            string parent = Path.GetDirectoryName(path).Replace('\\','/');
            string folder = Path.GetFileNameWithoutExtension(path) + " Toon Materials";
            if (!path.StartsWith("Assets/"))
            {
                parent="Assets";
                folder+=" "+AssetDatabase.AssetPathToGUID(path).Substring(0,8);
            }
            if (!AssetDatabase.IsValidFolder(parent+"/"+folder)) AssetDatabase.CreateFolder(parent,folder);
            string target=parent+"/"+folder;
            if (path.StartsWith("Assets/") && AssetImporter.GetAtPath(path) is ModelImporter importer)
            {
                RemapImportedModel(importer,model,target);
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            // Scripted importers such as glTFast own their embedded materials. Put overrides on a prefab variant.
            return PrepareVariant(model,target);
        }

        static void RemapImportedModel(ModelImporter importer,GameObject model,string target)
        {
            string path=importer.assetPath;
            var mappings = importer.GetExternalObjectMap();
            var sources = new Dictionary<AssetImporter.SourceAssetIdentifier,Material>();
            foreach (var pair in mappings)
                if (pair.Value is Material linked) sources[pair.Key] = linked;
            foreach (var original in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                sources.TryAdd(new AssetImporter.SourceAssetIdentifier(original),original);
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                foreach (Material used in renderer.sharedMaterials)
                    if (used && !sources.ContainsValue(used)) sources.TryAdd(new AssetImporter.SourceAssetIdentifier(used),used);
            bool changed = false;
            foreach (var pair in sources)
            {
                if (CharacterToonMaterial.IsPrepared(pair.Value)) continue;
                importer.AddRemap(pair.Key,MakeMaterial(pair.Value,pair.Key.name,target));
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
        }

        static GameObject PrepareVariant(GameObject source,string folder)
        {
            string path=folder+"/"+Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(source))+" Toon.prefab";
            var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing && PrefabUtility.GetCorrespondingObjectFromSource(existing)!=source)
                throw new IOException($"Another prefab already exists at {path}.");
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var root=existing ? PrefabUtility.LoadPrefabContents(path) : (GameObject)PrefabUtility.InstantiatePrefab(source,scene);
                try
                {
                    var converted=new Dictionary<Material,Material>();
                    bool changes=false;
                    foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        var slots=renderer.sharedMaterials;
                        bool modified=false;
                        for(int i=0;i<slots.Length;i++)
                        {
                            var original=slots[i];
                            if (!original || CharacterToonMaterial.IsPrepared(original)) continue;
                            if (!converted.TryGetValue(original,out var mat))
                                converted[original]=mat=MakeMaterial(original,original.name,folder);
                            slots[i]=mat; modified=true;
                        }
                        if (!modified) continue;
                        renderer.sharedMaterials=slots;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                        changes=true;
                    }
                    return changes || !existing ? PrefabUtility.SaveAsPrefabAsset(root,path) : existing;
                }
                finally { if(existing) PrefabUtility.UnloadPrefabContents(root); }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static Material MakeMaterial(Material source,string name,string folder)
        {
            var material=new Material(source) { name=name };
            CharacterToonMaterial.Convert(material,CharacterToonMaterial.InferRole(name));
            var invalid=Path.GetInvalidFileNameChars();
            string file=string.Concat(name.Select(c=>invalid.Contains(c)?'_' : c));
            AssetDatabase.CreateAsset(material,AssetDatabase.GenerateUniqueAssetPath(folder+"/"+file+".mat"));
            return material;
        }
    }
}
