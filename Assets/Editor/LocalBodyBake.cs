using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimeCS.Content;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AnimeCS.Editor
{
    internal static class LocalBodyBake
    {
        [MenuItem("AnimeCS/美术/更新共享动作与第一人称下身")]
        internal static void RefreshCast()
        {
            var cast = ContentLibrary.Catalog.SeatOutfits.Where(f => f && !f.Archived && f.Character && f.ViewRig).Distinct().ToArray();
            foreach(var outfit in cast)
            {
                ConfigureLayers(outfit.Character.Controller as AnimatorController);
                var data = new SerializedObject(outfit);
                data.FindProperty("_stanceController").objectReferenceValue = null;
                data.ApplyModifiedPropertiesWithoutUndo();
                Bind(outfit);
                CastEyeCalibration.Bind(outfit);
            }
            AssetDatabase.SaveAssets();
        }

        internal static void ConfigureLayers(AnimatorController controller)
        {
            if(!controller || controller.layers.Any(l => l.name == "Upper Body")) return;
            string path = Path.GetDirectoryName(AssetDatabase.GetAssetPath(controller)).Replace('\\','/') + "/UpperBody.mask";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if(!mask)
            {
                mask = new AvatarMask();
                for(int i=0; i<(int)AvatarMaskBodyPart.LastBodyPart; i++)
                    mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
                foreach(var part in new[]{ AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm,
                    AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers })
                    mask.SetHumanoidBodyPartActive(part,true);
                AssetDatabase.CreateAsset(mask,path);
            }
            var idle=controller.animationClips.First(c=>c.name=="Idle");
            controller.AddLayer("Upper Body");
            var layers=controller.layers;
            var upper=layers[layers.Length-1];upper.avatarMask=mask;upper.defaultWeight=1;
            var state=upper.stateMachine.AddState("Hold");
            state.motion=idle;upper.stateMachine.defaultState=state;
            controller.layers=layers; EditorUtility.SetDirty(controller);
        }

        internal static void Bind(OutfitDefinition outfit)
        {
            var data=new SerializedObject(outfit);
            var records=data.FindProperty("_bodyRegions");
            var renderers=outfit.Character.Model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for(int i=0;i<records.arraySize;i++)
            {
                var region=outfit.Regions(false)[i];
                if(!region.partitioned) continue;
                var include=Enumerable.Range(0,region.slots.Length).Where(slot=>region.slots[slot]==(int)WearSlot.Bottom || region.slots[slot]==(int)WearSlot.Shoes).ToArray();
                if(include.Length==0)continue;
                var skin=renderers.First(r=>r.sharedMesh==region.source);
                var thigh=skin.bones.FirstOrDefault(b=>b && b.name=="LeftUpperLeg");
                var knee=skin.bones.FirstOrDefault(b=>b && b.name=="LeftLowerLeg");
                float limit=thigh && knee?Mathf.Lerp(thigh.position.y,knee.position.y,.25f):float.PositiveInfinity;
                var compact=Compact(region.partitioned,ref include,skin.transform.localToWorldMatrix,limit);
                string path=Path.ChangeExtension(AssetDatabase.GetAssetPath(region.partitioned),null)+"Legs.asset";
                var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(saved){EditorUtility.CopySerialized(compact,saved);Object.DestroyImmediate(compact);}
                else{saved=compact;AssetDatabase.CreateAsset(saved,path);}
                var record=records.GetArrayElementAtIndex(i);
                record.FindPropertyRelative("firstPersonLower").objectReferenceValue=saved;
                var slots=record.FindPropertyRelative("lowerMaterialSlots");slots.arraySize=include.Length;
                for(int j=0;j<include.Length;j++)slots.GetArrayElementAtIndex(j).intValue=include[j];
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            BindPerspective(outfit);
        }

        [MenuItem("AnimeCS/美术/更新共享第一人称可见区域")]
        internal static void BakePerspective()
        {
            foreach (var outfit in ContentLibrary.Catalog.SeatOutfits.Where(o => o && !o.Archived && o.Character).Distinct())
                BindPerspective(outfit);
            AssetDatabase.SaveAssets();
        }

        private static void BindPerspective(OutfitDefinition outfit)
        {
            var model = outfit.Character.Model;
            if (!model || outfit.Regions(false) == null) return;
            var animator = model.GetComponentInChildren<Animator>(true);
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var region in outfit.Regions(false))
            {
                var renderer = renderers.FirstOrDefault(r => r.sharedMesh == region.source);
                // A packed array is substantially smaller than embedding another copy of the skinned mesh.
                region.firstPersonVisible = StylizedCharacters.Editor.FirstPersonMeshBake.Create(animator, renderer);
                region.soleAnchors = StylizedCharacters.Editor.FirstPersonMeshBake.MeasureSoles(animator, renderer);
                if (region.firstPersonVisible?.Length > 0 && region.partitioned && !region.partitioned.isReadable)
                {
                    // The source FBX can stay GPU-only. The generated partition is also the source for the camera mesh.
                    var mesh = new SerializedObject(region.partitioned);
                    mesh.FindProperty("m_IsReadable").boolValue = true;
                    mesh.FindProperty("m_KeepVertices").boolValue = true;
                    mesh.FindProperty("m_KeepIndices").boolValue = true;
                    mesh.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            EditorUtility.SetDirty(outfit);
        }

        private static Mesh Compact(Mesh source,ref int[] slots, Matrix4x4 toModel,float top)
        {
            var original=new List<int>();
            var reverse=new Dictionary<int,int>();
            var faces=new List<int[]>();
            var usedSlots=new List<int>();
            var heights=source.vertices.Select(v=>toModel.MultiplyPoint3x4(v).y).ToArray();
            foreach(int slot in slots)
            {
                int[] indices=source.GetTriangles(slot);
                var retained=new List<int>(indices.Length);
                for(int i=0;i<indices.Length;i+=3)
                {
                    if(Mathf.Max(heights[indices[i]],Mathf.Max(heights[indices[i+1]],heights[indices[i+2]]))>top)continue;
                    for(int j=0;j<3;j++)
                    {
                        int vertex=indices[i+j];
                        if(!reverse.TryGetValue(vertex,out int compact)){compact=original.Count;reverse.Add(vertex,compact);original.Add(vertex);}
                        retained.Add(compact);
                    }
                }
                if(retained.Count==0)continue;
                usedSlots.Add(slot);faces.Add(retained.ToArray());
            }
            slots=usedSlots.ToArray();
            T[] Keep<T>(T[] values) => values.Length==source.vertexCount ? original.Select(i=>values[i]).ToArray() : System.Array.Empty<T>();
            var mesh=new Mesh{name=source.name+"Legs",indexFormat=original.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.vertices=Keep(source.vertices);mesh.normals=Keep(source.normals);mesh.tangents=Keep(source.tangents);
            mesh.uv=Keep(source.uv);mesh.uv2=Keep(source.uv2);mesh.colors32=Keep(source.colors32);
            mesh.bindposes=source.bindposes;mesh.boneWeights=Keep(source.boneWeights);mesh.subMeshCount=faces.Count;
            for(int i=0;i<faces.Count;i++)mesh.SetTriangles(faces[i],i,false);
            var v=new Vector3[source.vertexCount];var n=new Vector3[v.Length];var t=new Vector3[v.Length];
            for(int i=0;i<source.blendShapeCount;i++)
            {
                var name=source.GetBlendShapeName(i);
                if(!name.EndsWith("DrapeStand") && !name.EndsWith("DrapeCrouch")) continue;
                for(int j=0;j<source.GetBlendShapeFrameCount(i);j++)
                {
                    source.GetBlendShapeFrameVertices(i,j,v,n,t);
                    mesh.AddBlendShapeFrame(name,source.GetBlendShapeFrameWeight(i,j),Keep(v),Keep(n),Keep(t));
                }
            }
            mesh.RecalculateBounds();return mesh;
        }
    }
}
