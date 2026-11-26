using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimeCS.Content;
using AnimeCS.Play;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimeCS.Editor
{
    public static class DustDemoImport
    {
        const string Source = "Assets/Content/Art/Maps/DustII/";
        const string Maps = "Assets/Content/Data/Maps/";
        static readonly Vector3[] T = { new(20.209f,3.828f,-20.888f),new(18.754f,3.828f,-21.781f),new(21.239f,3.828f,-19.321f),new(20.488f,3.828f,-17.700f),new(19.199f,3.828f,-16.695f) };
        static readonly Vector3[] CT = { new(-53.402f,-2.769f,9.706f),new(-53.315f,-2.769f,11.553f),new(-54.991f,-2.769f,8.188f),new(-57.169f,-2.769f,8.836f),new(-58.118f,-2.769f,10.706f) };
        static readonly Vector3 A=new(-62.992f,2.438f,28.245f), B=new(-68.072f,.08f,-39.014f);
        static Vector3 Ground(Vector3 point)
        {
            if (!NavMesh.SamplePosition(point,out var hit,3f,NavMesh.AllAreas)) throw new InvalidOperationException("找不到可行走地面 " + point);
            return hit.position;
        }

        [MenuItem("AnimeCS/美术/烘焙炙热沙城 II 地图")]
        public static void Bake()
        {
            var visual=AssetDatabase.LoadAssetAtPath<GameObject>(Source+"DustII.gltf");
            var hull=AssetDatabase.LoadAssetAtPath<GameObject>(Source+"DustIISolid.gltf");
            if (!visual || !hull) throw new InvalidOperationException("地图的外观与碰撞 glTF 尚未导入。");
            var scene=EditorSceneManager.NewPreviewScene();
            var root=new GameObject("Dust II"); SceneManager.MoveGameObjectToScene(root,scene);
            NavMeshDataInstance handle=default; NavMeshData nav=null;
            try
            {
                var world=(GameObject)PrefabUtility.InstantiatePrefab(visual,scene);world.transform.SetParent(root.transform,false);
                world.AddComponent<AnimeCS.Play.ImmutableMapMeshes>();
                var horizon=AssetDatabase.LoadAssetAtPath<GameObject>(Source+"Horizon/Horizon.gltf");
                if(horizon)
                {
                    var outside=(GameObject)PrefabUtility.InstantiatePrefab(horizon,scene);outside.transform.SetParent(root.transform,false);
                    outside.AddComponent<AnimeCS.Play.ImmutableMapMeshes>();
                    // Source 2 sky_camera origin and 1:16 world scale, mapped into Unity coordinates.
                    outside.transform.localScale=Vector3.one*16;outside.transform.localPosition=-new Vector3(7.8232f,5.1308f,-.2032f)*16;
                    foreach(var r in outside.GetComponentsInChildren<Renderer>()){r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;}
                    DustSurfaceImport.ApplyHorizonGround(outside);
                }
                var staticRoot=new GameObject("Map collision");staticRoot.transform.SetParent(root.transform,false);
                var sources=new List<NavMeshBuildSource>();
                foreach (var tile in hull.GetComponentsInChildren<MeshFilter>(true))
                {
                    var go=new GameObject(tile.name);go.transform.SetParent(staticRoot.transform,false);
                    go.transform.SetPositionAndRotation(tile.transform.position,tile.transform.rotation);go.transform.localScale=tile.transform.lossyScale;
                    go.layer=tile.name.StartsWith("Clip",StringComparison.Ordinal)?2:0;
                    var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=tile.sharedMesh;
                    sources.Add(new NavMeshBuildSource { shape=NavMeshBuildSourceShape.Mesh,sourceObject=tile.sharedMesh, transform=go.transform.localToWorldMatrix, area=0 });
                }
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,"Assets/Content/Prefabs/Maps/DustII.prefab");
                var settings=NavMesh.GetSettingsByIndex(0);
                settings.agentRadius=.42f;settings.agentHeight=1.72f;settings.agentClimb=.28f;settings.agentSlope=45;settings.overrideVoxelSize=true;settings.voxelSize=.10f;
                nav=NavMeshBuilder.BuildNavMeshData(settings,sources,new Bounds(new Vector3(-28,1,-5),new Vector3(152,34,156)),Vector3.zero,Quaternion.identity);
                if (!nav) throw new InvalidOperationException("导航网格烘焙失败。");
                handle=NavMesh.AddNavMeshData(nav);
                var catalog=ContentLibrary.Catalog;
                var map=CastArtImport.Definition<MapDefinition>(catalog,"Maps","DustII","map.dust2","炙热沙城 II");
                var data=new SerializedObject(map);
                void Vectors(string field,IEnumerable<Vector3> points)
                {
                    var values=points.ToArray();var list=data.FindProperty(field);list.arraySize=values.Length;
                    for(int i=0;i<values.Length;i++) list.GetArrayElementAtIndex(i).vector3Value=values[i];
                }
                void Angles(string field,float[] values)
                {
                    var list=data.FindProperty(field);list.arraySize=values.Length;
                    for(int i=0;i<values.Length;i++)list.GetArrayElementAtIndex(i).floatValue=values[i];
                }
                var siteA=Ground(A);var siteB=Ground(B);
                var navPath=Maps+"DustIINavigation.asset";
                var savedNav=AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath);
                if(savedNav){EditorUtility.CopySerialized(nav,savedNav);EditorUtility.SetDirty(savedNav);}
                else{savedNav=nav;AssetDatabase.CreateAsset(nav,navPath);}
                data.FindProperty("_navigation").objectReferenceValue=savedNav;
                data.FindProperty("_geometry").objectReferenceValue=prefab;
                Vectors("_attackSpawns",T.Select(v=>Ground(v)+Vector3.up*.065f));
                Vectors("_defendSpawns",CT.Select(v=>Ground(v)+Vector3.up*.065f));
                Angles("_attackYaw",new[]{-107f,-37f,-128.5f,-178f,-207.5f});Angles("_defendYaw",new[]{-101f,-129f,-37.5f,-335f,-307.5f});
                data.FindProperty("_siteA").vector3Value=siteA; data.FindProperty("_siteB").vector3Value=siteB;
                data.FindProperty("_siteRadius").floatValue=4f;
                foreach(var team in new[]{("attack",T),("defend",CT)})
                foreach(var goal in new[]{("A",siteA),("B",siteB)})
                {
                    var path=new NavMeshPath();
                    if(!NavMesh.CalculatePath(Ground(team.Item2[0]),goal.Item2,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)
                        throw new InvalidOperationException("路线不通 "+team.Item1+" → "+goal.Item1);
                    Vectors("_"+team.Item1+"Route"+goal.Item1,path.corners.Skip(1));
                }
                var regions=new[]{("匪家",T[0]),("警家",CT[0]),("A 点",siteA),("B 点",siteB),("中路",new Vector3(-30,0,-4)),("A 大道",new Vector3(-30,0,35)),("B 洞",new Vector3(-33,0,-50))};
                var loc=data.FindProperty("_regions");loc.arraySize=regions.Length;
                for(int i=0;i<regions.Length;i++){var item=loc.GetArrayElementAtIndex(i);item.FindPropertyRelative("Name").stringValue=regions[i].Item1;item.FindPropertyRelative("Center").vector3Value=regions[i].Item2;}
                float side=1024*4.4f*.0254f;
                var center=new Vector2(-3239f*.0254f+side*.5f,-2476f*.0254f+side*.5f);var span=Vector2.one*side;
                data.FindProperty("_radarYaw").floatValue=90;
                data.FindProperty("_radarCenter").vector2Value=center;data.FindProperty("_radarExtent").vector2Value=span;
                data.FindProperty("_radar").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Texture2D>(Source+"Overhead.png");
                data.ApplyModifiedPropertiesWithoutUndo();
                var style=AssetDatabase.LoadAssetAtPath<MatchPresentation>("Assets/Content/UI/Competitive.asset");
                string skyPath=Maps+"Daylight.mat";
                var sky=AssetDatabase.LoadAssetAtPath<Material>(skyPath);
                var shader=Shader.Find("Skybox/Cubemap");
                if (!sky) { sky=new Material(shader);AssetDatabase.CreateAsset(sky,skyPath); }
                sky.shader=shader;
                var importer=(TextureImporter)AssetImporter.GetAtPath(Source+"Daylight.exr");
                var textureSettings=new TextureImporterSettings();importer.ReadTextureSettings(textureSettings);
                if(textureSettings.textureShape!=TextureImporterShape.TextureCube || textureSettings.cubemapConvolution!=TextureImporterCubemapConvolution.Specular)
                {
                    textureSettings.textureShape=TextureImporterShape.TextureCube;textureSettings.generateCubemap=TextureImporterGenerateCubemap.Cylindrical;
                    textureSettings.cubemapConvolution=TextureImporterCubemapConvolution.Specular;importer.SetTextureSettings(textureSettings);importer.SaveAndReimport();
                }
                var panorama=AssetDatabase.LoadAssetAtPath<Cubemap>(Source+"Daylight.exr");sky.SetTexture("_Tex",panorama);
                sky.SetFloat("_Exposure",1.06f);style.sunIntensity=1.75f;style.skyLight=new Color(.84f,.87f,.90f);style.horizonLight=new Color(.69f,.71f,.73f);style.groundLight=new Color(.44f,.41f,.37f);
                style.daylightSky=sky;EditorUtility.SetDirty(sky);EditorUtility.SetDirty(style);AssetDatabase.SaveAssets();
            }
            finally { if(handle.valid)handle.Remove();if(nav && !AssetDatabase.Contains(nav))Object.DestroyImmediate(nav);EditorSceneManager.ClosePreviewScene(scene); }
            if(File.Exists(Source+"SurfacePaint.bin")) DustSurfaceImport.Synchronize();
        }

    }
}
