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
                t.hasFixedDuration = true;
                t.duration = modeValue == 1 ? .16f : .24f;
                t.canTransitionToSelf = false;
                t.AddCondition(AnimatorConditionMode.Equals, modeValue, "Mode");
            }

            AddAnyTransition(idle, 0);
            AddAnyTransition(run, 1);
            AddAnyTransition(crouch, 2);
            AddAnyTransition(walk, 3);
            AddAnyTransition(pose, 4);

            AddBlinkLayer(controller);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void AddBlinkLayer(AnimatorController controller)
        {
            var clip = new AnimationClip { name = "Relaxed blink", frameRate = 60f };
            var curve = new AnimationCurve();
            foreach (var key in new[] { new Keyframe(0, 0), new Keyframe(2.14f, 0),
                         new Keyframe(2.22f, 100), new Keyframe(2.25f, 100), new Keyframe(2.40f, 0),
                         new Keyframe(6.06f, 0), new Keyframe(6.14f, 100), new Keyframe(6.31f, 0),
                         new Keyframe(7.4f, 0) })
                curve.AddKey(key);
            foreach (string path in new[] { "Aegis_Lily/Lily", "Ember_Firefly/Firefly", "GFL2_Mishty/Mishty" })
            {
                string modelPath = "Assets/Content/Art/Characters/" + path + ".fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                var skin = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Single(r => r.sharedMesh.GetBlendShapeIndex("ShowcaseBlink") >= 0);
                string bindingPath = AnimationUtility.CalculateTransformPath(skin.transform, model.transform);
                var binding = EditorCurveBinding.FloatCurve(bindingPath, typeof(SkinnedMeshRenderer), "blendShape.ShowcaseBlink");
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.AddObjectToAsset(clip, controller);
            controller.AddLayer("Eyelids");
            var layers = controller.layers;
            var face = layers[layers.Length - 1];
            face.defaultWeight = 1f;
            var blink = face.stateMachine.AddState("Blink");
            blink.writeDefaultValues = false;
            blink.motion = clip;
            face.stateMachine.defaultState = blink;
            controller.layers = layers;
        }
    }
}
