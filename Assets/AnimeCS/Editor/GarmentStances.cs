using System.IO;
using System.Linq;
using AnimeCS.Content;
using UnityEditor;
using UnityEngine;

namespace AnimeCS.Editor
{
    internal static class GarmentStances
    {
        public static void Bind(OutfitDefinition outfit)
        {
            var character = outfit.Character;
            if (!character || !character.Model || !character.Controller) return;
            string model = AssetDatabase.GetAssetPath(character.Model);
            string folder = Path.GetDirectoryName(model)?.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(model);
            string source = $"{folder}/{name}Idle.fbx";
            if (AssetImporter.GetAtPath(source) is not ModelImporter importer) return;
            var avatar = character.Model.GetComponentInChildren<Animator>().avatar;
            if (!avatar || !avatar.isHuman) return;
            if (importer.animationType != ModelImporterAnimationType.Human || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || !importer.preserveHierarchy || !importer.importAnimation)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar = null;
                importer.preserveHierarchy = true;
                importer.importAnimation = true;
                importer.SaveAndReimport();
            }
            if (!importer.clipAnimations.Any(c => c.name == "DuckIdle" && c.loopTime))
            {
                var clips = importer.defaultClipAnimations;
                foreach (var clip in clips)
                {
                    clip.name = clip.name.Split('|').Last();
                    clip.loopTime = true;
                    clip.lockRootRotation = clip.lockRootPositionXZ = clip.lockRootHeightY = true;
                    clip.keepOriginalOrientation = clip.keepOriginalPositionXZ = clip.keepOriginalPositionY = true;
                }
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
            string target = $"{folder}/{name}Stance.overrideController";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(target);
            if (!controller)
            {
                controller = new AnimatorOverrideController(character.Controller);
                AssetDatabase.CreateAsset(controller, target);
            }
            controller.runtimeAnimatorController = character.Controller;
            var takes = AssetDatabase.LoadAllAssetsAtPath(source).OfType<AnimationClip>()
                .Where(c => c.name == "Idle" || c.name == "DuckIdle").ToArray();
            if (takes.Length != 2) throw new InvalidDataException($"Expected standing and crouching clips in {source}.");
            foreach (var take in takes) controller[take.name] = take;
            EditorUtility.SetDirty(controller);
            var data = new SerializedObject(outfit);
            data.FindProperty("_stanceController").objectReferenceValue = controller;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

