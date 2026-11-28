using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AnimeCS.Editor
{
    /// <summary>
    /// Configures AnimeShowcaseMotions.fbx as Humanoid and generates AnimeShowcase.controller
    /// for graceful anime RPG animations (Idle, Walk, Run, Pose, Crouch) in RenderReviewStage.
    /// </summary>
    public static class AnimeShowcaseBake
    {
        public const string MotionsFbxPath = "Assets/Content/Art/Characters/AnimeShowcaseMotions.fbx";
        public const string ControllerPath = "Assets/Resources/AnimeShowcase.controller";
        private const string DonorPath = "Assets/Content/Art/Characters/Aegis_Rover/Rover.fbx";

        [MenuItem("DanbaidongRP/Character Setup/Rebuild Anime Showcase Controller")]
        public static AnimatorController Rebuild()
        {
            if (AssetImporter.GetAtPath(MotionsFbxPath) is not ModelImporter importer)
                throw new InvalidOperationException($"Missing {MotionsFbxPath}. Run ArtSource/Blender/anime_showcase_motions.py first.");

            var donorImporter = AssetImporter.GetAtPath(DonorPath) as ModelImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            if (donorImporter != null && donorImporter.humanDescription.human.Length > 0)
            {
                var desc = importer.humanDescription;
                desc.human = donorImporter.humanDescription.human;
                importer.humanDescription = desc;
            }

            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = c.name.Split('|').Last();
                c.loopTime = true;
                c.lockRootRotation = c.lockRootPositionXZ = c.lockRootHeightY = true;
                c.keepOriginalOrientation = c.keepOriginalPositionXZ = c.keepOriginalPositionY = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            var loadedClips = AssetDatabase.LoadAllAssetsAtPath(MotionsFbxPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__", StringComparison.Ordinal))
                .ToDictionary(c => c.name);

            // Also reuse DuckIdle from Rover donor for backwards compatibility with RenderReviewTests crouch mode
            var duckIdle = AssetDatabase.LoadAllAssetsAtPath(DonorPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == "DuckIdle");

            System.IO.Directory.CreateDirectory("Assets/Resources");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Mode", AnimatorControllerParameterType.Int);
            controller.AddParameter("Crouch", AnimatorControllerParameterType.Float);
            controller.AddParameter("Forward", AnimatorControllerParameterType.Float);
            controller.AddParameter("Strafe", AnimatorControllerParameterType.Float);
            controller.AddParameter("Vertical", AnimatorControllerParameterType.Float);
            controller.AddParameter("AirTurn", AnimatorControllerParameterType.Float);

            var sm = controller.layers[0].stateMachine;
            var idle = sm.AddState("AnimeIdle");
            idle.motion = loadedClips["AnimeIdle"];
            sm.defaultState = idle;

            var run = sm.AddState("AnimeRun");
            run.motion = loadedClips["AnimeRun"];

            var crouch = sm.AddState("DuckIdle");
            crouch.motion = duckIdle ? duckIdle : loadedClips["AnimeIdle"];

            var walk = sm.AddState("AnimeWalk");
            walk.motion = loadedClips["AnimeWalk"];

            var pose = sm.AddState("AnimePose");
            pose.motion = loadedClips["AnimePose"];

            void AddAnyTransition(AnimatorState target, int modeValue)
            {
                var t = sm.AddAnyStateTransition(target);
                t.hasExitTime = false;
                t.duration = 0.22f;
                t.canTransitionToSelf = false;
                t.AddCondition(AnimatorConditionMode.Equals, modeValue, "Mode");
            }

            AddAnyTransition(idle, 0);
            AddAnyTransition(run, 1);
            AddAnyTransition(crouch, 2);
            AddAnyTransition(walk, 3);
            AddAnyTransition(pose, 4);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }
    }
}
