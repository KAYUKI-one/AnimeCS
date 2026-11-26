using System.IO;
using System.Linq;
using AnimeCS.Content;
using AnimeCS.Play;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AnimeCS.Editor
{
    public static class InventoryArtwork
    {
        const string Folder="Assets/AnimeCS/UI/Inventory";
        [MenuItem("AnimeCS/美术/从当前模型拍摄库存缩略图")]
        public static void Bake()
        {
            ContentLibrary.EnsureFolder(Folder);
            var catalog=ContentLibrary.Catalog;
            foreach(var fit in catalog.SeatOutfits)
            {
                var name=fit.Character.name;var s=new SerializedObject(fit.Character);
                foreach(var pair in new[]{("_portrait",".png"),("_icon","Icon.png")})
                    s.FindProperty(pair.Item1).objectReferenceValue=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/AnimeCS/UI/Portraits/"+name+pair.Item2);
                s.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach(var item in catalog.Entries)
            {
                if(item is not WeaponDefinition && item is not WeaponSkinDefinition && item is not WearableDefinition && item is not UtilityDefinition)continue;
                var gun=item as WeaponDefinition ?? (item as WeaponSkinDefinition)?.Weapon;
                var wear=item as WearableDefinition;
                var source=gun?gun.WorldModel:item is UtilityDefinition utility?utility.Model:wear?.Model(false,catalog.Character);
                var preview=new PreviewRenderUtility();
                try
                {
                    GameObject model;
                    if(source){model=Object.Instantiate(source);preview.AddSingleGO(model);}
                    else if(wear && wear.Material){model=GameObject.CreatePrimitive(PrimitiveType.Sphere);preview.AddSingleGO(model);model.GetComponent<Renderer>().sharedMaterial=wear.Material;}
                    else continue;
                    if(gun)AppearancePresenter.ApplySkin(model,gun,item as WeaponSkinDefinition);
                    var renderers=model.GetComponentsInChildren<Renderer>();if(renderers.Length==0)continue;
                    Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                    var cam=preview.camera;
                    var direction=gun?new Vector3(-1,.095f,-.12f):new Vector3(-.20f,.14f,1);
                    cam.transform.position=bounds.center+direction.normalized*3;cam.transform.LookAt(bounds.center);
                    cam.orthographic=true;cam.orthographicSize=Mathf.Max(.05f,Mathf.Max(bounds.extents.y,gun?bounds.extents.z*.51f:bounds.extents.x*.75f)*1.14f);
                    cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.155f,.185f,.192f);cam.nearClipPlane=.01f;cam.farClipPlane=10;
                    preview.ambientColor=new Color(.74f,.76f,.79f);preview.lights[0].intensity=1.7f;preview.lights[0].transform.rotation=Quaternion.Euler(44,145,0);
                    preview.lights[1].intensity=.85f;preview.lights[1].transform.rotation=Quaternion.Euler(15,-34,0);
                    preview.BeginStaticPreview(new Rect(0,0,300,144));preview.Render(true);var image=preview.EndStaticPreview();
                    string path=Folder+"/"+item.Id+".png";File.WriteAllBytes(path,image.EncodeToPNG());Object.DestroyImmediate(image);
                    AssetDatabase.ImportAsset(path);
                    var s=new SerializedObject(item);s.FindProperty("_thumbnail").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if(item is WeaponDefinition || item is UtilityDefinition)s.FindProperty("_glyph").objectReferenceValue=Glyph(preview,renderers,item);
                    s.ApplyModifiedPropertiesWithoutUndo();
                }
                finally{preview.Cleanup();}
            }
            AssetDatabase.SaveAssets();
        }

        static Texture2D Glyph(PreviewRenderUtility preview,Renderer[] renderers,ContentDefinition item)
        {
            var white=new Material(Shader.Find("Universal Render Pipeline/Unlit"));white.SetColor("_BaseColor",Color.white);
            Texture2D image=null;
            try
            {
                foreach(var r in renderers){r.SetPropertyBlock(null);r.sharedMaterials=Enumerable.Repeat(white,r.sharedMaterials.Length).ToArray();}
                preview.camera.backgroundColor=Color.black;
                preview.BeginStaticPreview(new Rect(0,0,220,100));preview.Render(true);image=preview.EndStaticPreview();
                var pixels=image.GetPixels32();for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(255,255,255,pixels[i].r);
                var rgba=new Texture2D(image.width,image.height,TextureFormat.RGBA32,false);
                rgba.SetPixels32(pixels);rgba.Apply();string path=Folder+"/"+item.Id+"-hud.png";File.WriteAllBytes(path,rgba.EncodeToPNG());Object.DestroyImmediate(rgba);
                AssetDatabase.ImportAsset(path);return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            finally{Object.DestroyImmediate(white);if(image)Object.DestroyImmediate(image);}
        }
    }
}