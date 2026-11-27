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
        const string Folder="Assets/Content/UI/Inventory";
        [MenuItem("AnimeCS/美术/从当前模型拍摄库存缩略图")]
        public static void Bake()
        {
            ContentLibrary.EnsureFolder(Folder);
            var catalog=ContentLibrary.Catalog;
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
                    var matte=Mask(preview,renderers,image.width,image.height);
                    try
                    {
                        var color=image.GetPixels32();
                        var alpha=matte.GetPixels32();
                        var background=color[0];
                        for (int i=0;i<color.Length;i++)
                        {
                            byte coverage=alpha[i].r;
                            if (coverage==0) { color[i]=new Color32(0,0,0,0); continue; }
                            byte Unmix(byte value,byte backdrop) => (byte)Mathf.Clamp(Mathf.RoundToInt((value*255f-backdrop*(255-coverage))/coverage),0,255);
                            color[i]=new Color32(Unmix(color[i].r,background.r),Unmix(color[i].g,background.g),Unmix(color[i].b,background.b),coverage);
                        }
                        var thumbnail=SaveRgba(item.Id,image.width,image.height,color);
                        var record=new SerializedObject(item);
                        record.FindProperty("_thumbnail").objectReferenceValue=thumbnail;
                        if (item is WeaponDefinition || item is UtilityDefinition)
                        {
                            for (int i=0;i<alpha.Length;i++) alpha[i]=new Color32(255,255,255,alpha[i].r);
                            record.FindProperty("_glyph").objectReferenceValue=SaveRgba(item.Id+"-hud",image.width,image.height,alpha);
                        }
                        record.ApplyModifiedPropertiesWithoutUndo();
                    }
                    finally {Object.DestroyImmediate(image);Object.DestroyImmediate(matte);}
                }
                finally{preview.Cleanup();}
            }
            AssetDatabase.SaveAssets();
        }

        static Texture2D SaveRgba(string name,int width,int height,Color32[] pixels)
        {
            var rgba=new Texture2D(width,height,TextureFormat.RGBA32,false);
            string path=Folder+"/"+name+".png";
            try {rgba.SetPixels32(pixels);rgba.Apply();File.WriteAllBytes(path,rgba.EncodeToPNG());}
            finally {Object.DestroyImmediate(rgba);}
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D Mask(PreviewRenderUtility preview,Renderer[] renderers,int width,int height)
        {
            var white=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                white.SetColor("_BaseColor",Color.white);
                foreach(var renderer in renderers)
                {
                    renderer.SetPropertyBlock(null);
                    renderer.sharedMaterials=Enumerable.Repeat(white,renderer.sharedMaterials.Length).ToArray();
                }
                preview.camera.backgroundColor=Color.black;
                preview.BeginStaticPreview(new Rect(0,0,width,height));
                preview.Render(true);
                return preview.EndStaticPreview();
            }
            finally{Object.DestroyImmediate(white);}
        }
    }
}
