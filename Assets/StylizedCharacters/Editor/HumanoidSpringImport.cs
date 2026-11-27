using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Animations.SpringBones;
using UnityEditor;
using UnityEngine;

namespace StylizedCharacters.Editor
{
    public static class HumanoidSpringImport
    {
        private static readonly string[] Hair = { "springhair", "hair", "bangs", "ponytail", "braid", "veil",
            "髪", "髮", "头发", "发辫", "辫子", "刘海", "馬尾", "马尾", "頭紗", "头纱", "ベール" };
        private static readonly string[] Cloth = { "springcloth", "dress", "skirt", "cape", "cloak", "coat",
            "scarf", "ribbon", "piaodai", "裙", "衣摆", "衣擺", "披风", "披風", "飘带", "スカート", "マント" };

        internal static bool IsLooseGarment(string name)
        {
            string key = HumanoidImport.Normalize(name);
            return Cloth.Any(token => key.Contains(token)) || Hair.Any(token => key.Contains(token));
        }

        /// <summary>Finds weighted cloth and hair joints while protecting the animated Humanoid hierarchy.</summary>
        public static (int hair, int cloth) Configure(Animator animator)
        {
            if (animator.GetComponentInChildren<SpringManager>(true)) return default;
            var rig = animator.GetComponent<HumanoidSecondaryMotion>();
            if (!rig) rig = animator.gameObject.AddComponent<HumanoidSecondaryMotion>();
            var data = new SerializedObject(rig);
            var hair = data.FindProperty("hairJoints");
            var cloth = data.FindProperty("clothJoints");
            if (hair.arraySize > 0 || cloth.arraySize > 0) return (hair.arraySize, cloth.arraySize);

            var protectedBones = new HashSet<Transform>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                for (var bone = animator.GetBoneTransform((HumanBodyBones)i); bone && protectedBones.Add(bone); bone = bone.parent) { }
            var weighted = new HashSet<Transform>();
            foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin.sharedMesh) continue;
                var bones = skin.bones;
                foreach (var weight in skin.sharedMesh.boneWeights)
                {
                    Keep(weight.boneIndex0, weight.weight0);
                    Keep(weight.boneIndex1, weight.weight1);
                    Keep(weight.boneIndex2, weight.weight2);
                    Keep(weight.boneIndex3, weight.weight3);
                }
                void Keep(int index, float value)
                {
                    if (value > .02f && index < bones.Length && bones[index]) weighted.Add(bones[index]);
                }
            }
            var hairBones = new List<Transform>();
            var clothBones = new List<Transform>();
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
            {
                if (!weighted.Contains(bone) || protectedBones.Contains(bone)) continue;
                // Children often use numeric identifiers. Inherit the meaning of their nearest named branch.
                for (var branch = bone; branch && !protectedBones.Contains(branch); branch = branch.parent)
                {
                    string key = HumanoidImport.Normalize(branch.name);
                    var group = Hair.Any(token => key.Contains(token)) ? hairBones :
                        Cloth.Any(token => key.Contains(token)) ? clothBones : null;
                    if (group == null) continue;
                    group.Add(bone);
                    EnsureTip(bone);
                    break;
                }
            }
            Bind(hair, hairBones);
            Bind(cloth, clothBones);
            data.ApplyModifiedPropertiesWithoutUndo();
            return (hairBones.Count, clothBones.Count);
        }

        private static void Bind(SerializedProperty property, List<Transform> bones)
        {
            property.arraySize = bones.Count;
            for (int i = 0; i < bones.Count; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = bones[i];
        }

        private static void EnsureTip(Transform bone)
        {
            if (bone.childCount > 0) return;
            var parentSpan = bone.parent ? bone.position - bone.parent.position : Vector3.down * .04f;
            if (parentSpan.sqrMagnitude < .00001f) parentSpan = Vector3.down * .04f;
            var tip = new GameObject(bone.name + "_End").transform;
            tip.SetParent(bone, false);
            tip.position = bone.position + parentSpan.normalized * Mathf.Clamp(parentSpan.magnitude * .65f, .016f, .10f);
        }
    }
}
