using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StylizedCharacters.Editor
{
    /// <summary>Recognizes standard humanoid, Bip and localized MMD deform bones in imported models.</summary>
    public static class HumanoidImport
    {
        private static readonly string[] Sides = { "Left", "Right" };
        private static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
        private static readonly string[] FingerMmd = { "親指", "人指", "中指", "薬指", "小指" };
        private static readonly string[] Joints = { "Proximal", "Intermediate", "Distal" };
        private static readonly Dictionary<string, string[]> Aliases = BuildAliases();

        public static bool Valid(Animator animator) => animator && animator.avatar && animator.avatar.isValid && animator.isHuman;

        public static Avatar CreateAvatar(GameObject model, out string[] missing)
        {
            var skeleton = model.GetComponentsInChildren<Transform>(true);
            var mapping = Map(skeleton, out missing);
            if (missing.Length != 0) return null;
            var description = new HumanDescription
            {
                human = mapping,
                skeleton = skeleton.Select(bone => new SkeletonBone
                {
                    name = bone.name, position = bone.localPosition, rotation = bone.localRotation, scale = bone.localScale
                }).ToArray(),
                upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f,
                armStretch = .05f, legStretch = .05f
            };
            return AvatarBuilder.BuildHumanAvatar(model, description);
        }

        /// <summary>Mapping uses existing joint names and selects each bone at most once.</summary>
        public static HumanBone[] Map(IEnumerable<Transform> skeleton, out string[] missing)
        {
            var unique = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var bone in skeleton)
            {
                if (!bone) continue;
                string key = Normalize(bone.name);
                if (!unique.TryAdd(key, bone)) unique[key] = null;
            }
            var claimed = new HashSet<Transform>();
            var result = new List<HumanBone>();
            var required = new List<string>();
            var names = HumanTrait.BoneName;
            for (int i = 0; i < names.Length; i++)
            {
                string slot = names[i].Replace(" ", "");
                Transform selected = null;
                if (unique.TryGetValue(Normalize(slot), out var exact) && exact && claimed.Add(exact)) selected = exact;
                if (!selected && Aliases.TryGetValue(slot, out var alternatives))
                    foreach (string alias in alternatives)
                        if (unique.TryGetValue(alias, out var candidate) && candidate && claimed.Add(candidate))
                        { selected = candidate; break; }
                if (selected) result.Add(new HumanBone { boneName = selected.name, humanName = names[i],
                    limit = new HumanLimit { useDefaultValues = true } });
                else if (HumanTrait.RequiredBone(i)) required.Add(names[i]);
            }
            missing = required.ToArray();
            return result.ToArray();
        }

        private static Dictionary<string, string[]> BuildAliases()
        {
            var map = new Dictionary<string, string[]>();
            void Add(string slot, params string[] aliases) => map[slot] = aliases.Select(Normalize).Distinct().ToArray();
            Add("Hips", "Pelvis", "CenterHip", "RootHip", "腰", "下半身", "髋", "胯");
            Add("Spine", "Spine0", "Spine01", "Waist", "上半身", "脊椎");
            Add("Chest", "Spine1", "Spine02", "UpperBody", "上半身2", "胸");
            Add("UpperChest", "Spine2", "Spine03", "Chest2", "上半身3", "上胸");
            Add("Neck", "Neck0", "Neck01", "首", "颈");
            Add("Head", "Head0", "Head01", "頭", "头");
            foreach (var side in Sides)
            {
                string shortSide = side.Substring(0, 1);
                string local = side == "Left" ? "左" : "右";
                void Limb(string suffix, string[] canonical, params string[] mmd)
                {
                    var candidates = new List<string>();
                    foreach (var term in canonical)
                        candidates.AddRange(new[] { shortSide + term, term + shortSide, side + term, term + side });
                    foreach (var term in mmd) candidates.AddRange(new[] { local + term, term + shortSide });
                    Add(side + suffix, candidates.ToArray());
                }
                Limb("Shoulder", new[] { "Clavicle", "Collar", "Shoulder" }, "肩");
                Limb("UpperArm", new[] { "UpperArm", "Arm" }, "腕", "上臂");
                Limb("LowerArm", new[] { "Forearm", "LowerArm", "Elbow" }, "ひじ", "肘");
                Limb("Hand", new[] { "Hand", "Wrist" }, "手首", "手腕");
                Limb("UpperLeg", new[] { "UpperLeg", "Thigh", "UpLeg" }, "足", "大腿");
                Limb("LowerLeg", new[] { "Calf", "LowerLeg", "Shin", "Knee", "Leg" }, "ひざ", "膝", "小腿");
                Limb("Foot", new[] { "Foot", "Ankle" }, "足首", "脚踝");
                Limb("Toes", new[] { "Toe0", "Toe", "Toes", "ToeBase" }, "つま先", "足先");
                Limb("Eye", new[] { "Eye", "Eye0" }, "目", "眼");
                for (int finger = 0; finger < Fingers.Length; finger++)
                    for (int joint = 0; joint < Joints.Length; joint++)
                    {
                        string bip = "Finger" + finger + (joint == 0 ? "" : joint.ToString());
                        string chain = Fingers[finger] + (joint + 1);
                        string mmd = FingerMmd[finger] + (finger == 0 ? joint : joint + 1);
                        Limb(Fingers[finger] + Joints[joint], new[] { bip, chain }, mmd);
                    }
            }
            return map;
        }

        internal static string Normalize(string name)
        {
            int namespaceEnd = name.LastIndexOf(':');
            if (namespaceEnd >= 0) name = name.Substring(namespaceEnd + 1);
            name = name.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            var key = new StringBuilder(name.Length);
            foreach (char c in name) if (char.IsLetterOrDigit(c)) key.Append(c);
            name = key.ToString();
            foreach (string prefix in new[] { "mixamorig", "jbip001", "jbip01", "bip001", "bip01", "jbip", "bip" })
                if (name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length)
                    return name.Substring(prefix.Length);
            return name;
        }
    }
}
