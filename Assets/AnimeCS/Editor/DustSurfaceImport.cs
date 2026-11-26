using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AnimeCS.Editor
{
    public static class DustSurfaceImport
    {
        private const string Art="Assets/AnimeCS/Art/Maps/DustII/";
        private const string Asset="Assets/AnimeCS/Content/Maps/DustII.prefab";
        private const string Materials=Art+"SurfaceMaterials";

        [Serializable] private class WorldFile {public ArtData extras;}
        [Serializable] private class ArtData {public Surface[] paintedSurfaces,staticSurfaces;}
        [Serializable] private class Surface
        {
            public string name,baseMap,normal,orm,layer,surface,mask;
            public float[] baseTint,layerTint,colorUV,normalUV,maskUV;
            public int blendMode;
            public float softness;
        }

        [MenuItem("AnimeCS/美术/同步沙城分层材质与枪械表面")]
        public static void Synchronize()
        {
            var art=JsonUtility.FromJson<WorldFile>(File.ReadAllText(Art+"DustII.gltf")).extras;
            if(art?.paintedSurfaces==null || art.paintedSurfaces.Length==0)throw new InvalidDataException("地图缺少顶点涂层，请先运行 dust_surfaces.py。");
            var shader=Shader.Find("AnimeCS/Painted Masonry");
            if(!shader || !shader.isSupported)throw new InvalidOperationException("分层材质着色器不可用。");
            ContentLibrary.EnsureFolder(Materials);
            SetWorldFiltering(Art+"DustII.gltf",12);
            SetWorldFiltering(Art+"Horizon/Horizon.gltf",8);
            ConfigureTextures();
            var table=new Dictionary<string,Surface>(StringComparer.Ordinal);
            foreach(var recipe in art.paintedSurfaces.Concat(art.staticSurfaces??Array.Empty<Surface>()))table[recipe.name]=recipe;
            var library=new Dictionary<Material,Material>();
            // Read original glTF material slots even if this prefab already has an earlier material override.
            var root=PrefabUtility.LoadPrefabContents(Asset);
            try
            {
                var horizon = root.transform.Find("Horizon");
                if(horizon) ApplyHorizonGround(horizon.gameObject);
                int count=0;
                foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var original=PrefabUtility.GetCorrespondingObjectFromOriginalSource(r);
                    if(!original || AssetDatabase.GetAssetPath(original)!=Art+"DustII.gltf")continue;
                    var sourceSlots=original.sharedMaterials;
                    var slots=r.sharedMaterials;
                    if(sourceSlots.Length!=slots.Length)continue;
                    bool painted=false;
                    for(int i=0;i<slots.Length;i++)
                    {
                        Material mat=sourceSlots[i];
                        if(!mat || mat.renderQueue>2449 || !table.TryGetValue(mat.name,out var recipe))continue;
                        if(!library.TryGetValue(mat,out var value))
                        {
                            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mat,out string _,out long id);
                            string path=Materials+"/"+recipe.name+"_"+id+".mat";
                            value=AssetDatabase.LoadAssetAtPath<Material>(path);
                            if(!value){value=new Material(shader);AssetDatabase.CreateAsset(value,path);}
                            value.shader=shader;
                            var tint=mat.HasProperty("baseColorFactor")?mat.GetColor("baseColorFactor"):Color.white;
                            value.SetColor("_BaseTint",tint*Tint(recipe.baseTint));
                            bool layered=recipe.blendMode>=0;
                            value.SetFloat("_Layered",layered?1:0);
                            if(layered)value.EnableKeyword("_PAINTED");else value.DisableKeyword("_PAINTED");
                            value.SetFloat("_MetalFactor",string.IsNullOrEmpty(recipe.orm)?0f:mat.HasProperty("metallicFactor")?mat.GetFloat("metallicFactor"):1f);
                            value.SetFloat("_RoughFactor",mat.HasProperty("roughnessFactor")?mat.GetFloat("roughnessFactor"):1f);
                            if(layered)value.SetColor("_LayerTint",tint*Tint(recipe.layerTint));
                            Bind(value,"_BaseMap",recipe.baseMap);Bind(value,"_BumpMap",recipe.normal);
                            Bind(value,"_ORM",recipe.orm);Bind(value,"_Layer",recipe.layer);
                            Bind(value,"_LayerSurface",recipe.surface);Bind(value,"_BlendMask",recipe.mask);
                            value.SetFloat("_PaintMode",recipe.blendMode);value.SetFloat("_Softness",recipe.softness);
                            if(layered)
                            {
                                TransformUV(value,"Color",recipe.colorUV);TransformUV(value,"Normal",recipe.normalUV);TransformUV(value,"Mask",recipe.maskUV);
                            }
                            EditorUtility.SetDirty(value);library.Add(mat,value);
                        }
                        slots[i]=value;painted=true;count++;
                    }
                    if(!painted)continue;
                    r.sharedMaterials=slots;PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                }
                if(count==0)throw new InvalidDataException("找不到对应的沙城原始材质槽位。");
                PrefabUtility.SaveAsPrefabAsset(root,Asset);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            ApplyRifle();
            AssetDatabase.SaveAssets();
        }

        internal static void ApplyHorizonGround(GameObject horizon)
        {
            const string path = Art + "Horizon/Ground.mat";
            var shader = Shader.Find("AnimeCS/Horizon Ground");
            if(!shader || !shader.isSupported) throw new InvalidOperationException("远景沙地着色器不可用。");
            var ground = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool found = false;
            foreach(var renderer in horizon.GetComponentsInChildren<MeshRenderer>(true))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(renderer);
                if(!source) continue;
                var original = source.sharedMaterials;
                var slots = renderer.sharedMaterials;
                bool changed = false;
                for(int i = 0; i < original.Length && i < slots.Length; i++)
                {
                    var material = original[i];
                    if(!material || material.name != "hr_dust_ground_sand_skybox") continue;
                    if(!ground)
                    {
                        ground = new Material(shader);
                        AssetDatabase.CreateAsset(ground, path);
                    }
                    if(!found)
                    {
                        ground.shader = shader;
                        ground.mainTexture = material.mainTexture;
                        ground.color = material.GetColor("baseColorFactor");
                        EditorUtility.SetDirty(ground);
                    }
                    found = true;
                    if(slots[i] == ground) continue;
                    slots[i] = ground;
                    changed = true;
                }
                if(changed)
                {
                    renderer.sharedMaterials = slots;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }
            if(!found) throw new InvalidDataException("天际场景中找不到原始的背景沙地。");
        }

        private static void Bind(Material m,string property,string path)
        {
            if(string.IsNullOrEmpty(path))return;
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+path);
            if(!texture)throw new FileNotFoundException(Art+path);
            m.SetTexture(property,texture);
        }
        private static Color Tint(float[] c)=>new(c[0],c[1],c[2],1);
        private static void TransformUV(Material m,string prefix,float[] t)
        {
            m.SetVector("_"+prefix+"U",new Vector4(t[0],t[1],t[2],0));
            m.SetVector("_"+prefix+"V",new Vector4(t[3],t[4],t[5],0));
        }
        private static void SetWorldFiltering(string path,int aniso)
        {
            var importer=AssetImporter.GetAtPath(path);
            if(!importer)return;
            var settings=new SerializedObject(importer);
            var value=settings.FindProperty("importSettings.anisotropicFilterLevel");
            if(value==null || value.intValue==aniso)return;
            value.intValue=aniso;settings.ApplyModifiedPropertiesWithoutUndo();importer.SaveAndReimport();
        }
        private static void ConfigureTextures()
        {
            foreach(string path in Directory.GetFiles(Art+"SurfaceLayers","*.png"))
            {
                var p=path.Replace('\\','/');
                bool srgb=p.EndsWith("_albedo.png",StringComparison.Ordinal);
                var importer=(TextureImporter)AssetImporter.GetAtPath(p);
                if(importer.sRGBTexture==srgb && importer.anisoLevel==12 && importer.filterMode==FilterMode.Trilinear && importer.mipmapEnabled)continue;
                importer.sRGBTexture=srgb;importer.mipmapEnabled=true;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=12;
                importer.alphaIsTransparency=false;importer.textureCompression=TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }
        private static void ApplyRifle()
        {
            const string root="Assets/AnimeCS/Art/Weapons/Ember47/Ember47";
            var data=(TextureImporter)AssetImporter.GetAtPath(root+"Surface.png");
            if(!data)throw new FileNotFoundException("缺少枪械表面图，请运行 rifle_finish.py。");
            if(data.sRGBTexture || data.anisoLevel<8)
            {
                data.sRGBTexture=false;data.alphaIsTransparency=false;data.filterMode=FilterMode.Trilinear;
                data.anisoLevel=8;data.SaveAndReimport();
            }
            var mat=AssetDatabase.LoadAssetAtPath<Material>(root+"_T00.mat");
            var color=AssetDatabase.LoadAssetAtPath<Texture2D>(root+"Albedo.png");
            mat.SetTexture("_BaseMap",color);mat.SetTexture("_MainTex",color);
            mat.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(root+"Surface.png"));
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");mat.SetFloat("_Smoothness",1);EditorUtility.SetDirty(mat);
        }
    }
}
