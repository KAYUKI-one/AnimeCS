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
    public static class CastPortraitBaker
    {
        private const string FrontFolder = "Assets/Content/UI/Portraits/Match";

        [MenuItem("AnimeCS/美术/生成角色正面对阵卡")]
        public static void BakeFronts()
        {
            if (!AssetDatabase.IsValidFolder(FrontFolder)) AssetDatabase.CreateFolder("Assets/Content/UI/Portraits", "Match");
            var characters = ContentLibrary.Catalog.Entries.OfType<CharacterDefinition>().Where(c=>!c.Archived && c.Model);
            foreach (var character in characters)
            {
                string output = FrontFolder + "/" + character.name + ".png";
                var preview = new PreviewRenderUtility();
                var graph = PlayableGraph.Create("Frontal portrait");
                try
                {
                    var body = Object.Instantiate(character.Model);
                    preview.AddSingleGO(body);
                    var animator = body.GetComponentInChildren<Animator>();
                    if (!animator || !animator.isHuman) throw new System.InvalidOperationException(character.name+" 缺少人形骨骼");
                    var idle = character.Controller ? character.Controller.animationClips.FirstOrDefault(c=>c.name=="Idle") : null;
                    if (idle)
                    {
                        var pose=AnimationPlayableOutput.Create(graph,"Portrait",animator);
                        pose.SetSourcePlayable(AnimationClipPlayable.Create(graph,idle));
                        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                        graph.Play(); graph.Evaluate(.15f);
                    }
                    var head = animator.GetBoneTransform(HumanBodyBones.Head);
                    Vector3 target=head.position + Vector3.down*.12f;
                    var cam=preview.camera;
                    cam.orthographic=true; cam.orthographicSize=.45f;
                    cam.nearClipPlane=.04f; cam.farClipPlane=12;
                    cam.clearFlags=CameraClearFlags.SolidColor;
                    cam.backgroundColor=character.Faction==Side.Defend ? new Color(.32f,.47f,.59f) : new Color(.51f,.37f,.47f);
                    cam.transform.position=target+Vector3.forward*2.5f;
                    cam.transform.LookAt(target);
                    preview.ambientColor=new Color(.76f,.76f,.82f);
                    preview.lights[0].intensity=1.28f; preview.lights[0].transform.rotation=Quaternion.Euler(25,153,0);
                    preview.lights[1].intensity=.72f; preview.lights[1].transform.rotation=Quaternion.Euler(15,-38,0);
                    preview.BeginStaticPreview(new Rect(0,0,320,576)); preview.Render(true);
                    var result=preview.EndStaticPreview();
                    try { File.WriteAllBytes(output,result.EncodeToPNG()); }
                    finally { Object.DestroyImmediate(result); }
                }
                finally { graph.Destroy(); preview.Cleanup(); }
                AssetDatabase.ImportAsset(output,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(output);
                importer.npotScale=TextureImporterNPOTScale.None; importer.mipmapEnabled=false;
                importer.wrapMode=TextureWrapMode.Clamp; importer.textureCompression=TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
                var settings=new SerializedObject(character);
                var field=settings.FindProperty("_battlePortrait");
                // Re-baking generated files preserves a separately assigned commissioned illustration.
                if (!field.objectReferenceValue || AssetDatabase.GetAssetPath(field.objectReferenceValue)==output)
                    field.objectReferenceValue=AssetDatabase.LoadAssetAtPath<Texture2D>(output);
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("AnimeCS/美术/从当前阵容重拍角色立绘")]
        public static void Bake()
        {
            const string folder = "Assets/Content/UI/Portraits";
            Directory.CreateDirectory(folder);
            var catalog = ContentLibrary.Catalog;
            string[] colors = { "#45515A", "#5C576D", "#496864", "#744F57" };
            string[] weapons = { "ember47", "fold44", "feather08", "longglass96" };
            for (int i=0; i<catalog.SeatOutfits.Count; i++)
            {
                var fit=catalog.SeatOutfits[i];
                if (!fit || !fit.Character || !fit.Character.Model || !fit.Character.Controller) continue;
                var clip=fit.Character.Controller.animationClips.FirstOrDefault(c=>c.name=="Idle");
                if (!clip) continue;
                var preview=new PreviewRenderUtility();
                var graph=PlayableGraph.Create();
                try
                {
                    var cam=preview.camera;
                    cam.nearClipPlane=.025f;cam.farClipPlane=18;cam.clearFlags=CameraClearFlags.SolidColor;
                    ColorUtility.TryParseHtmlString(colors[i%colors.Length],out var color);cam.backgroundColor=color;
                    preview.ambientColor=new Color(.69f,.7f,.74f);
                    preview.lights[0].intensity=1.45f;preview.lights[0].transform.rotation=Quaternion.Euler(32,148,0);
                    preview.lights[1].intensity=1.1f;preview.lights[1].transform.rotation=Quaternion.Euler(23,-25,0);
                    var body=Object.Instantiate(fit.Character.Model); preview.AddSingleGO(body);
                    body.transform.rotation=Quaternion.Euler(0,i%2==0?19:-15,0)*body.transform.rotation;
                    var a=body.GetComponent<Animator>();a.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                    var hands=new WeaponGripRig(a);
                    var output=AnimationPlayableOutput.Create(graph,"Portrait",a);
                    output.SetSourcePlayable(AnimationClipPlayable.Create(graph,clip));graph.Play();graph.Evaluate(.15f);
                    var source=catalog.Find<WeaponDefinition>("weapon."+weapons[i%weapons.Length]);
                    if (source && source.WorldModel)
                    {
                        var mount = new GameObject("展示枪"); preview.AddSingleGO(mount);
                        Object.Instantiate(source.WorldModel,mount.transform,false);
                        hands.Pose(body.transform,mount.transform,source,body.transform.rotation*Quaternion.Euler(18,-19,-6),false);
                    }
                    var name=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(fit.Character.Model));
                    foreach(bool icon in new[]{false,true})
                    {
                        float focus=a.GetBoneTransform(HumanBodyBones.Head).position.y+(icon?.075f:-.25f);
                        cam.transform.position=new Vector3(0,focus+.035f,icon?.72f:1.83f);
                        cam.transform.LookAt(new Vector3(0,focus,0));cam.fieldOfView=icon?27:31;
                        preview.BeginStaticPreview(new Rect(0,0,icon?144:400,icon?144:680));preview.Render(true);
                        var image=preview.EndStaticPreview();
                        File.WriteAllBytes(folder+"/"+name+(icon?"Icon":"")+".png",image.EncodeToPNG());
                        Object.DestroyImmediate(image);
                    }
                }
                finally { graph.Destroy();preview.Cleanup(); }
            }
            AssetDatabase.Refresh();
            foreach(var guid in AssetDatabase.FindAssets("t:Texture2D",new[]{folder}))
            {
                var path=AssetDatabase.GUIDToAssetPath(guid);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                if (!importer.mipmapEnabled && importer.npotScale==TextureImporterNPOTScale.None && importer.textureCompression==TextureImporterCompression.Uncompressed) continue;
                importer.npotScale=TextureImporterNPOTScale.None;
                importer.mipmapEnabled=false; importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
            }
            foreach(var fit in catalog.SeatOutfits)
            {
                var data=new SerializedObject(fit.Character);string file=folder+"/"+fit.Character.name;
                data.FindProperty("_portrait").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Texture2D>(file+".png");
                data.FindProperty("_icon").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Texture2D>(file+"Icon.png");
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
        }
    }
}
