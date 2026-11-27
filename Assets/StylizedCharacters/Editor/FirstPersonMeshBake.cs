using UnityEngine;

namespace StylizedCharacters.Editor
{
    public static class FirstPersonMeshBake
    {
        /// <summary>Classifies bind-pose vertices. Mask indices remain valid on a material repartition of the same mesh.</summary>
        public static byte[] Create(Animator humanoid, SkinnedMeshRenderer skin)
        {
            if (!humanoid || !humanoid.isHuman || !skin || !skin.sharedMesh) return null;
            var head = humanoid.GetBoneTransform(HumanBodyBones.Neck) ?? humanoid.GetBoneTransform(HumanBodyBones.Head);
            var left = humanoid.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var right = humanoid.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (!head || !left || !right) return null;
            var mesh = skin.sharedMesh;
            var weights = mesh.boneWeights;
            if (mesh.vertexCount != weights.Length) return null;
            var bones = skin.bones;
            var arms = new bool[bones.Length];
            var concealed = new bool[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (!bone) continue;
                arms[i] = bone.IsChildOf(left) || bone.IsChildOf(right);
                string name = bone.name;
                concealed[i] = bone.IsChildOf(head) || name.StartsWith("SpringHair_", System.StringComparison.Ordinal) ||
                    HumanoidSpringImport.IsLooseGarment(name) ||
                    name.StartsWith("SpringCloth_UpperChest_", System.StringComparison.Ordinal) ||
                    name.StartsWith("SpringCloth_Chest_", System.StringComparison.Ordinal) ||
                    name.Contains("Shoulder_");
            }
            // These atlases contain open garment shells. Showing the shoulders through the hands
            // keeps the live grip visible without looking through a cut waist, collar or skirt.
            var mask = new byte[(weights.Length + 7) / 8];
            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight w = weights[i];
                bool visible = Sum(w, concealed) < .27f && Sum(w, arms) >= .58f;
                if (visible) mask[i >> 3] |= (byte)(1 << (i & 7));
            }
            return mask;
        }

        private static float Sum(BoneWeight w, bool[] region) =>
            (region[w.boneIndex0] ? w.weight0 : 0f) +
            (region[w.boneIndex1] ? w.weight1 : 0f) +
            (region[w.boneIndex2] ? w.weight2 : 0f) +
            (region[w.boneIndex3] ? w.weight3 : 0f);

        /// <summary>Finds foot-local sole points from the imported boot geometry rather than the ankle's vertical projection.</summary>
        public static Vector3[] MeasureSoles(Animator humanoid, SkinnedMeshRenderer skin)
        {
            if (!humanoid || !humanoid.isHuman || !skin || !skin.sharedMesh) return null;
            var boneIds = new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes };
            var bones = skin.bones;
            var mesh = skin.sharedMesh;
            var positions = mesh.vertices;
            var weights = mesh.boneWeights;
            var bind = mesh.bindposes;
            if (positions.Length != weights.Length) return null;
            var result = new Vector3[boneIds.Length];
            for (int foot = 0; foot < result.Length; foot++)
            {
                var bone = humanoid.GetBoneTransform(boneIds[foot]);
                int index = bone ? System.Array.IndexOf(bones, bone) : -1;
                if (index < 0 || index >= bind.Length) continue;
                float lowest = float.PositiveInfinity;
                Vector3 point = default;
                for (int v = 0; v < positions.Length; v++)
                {
                    if (weights[v].boneIndex0 != index) continue;
                    var height = skin.transform.TransformPoint(positions[v]).y;
                    if (height >= lowest) continue;
                    lowest = height;
                    point = positions[v];
                }
                if (!float.IsPositiveInfinity(lowest)) result[foot] = bind[index].MultiplyPoint3x4(point);
            }
            return result;
        }
    }
}
