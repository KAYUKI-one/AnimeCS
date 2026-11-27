using System;
using System.Linq;
using NUnit.Framework;
using StylizedCharacters;
using StylizedCharacters.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimeCS.Tests
{
    public sealed class HumanoidImportTests
    {
        [TestCase("mmd")]
        [TestCase("bip001")]
        [TestCase("bip01")]
        public void ExportNamingProfilesCreateAnAvatarAndPrepareTheSameVisualPrefabTwice(string convention)
        {
            const string path = "Assets/Content/Data/Characters/Nora.asset";
            var definition = AssetDatabase.LoadAssetAtPath<AnimeCS.Content.CharacterDefinition>(path);
            var model = Object.Instantiate(definition.Model);
            string folder = "Assets/AnimeCS_Test_Humanoid_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder));
            try
            {
                model.name = "Imported Figure";
                var original = model.GetComponentInChildren<Animator>();
                var humanoid = Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                    .Select(i => (role: (HumanBodyBones)i, bone: original.GetBoneTransform((HumanBodyBones)i)))
                    .Where(pair => pair.bone).ToArray();
                foreach (var pair in humanoid) pair.bone.name = NameFor(pair.role.ToString(), convention);
                original.avatar = null;
                var map = HumanoidImport.Map(model.GetComponentsInChildren<Transform>(true), out var missing);
                Assert.That(missing, Is.Empty);
                Assert.That(map.Select(bone => bone.boneName).Distinct().Count(), Is.EqualTo(map.Length));
                var source = PrefabUtility.SaveAsPrefabAsset(model, folder + "/Figure.prefab");
                var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CharacterPrefabSetup.ControllerPath);
                var visual = CharacterPrefabSetup.PrepareVisual(source, controller);
                Assert.That(visual, Is.Not.Null);
                Assert.That(HumanoidImport.Valid(visual.GetComponentInChildren<Animator>()), Is.True);
                Assert.That(visual.GetComponent<PreparedCharacter>().Body, Is.Not.Empty);
                var motion = new SerializedObject(visual.GetComponentInChildren<HumanoidSecondaryMotion>());
                Assert.That(motion.FindProperty("hairJoints").arraySize, Is.Positive);
                Assert.That(motion.FindProperty("clothJoints").arraySize, Is.Positive);
                Assert.That(CharacterPrefabSetup.PrepareVisual(source, controller), Is.SameAs(visual));
                var player = CharacterPrefabSetup.Create(source, controller);
                Assert.That(player.GetComponent<CharacterController>(), Is.Not.Null);
                Assert.That(player.GetComponentsInChildren<Camera>(), Has.Length.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(model);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static string NameFor(string role, string convention)
        {
            if (role == "Hips") return convention == "mmd" ? "下半身" : "Bip001 Pelvis";
            if (role == "Spine") return convention == "mmd" ? "上半身" : "Bip001 Spine";
            if (role == "Chest") return convention == "mmd" ? "上半身２" : "Bip001 Spine1";
            if (role == "UpperChest") return convention == "mmd" ? "上半身３" : "Bip001 Spine2";
            if (role == "Neck") return convention == "mmd" ? "首" : "Bip001 Neck";
            if (role == "Head") return convention == "mmd" ? "頭" : "Bip001 Head";
            bool left = role.StartsWith("Left", StringComparison.Ordinal);
            bool right = role.StartsWith("Right", StringComparison.Ordinal);
            if (!left && !right) return role;
            string side = left ? "L" : "R";
            string part = role.Substring(left ? 4 : 5);
            string mmd = part switch
            {
                "Shoulder" => "肩", "UpperArm" => "腕", "LowerArm" => "ひじ", "Hand" => "手首",
                "UpperLeg" => "足", "LowerLeg" => "ひざ", "Foot" => "足首", "Toes" => "つま先", _ => null
            };
            if (mmd == null) return role;
            if (convention == "mmd") return mmd + "." + side;
            string bip = part switch
            {
                "Shoulder" => "Clavicle", "LowerArm" => "Forearm", "UpperLeg" => "Thigh",
                "LowerLeg" => "Calf", "Toes" => "Toe0", _ => part
            };
            return convention == "bip001" ? $"Bip001 {side} {bip}" : $"Character:Bip01_{side}_{bip}";
        }
    }
}
