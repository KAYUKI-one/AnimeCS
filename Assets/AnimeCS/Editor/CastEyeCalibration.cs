using System.Globalization;
using System.IO;
using System.Linq;
using AnimeCS.Content;
using AnimeCS.Play;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AnimeCS.Editor
{
    internal static class CastEyeCalibration
    {
        internal static void Bind(OutfitDefinition outfit)
        {
            var character=outfit.Character;
            string path=Path.ChangeExtension(AssetDatabase.GetAssetPath(character.Model),null)+"Face.txt";
            var file=AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if(!file)return;
            var xyz=file.text.Split(' ').Select(x=>float.Parse(x,CultureInfo.InvariantCulture)).ToArray();
            var body=Object.Instantiate(character.Model);
            var graph=PlayableGraph.Create();
            try
            {
                var animator=body.GetComponentInChildren<Animator>();
                var head=animator.GetBoneTransform(HumanBodyBones.Head);
                var anchor=head.InverseTransformPoint(body.transform.TransformPoint(new Vector3(xyz[0],xyz[1],xyz[2])));
                var data=new SerializedObject(character);
                data.FindProperty("_faceAnchor").vector3Value=anchor;data.ApplyModifiedPropertiesWithoutUndo();
                var rig=new CharacterRig(body,character,outfit.PoseController);
                var clip=(outfit.PoseController?outfit.PoseController:character.Controller).animationClips.First(c=>c.name=="Idle");
                var playable=AnimationClipPlayable.Create(graph,clip);
                AnimationPlayableOutput.Create(graph,"Calibration",animator).SetSourcePlayable(playable);
                graph.Play();graph.Evaluate(.02f);
                data.Update();
                data.FindProperty("_standingEyes").floatValue=head.TransformPoint(anchor).y-rig.LowestSole(body.transform.position.y);
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            finally { graph.Destroy(); Object.DestroyImmediate(body); }
        }
    }
}
