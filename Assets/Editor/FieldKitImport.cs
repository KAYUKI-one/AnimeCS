using System.Collections.Generic;
using System.Linq;
using AnimeCS.Content;
using AnimeCS.Play;
using UnityEditor;
using UnityEngine;

namespace AnimeCS.Editor
{
    internal static class FieldKitImport
    {
        private const string Root="Assets/Content/Art/FieldKit/";
        private static T Load<T>(string path) where T:Object=>AssetDatabase.LoadAssetAtPath<T>(Root+path);
        private static readonly Dictionary<string,string> Colors = new()
        {
            {"Basalt","#424D60"},{"Titanium","#92A9BC"},{"Ceramic","#DCDFED"},{"Copper","#C88F77"},
            {"Petrol","#4D8A93"},{"Amber","#FFBE70"},{"Glacier","#8ADDE2"},{"Violet","#B49CE0"},
            {"BlackGlass","#323C53"},{"Ivory","#F2E6D9"}
        };

        private static void ApplyColor(Material finish, string name, string html)
        {
            ColorUtility.TryParseHtmlString(html,out var color);
            finish.SetColor("_BaseColor",color);
            bool metal = name is "Titanium" or "Copper";
            finish.SetFloat("_Metallic",metal ? .46f : name == "Petrol" ? .24f : .08f);
            finish.SetFloat("_Smoothness",name == "BlackGlass" ? .58f : metal ? .43f : .29f);
            if (name is "Glacier" or "Amber" or "Violet")
            {
                finish.EnableKeyword("_EMISSION");
                finish.SetColor("_EmissionColor",color*.46f);
            }
            EditorUtility.SetDirty(finish);
        }

        [MenuItem("AnimeCS/美术/应用晴空中庭道具配色")]
        public static void ApplyCourtyardPalette()
        {
            foreach (var pair in Colors)
            {
                var finish = Load<Material>("Materials/"+pair.Key+".mat");
                if (finish) ApplyColor(finish, pair.Key, pair.Value);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("AnimeCS/美术/同步科幻道具与声音")]
        internal static void Import()
        {
            foreach(string prop in new[]{"Smoke","Flash","Echo","ObjectiveCase","RecoveryKit"})
            {
                string path=Root+"Models/"+prop+".fbx";
                var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var sources=model.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct().ToArray();
                bool changed=false;
                foreach(var source in sources)
                {
                    string name=source.name;
                    if(!Colors.TryGetValue(name,out var html))continue;
                    var finish=Load<Material>("Materials/"+name+".mat");
                    if(!finish)
                    {
                        finish=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};
                        ApplyColor(finish,name,html);
                        AssetDatabase.CreateAsset(finish,Root+"Materials/"+name+".mat");
                    }
                    if(source==finish)continue;
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),name),finish);changed=true;
                }
                if(changed)importer.SaveAndReimport();
                if(prop=="RecoveryKit")continue;
                string prefab="Assets/Content/Prefabs/"+prop+".prefab";
                var root=PrefabUtility.LoadPrefabContents(prefab);
                try
                {
                    for(int i=root.transform.childCount-1;i>=0;i--)Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                    var child=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),root.transform);
                    child.name="Shell";PrefabUtility.SaveAsPrefabAsset(root,prefab);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            var catalog=ContentLibrary.Catalog;
            var smoke=catalog.Find<UtilityDefinition>("utility.smoke");
            var smokeData=new SerializedObject(smoke);
            var puff=Load<Material>("Materials/SmokePuffs.mat");
            if(!puff){puff=new Material(Shader.Find("AnimeCS/Smoke puffs"));AssetDatabase.CreateAsset(puff,Root+"Materials/SmokePuffs.mat");}
            puff.SetTexture("_BaseMap",Load<Texture2D>("Effects/Puff.png"));EditorUtility.SetDirty(puff);
            smokeData.FindProperty("_cloud").objectReferenceValue=puff;smokeData.ApplyModifiedPropertiesWithoutUndo();
            var blast=Load<Material>("Materials/DeviceBlast.mat");
            var burstShader=Shader.Find("AnimeCS/Device burst");
            if(!blast){blast=new Material(burstShader);AssetDatabase.CreateAsset(blast,Root+"Materials/DeviceBlast.mat");}
            blast.shader=burstShader;blast.renderQueue=(int)UnityEngine.Rendering.RenderQueue.Transparent;
            blast.SetTexture("_BaseMap",Load<Texture2D>("Effects/Blast.png"));EditorUtility.SetDirty(blast);
            var style=AssetDatabase.LoadAssetAtPath<MatchPresentation>("Assets/Content/UI/Competitive.asset");
            AudioClip Clip(string key)=>AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Content/Audio/FieldKit/"+key);
            style.shotSound=Clip("rifle_01.wav");style.alternateRifleSound=Clip("rifle_02.wav");style.suppressedSound=Clip("suppressed_01.wav");
            style.pistolSound=Clip("pistol_01.wav");style.marksmanSound=Clip("marksman_01.wav");style.deviceBurstSound=Clip("DeviceBurst.ogg");
            style.bootSteps=Enumerable.Range(0,5).Select(i=>Clip("Concrete_"+i+".ogg")).ToArray();style.deviceBlast=blast;
            style.recoveryKit=Load<GameObject>("Models/RecoveryKit.fbx");
            EditorUtility.SetDirty(style);AssetDatabase.SaveAssets();
        }
    }
}
