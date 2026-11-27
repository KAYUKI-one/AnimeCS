using System.Collections.Generic;
using StylizedCharacters.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AnimeCS.Editor
{
    internal static class CharacterKitAnimations
    {
        private const string Folder = "Assets/StylizedCharacters/Animation";
        private const string Source = "Assets/Content/Art/Characters/Locomotion.controller";

        [MenuItem("AnimeCS/美术/准备通用角色动作库")]
        internal static void Prepare()
        {
            ContentLibrary.EnsureFolder(Folder + "/Clips");
            string target = CharacterPrefabSetup.ControllerPath;
            if (!AssetDatabase.LoadAssetAtPath<AnimatorController>(target) && !AssetDatabase.CopyAsset(Source, target))
                throw new System.InvalidOperationException("Unable to copy the locomotion controller.");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(target);
            for (int i = controller.layers.Length - 1; i > 0; i--)
                if (controller.layers[i].name == "Upper Body") controller.RemoveLayer(i);
            var exported = new Dictionary<AnimationClip, AnimationClip>();
            foreach (var layer in controller.layers) Replace(layer.stateMachine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            void Replace(AnimatorStateMachine machine)
            {
                foreach (var node in machine.states)
                {
                    node.state.motion = Export(node.state.motion);
                    EditorUtility.SetDirty(node.state);
                }
                foreach (var nested in machine.stateMachines) Replace(nested.stateMachine);
            }

            Motion Export(Motion motion)
            {
                if (motion is BlendTree tree)
                {
                    var children = tree.children;
                    for (int i = 0; i < children.Length; i++) children[i].motion = Export(children[i].motion);
                    tree.children = children;
                    EditorUtility.SetDirty(tree);
                    return tree;
                }
                if (motion is not AnimationClip clip) return motion;
                if (AssetDatabase.GetAssetPath(clip).StartsWith(Folder + "/")) return clip;
                if (exported.TryGetValue(clip, out var saved)) return saved;
                var path = $"{Folder}/Clips/{clip.name}.anim";
                saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (saved) EditorUtility.CopySerialized(clip, saved);
                else
                {
                    saved = Object.Instantiate(clip);
                    saved.name = clip.name;
                    saved.hideFlags = HideFlags.None;
                    AssetDatabase.CreateAsset(saved, path);
                }
                exported.Add(clip, saved);
                return saved;
            }
        }
    }
}
