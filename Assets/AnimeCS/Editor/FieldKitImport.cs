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
        private const string Root="Assets/AnimeCS/Art/FieldKit/";
        private static T Load<T>(string path) where T:Object=>AssetDatabase.LoadAssetAtPath<T>(Root+path);
        [MenuItem("AnimeCS/美术/同步科幻道具与声音")]
        internal static void Import()
        {
            var finishes=new Dictionary<string,string>
            {
                {"Basalt","#333D48"},{"Titanium","#869195"},{"Ceramic","#CDD0C1"},{"Copper","#BE8254"},
                {"Petrol","#256C71"},{"Amber","#FFC04F"},{"Glacier","#78EFF0"},{"Violet","#9978CE"},
                {"BlackGlass","#26343B"},{"Ivory","#DCD9CE"}
            };
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
                    if(!finishes.TryGetValue(name,out var html))continue;
                    var finish=Load<Material>("Materials/"+name+".mat");
                    if(!finish)
                    {
                        finish=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};
                        ColorUtility.TryParseHtmlString(html,out var color);finish.SetColor("_BaseColor",color);
                        bool metal=name is "Titanium" or "Copper";
                        finish.SetFloat("_Metallic",metal?.78f:name=="Petrol"?.42f:.12f);
                        finish.SetFloat("_Smoothness",metal?.56f:name=="BlackGlass"?.76f:.38f);
                        if(name is "Glacier" or "Amber" or "Violet"){finish.EnableKeyword("_EMISSION");finish.SetColor("_EmissionColor",color*.58f);}
                        AssetDatabase.CreateAsset(finish,Root+"Materials/"+name+".mat");
                    }
                    if(source==finish)continue;
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),name),finish);changed=true;
                }
                if(changed)importer.SaveAndReimport();
                if(prop=="RecoveryKit")continue;
                string prefab="Assets/AnimeCS/Content/Models/"+prop+".prefab";
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
            var style=AssetDatabase.LoadAssetAtPath<MatchPresentation>("Assets/AnimeCS/UI/Competitive.asset");
            AudioClip Clip(string key)=>Load<AudioClip>("Audio/"+key);
            style.shotSound=Clip("rifle_01.wav");style.alternateRifleSound=Clip("rifle_02.wav");style.suppressedSound=Clip("suppressed_01.wav");
            style.pistolSound=Clip("pistol_01.wav");style.marksmanSound=Clip("marksman_01.wav");style.deviceBurstSound=Clip("DeviceBurst.ogg");
            style.bootSteps=Enumerable.Range(0,5).Select(i=>Clip("Concrete_"+i+".ogg")).ToArray();style.deviceBlast=blast;
            style.recoveryKit=Load<GameObject>("Models/RecoveryKit.fbx");
            EditorUtility.SetDirty(style);AssetDatabase.SaveAssets();
        }
    }
}
