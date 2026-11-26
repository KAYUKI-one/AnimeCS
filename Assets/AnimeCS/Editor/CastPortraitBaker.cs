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
        [MenuItem("AnimeCS/美术/从当前阵容重拍角色立绘")]
        public static void Bake()
        {
            const string folder = "Assets/AnimeCS/UI/Portraits";
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
            foreach(var guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/AnimeCS/UI"}))
            {
                var path=AssetDatabase.GUIDToAssetPath(guid);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                if (!importer.mipmapEnabled && importer.npotScale==TextureImporterNPOTScale.None && importer.textureCompression==TextureImporterCompression.Uncompressed) continue;
                importer.npotScale=TextureImporterNPOTScale.None;
                importer.mipmapEnabled=false; importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
            }
        }
    }
}
