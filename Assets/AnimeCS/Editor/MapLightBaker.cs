using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimeCS.Content;
using AnimeCS.Play;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AnimeCS.Editor
{
    public static class MapLightBaker
    {
        const string Folder="Assets/AnimeCS/Art/MapLighting";
        internal static void PrepareGrade()
        {
            ContentLibrary.EnsureFolder(Folder);
            const string path=Folder+"/CompetitiveDay.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if(!profile)
            {
                profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,path);
                var tone=profile.Add<Tonemapping>();tone.mode.Override(TonemappingMode.ACES);
                var bloom=profile.Add<Bloom>();bloom.threshold.Override(1.35f);bloom.intensity.Override(.065f);bloom.scatter.Override(.42f);
                var grade=profile.Add<ColorAdjustments>();grade.postExposure.Override(.12f);grade.contrast.Override(3);grade.saturation.Override(-3);
                foreach(var c in profile.components){c.name=c.GetType().Name;AssetDatabase.AddObjectToAsset(c,profile);}
                EditorUtility.SetDirty(profile);
            }
            var style=AssetDatabase.LoadAssetAtPath<MatchPresentation>("Assets/AnimeCS/UI/Competitive.asset");
            style.daylightGrade=profile;EditorUtility.SetDirty(style);AssetDatabase.SaveAssets();
        }

        public static void Prepare(MapDefinition map)
        {
            if(!map || !map.Geometry)throw new System.ArgumentException("选择带模型的地图。");
            PrepareGrade();
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            string sub=Folder+"/"+map.name;
            ContentLibrary.EnsureFolder(sub);
            string path=sub+"/"+map.name+".unity";
            try
            {
                SceneManager.SetActiveScene(scene);
                var geo=(GameObject)PrefabUtility.InstantiatePrefab(map.Geometry,scene);
                if(!geo.GetComponentInChildren<Collider>())
                    foreach(var mesh in geo.GetComponentsInChildren<MeshFilter>()) if(mesh.sharedMesh) mesh.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh.sharedMesh;
                foreach(var r in geo.GetComponentsInChildren<MeshRenderer>())
                {
                    if(!r.receiveShadows || r.bounds.size.magnitude>800)continue;
                    var filter=r.GetComponent<MeshFilter>(); if(!filter || !filter.sharedMesh)continue;
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject,GameObjectUtility.GetStaticEditorFlags(r.gameObject)|StaticEditorFlags.ContributeGI);
                    r.receiveGI=filter.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord1) ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
                    r.scaleInLightmap=.7f;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                }
                var sun=new GameObject("Bake sunlight").AddComponent<Light>();
                var style=AssetDatabase.LoadAssetAtPath<MatchPresentation>("Assets/AnimeCS/UI/Competitive.asset");
                sun.type=LightType.Directional;sun.lightmapBakeType=LightmapBakeType.Mixed;sun.color=new Color(1,.964f,.895f);sun.intensity=style.sunIntensity;
                sun.transform.rotation=Object.FindObjectsByType<Light>().FirstOrDefault(l=>l!=sun&&l.type==LightType.Directional)?.transform.rotation ?? Quaternion.Euler(52,-32,0);
                sun.shadows=LightShadows.Soft;RenderSettings.sun=sun;RenderSettings.skybox=style.daylightSky;
                RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=style.skyLight;
                RenderSettings.ambientEquatorColor=style.horizonLight;RenderSettings.ambientGroundColor=style.groundLight;
                bool dust=map.Id=="map.dust2";
                if(dust)
                {
                    sun.transform.rotation=Quaternion.Euler(50,-43,0);
                    sun.color=new Color(1,.898f,.792f);sun.intensity=2.35f;sun.bounceIntensity=1.75f;
                    RenderSettings.ambientSkyColor=new Color(.84f,.90f,.98f);
                    RenderSettings.ambientEquatorColor=new Color(.78f,.79f,.80f);
                    RenderSettings.ambientGroundColor=new Color(.56f,.51f,.44f);
                    CreateDustGrade(map,style,sub);
                }
                var settingsPath=sub+"/Indirect.asset";
                var settings=AssetDatabase.LoadAssetAtPath<LightingSettings>(settingsPath);
                if(!settings){settings=new LightingSettings();AssetDatabase.CreateAsset(settings,settingsPath);}
                settings.bakedGI=true;settings.realtimeGI=false;settings.mixedBakeMode=MixedLightingMode.IndirectOnly;
                settings.lightmapper=LightingSettings.Lightmapper.ProgressiveGPU;
                settings.lightmapResolution=6;settings.lightmapMaxSize=1024;settings.directSampleCount=16;
                settings.indirectSampleCount=dust?128:64;settings.environmentSampleCount=dust?128:64;settings.maxBounces=dust?3:2;EditorUtility.SetDirty(settings);
                Lightmapping.lightingSettings=settings;
                var locations=new List<Vector3>();
                foreach(Side side in new[]{Side.Attack,Side.Defend})
                {
                    for(int i=0;i<map.SpawnCount(side);i++) locations.Add(map.Spawn(side,i));
                    foreach(Site site in new[]{Site.A,Site.B})locations.AddRange(map.Route(side,site));
                }
                locations.Add(map.SitePosition(Site.A));locations.Add(map.SitePosition(Site.B));
                var bounds=new Bounds(locations[0],Vector3.zero);
                foreach(var point in locations)bounds.Encapsulate(point);
                var points=new List<Vector3>();
                Physics.SyncTransforms();
                var colliders=new HashSet<Collider>(geo.GetComponentsInChildren<Collider>());
                for(float x=bounds.min.x-4;x<=bounds.max.x+4;x+=4.5f)
                    for(float z=bounds.min.z-4;z<=bounds.max.z+4;z+=4.5f)
                    {
                        var origin=new Vector3(x,bounds.max.y+12,z);
                        foreach(var hit in Physics.RaycastAll(origin,Vector3.down,bounds.size.y+22))
                            if(hit.normal.y>.72f && colliders.Contains(hit.collider))
                                foreach(float y in new[]{.35f,1.55f,3.1f}) points.Add(hit.point+Vector3.up*y);
                    }
                foreach(var p in locations){points.Add(p+Vector3.up*.55f);points.Add(p+Vector3.up*2f);}
                new GameObject("Irradiance samples").AddComponent<LightProbeGroup>().probePositions=points.Distinct().ToArray();
                foreach(var side in new[]{Side.Attack,Side.Defend})
                {
                    var rp=new GameObject("Reflections "+side).AddComponent<ReflectionProbe>();
                    rp.transform.position=map.Spawn(side,0)+Vector3.up*1.55f;rp.mode=ReflectionProbeMode.Baked;
                    rp.size=new Vector3(30,12,30);rp.resolution=128;rp.boxProjection=true;rp.blendDistance=3;rp.hdr=true;
                }
                if(!EditorSceneManager.SaveScene(scene,path))throw new IOException(path);
                AssetDatabase.SaveAssets();
            }
            finally {SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);}
        }

        private static void CreateDustGrade(MapDefinition map,MatchPresentation style,string folder)
        {
            var path=folder+"/SunlitStreet.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if(!profile)
            {
                if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(style.daylightGrade),path))throw new IOException(path);
                profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            }
            if(profile.TryGet<ColorAdjustments>(out var adjustment))
            {
                adjustment.postExposure.Override(.30f);adjustment.contrast.Override(2);adjustment.saturation.Override(2);
                EditorUtility.SetDirty(adjustment);
            }
            var so=new SerializedObject(map);
            so.FindProperty("_combatGrade").objectReferenceValue=profile;so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void Bake(MapDefinition map)
        {
            Prepare(map);
            var path=Folder+"/"+map.name+"/"+map.name+".unity";
            void Finished()
            {
                Lightmapping.bakeCompleted-=Finished;
                EditorApplication.delayCall+=()=>{AssetDatabase.Refresh();Bind(map);};
            }
            Lightmapping.bakeCompleted+=Finished;
            try { Lightmapping.BakeMultipleScenes(new[]{path}); }
            catch {Lightmapping.bakeCompleted-=Finished;throw;}
        }

        // Bind only once the lightmapper has written the actual scene data.
        public static void Bind(MapDefinition map)
        {
            var folder=Folder+"/"+map.name;
            if(AssetDatabase.FindAssets("t:LightingDataAsset",new[]{folder}).Length==0)
                throw new IOException("地图还没有烘焙光照数据。");
            var path=folder+"/"+map.name+".unity";
            var data=new SerializedObject(map);data.FindProperty("_lightingScenePath").stringValue=path;data.ApplyModifiedPropertiesWithoutUndo();
            FoundationBuild.IncludeScene(path);AssetDatabase.SaveAssets();
        }
    }
}
