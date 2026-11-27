using System;
using System.Collections.Generic;
using System.Linq;
using AnimeCS.Content;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimeCS.Editor
{
    internal static class WearableBake
    {
        const string Folder="Assets/Content/Art/Wearables";
        public static void Bake(ContentCatalog catalog,List<OutfitDefinition> outfits)
        {
            ContentLibrary.EnsureFolder(ContentLibrary.Root+"/Wearables");
            var hidden=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Hidden.mat");
            if(!hidden){hidden=new Material(Shader.Find("AnimeCS/Hidden garment region"));AssetDatabase.CreateAsset(hidden,Folder+"/Hidden.mat");}
            CastArtImport.Remap(Folder+"/CatBand.fbx",true);
            var head=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/CatBand.fbx");
            foreach(var (slot,file,label,color) in new[]{
                (WearSlot.Headwear,"CatBand","绒面猫耳发箍","#54495d"),
                (WearSlot.Top,"FieldJacket","雾蓝训练上衣","#677d8e"),
                (WearSlot.Gloves,"IrisGloves","鸢紫编织手套","#8059a1"),
                (WearSlot.Bottom,"FieldTrousers","石墨训练裤","#474c56"),
                (WearSlot.Shoes,"FieldBoots","沙砾训练靴","#827461")})
            {
                var item=CastArtImport.Definition<WearableDefinition>(catalog,"Wearables",file,"wear."+file.ToLowerInvariant(),label);
                var data=new SerializedObject(item);data.FindProperty("_slot").enumValueIndex=(int)slot;
                data.FindProperty("_included").boolValue=true;
                if(slot==WearSlot.Gloves)
                {
                    var fits=data.FindProperty("_fits");fits.arraySize=outfits.Count;
                    for(int i=0;i<outfits.Count;i++)
                    {
                        var ch=outfits[i].Character;string path=Folder+"/Iris/"+ch.name+"Gloves.fbx";CastArtImport.Remap(path,false);
                        var glove=AssetDatabase.LoadAssetAtPath<GameObject>(path);var fit=fits.GetArrayElementAtIndex(i);
                        fit.FindPropertyRelative("character").objectReferenceValue=ch;fit.FindPropertyRelative("model").objectReferenceValue=glove;fit.FindPropertyRelative("viewModel").objectReferenceValue=glove;
                    }
                    var gloveMat=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Iris/Iris_T00.mat");
                    gloveMat.SetFloat("_Metallic",.02f);gloveMat.SetFloat("_Smoothness",.29f);EditorUtility.SetDirty(gloveMat);
                    data.FindProperty("_material").objectReferenceValue=gloveMat;
                }
                else if(slot==WearSlot.Headwear) data.FindProperty("_model").objectReferenceValue=head;
                else
                {
                    string path=Folder+"/"+file+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
                    ColorUtility.TryParseHtmlString(color,out var tint);mat.color=tint;
                    var weave=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Weave.png");
                    mat.mainTexture=weave;mat.mainTextureScale=Vector2.one*(slot==WearSlot.Gloves?8:14);
                    mat.SetFloat("_Smoothness",slot==WearSlot.Shoes?.34f:.22f);mat.SetFloat("_Cull",0);EditorUtility.SetDirty(mat);
                    data.FindProperty("_material").objectReferenceValue=mat;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            CharacterShading.ApplyToWearables();
            foreach(var outfit in outfits)
            {
                var data=new SerializedObject(outfit);data.FindProperty("_hiddenRegion").objectReferenceValue=hidden;
                Regions(outfit.Character.Model,outfit.name,false,data.FindProperty("_bodyRegions"));
                Regions(outfit.ViewRig,outfit.name,true,data.FindProperty("_viewRegions"));
                data.ApplyModifiedPropertiesWithoutUndo();
                LocalBodyBake.Bind(outfit);
            }
        }

        [MenuItem("AnimeCS/美术/重烘角色身体与第一人称下身蒙皮")]
        internal static void RefreshBodyMeshes()
        {
            foreach (var outfit in ContentLibrary.Catalog.SeatOutfits.Where(o => o && !o.Archived && o.Character && o.Character.Model).Distinct())
            {
                var data = new SerializedObject(outfit);
                Regions(outfit.Character.Model, outfit.name, false, data.FindProperty("_bodyRegions"));
                data.ApplyModifiedPropertiesWithoutUndo();
                LocalBodyBake.Bind(outfit);
            }
            AssetDatabase.SaveAssets();
        }

        static int Slot(Transform t)
        {
            string s=t?t.name:"";
            if (s.StartsWith("SpringHair_", StringComparison.Ordinal) || s.StartsWith("SpringCloth_", StringComparison.Ordinal)) return -2;
            if(s.Contains("Hand") || s.Contains("Thumb") || s.Contains("Index") || s.Contains("Middle") || s.Contains("Ring") || s.Contains("Little"))return (int)WearSlot.Gloves;
            if(s.Contains("Foot") || s.Contains("Toes"))return (int)WearSlot.Shoes;
            if(s.Contains("Leg") || s=="Hips")return (int)WearSlot.Bottom;
            if(s.Contains("Arm") || s.Contains("Shoulder") || s.Contains("Spine") || s.Contains("Chest"))return (int)WearSlot.Top;
            return -1;
        }
        static int Dominant(BoneWeight w,int[] slots)
        {
            int slot = -1;
            float best = 0;
            void Candidate(int bone, float weight)
            {
                if (bone >= slots.Length || slots[bone] == -2 || weight <= best) return;
                slot = slots[bone];
                best = weight;
            }
            Candidate(w.boneIndex0, w.weight0); Candidate(w.boneIndex1, w.weight1);
            Candidate(w.boneIndex2, w.weight2); Candidate(w.boneIndex3, w.weight3);
            return slot;
        }
        static void Regions(GameObject source,string name,bool view,SerializedProperty result)
        {
            if(!source){result.arraySize=0;return;}
            var renderers=source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            result.arraySize=renderers.Length;
            for(int r=0;r<renderers.Length;r++)
            {
                var renderer=renderers[r];var mesh=renderer.sharedMesh;
                var sourceSlots=renderer.bones.Select(Slot).ToArray();
                var weights=mesh.boneWeights;var faceSlot=weights.Select(w=>Dominant(w,sourceSlots)).ToArray();
                var triangles=new List<int[]>();var materials=new List<Material>();var slots=new List<int>();var original=renderer.sharedMaterials;
                for(int sub=0;sub<mesh.subMeshCount;sub++)
                {
                    var buckets=new SortedDictionary<int,List<int>>();
                    var ix=mesh.GetTriangles(sub);
                    for(int j=0;j<ix.Length;j+=3)
                    {
                        int a=faceSlot[ix[j]],b=faceSlot[ix[j+1]],c=faceSlot[ix[j+2]];
                        int key=a==b||a==c?a:b==c?b:a;
                        if(!buckets.TryGetValue(key,out var bucket))buckets.Add(key,bucket=new List<int>());
                        bucket.Add(ix[j]);bucket.Add(ix[j+1]);bucket.Add(ix[j+2]);
                    }
                    foreach(var pair in buckets){triangles.Add(pair.Value.ToArray());slots.Add(pair.Key);materials.Add(original[Mathf.Min(sub,original.Length-1)]);}
                }
                string id=name+(view?"Sleeves":"Body")+r;
                var clone=Object.Instantiate(mesh);clone.name=id;clone.subMeshCount=triangles.Count;
                for(int s=0;s<triangles.Count;s++)clone.SetTriangles(triangles[s],s,false);
                string path=Folder+"/"+id+".asset";
                var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(saved){EditorUtility.CopySerialized(clone,saved);Object.DestroyImmediate(clone);}
                else{saved=clone;AssetDatabase.CreateAsset(saved,path);}
                var record=result.GetArrayElementAtIndex(r);
                record.FindPropertyRelative("source").objectReferenceValue=mesh;record.FindPropertyRelative("partitioned").objectReferenceValue=saved;
                var m=record.FindPropertyRelative("materials");var keys=record.FindPropertyRelative("slots");m.arraySize=keys.arraySize=materials.Count;
                for(int i=0;i<materials.Count;i++){m.GetArrayElementAtIndex(i).objectReferenceValue=materials[i];keys.GetArrayElementAtIndex(i).intValue=slots[i];}
            }
        }
    }
}
