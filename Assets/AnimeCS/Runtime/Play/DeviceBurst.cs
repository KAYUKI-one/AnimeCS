using UnityEngine;
using UnityEngine.Rendering;

namespace AnimeCS.Play
{
    internal static class DeviceBurst
    {
        internal static void Show(Vector3 point,Transform parent,Material sheet)
        {
            var obj=new GameObject("装置焰云");
            obj.transform.SetParent(parent,true);obj.transform.position=point+Vector3.up*1.35f;
            var system=obj.AddComponent<ParticleSystem>();system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=system.main;main.loop=false;main.playOnAwake=false;main.duration=1.15f;main.startLifetime=1.1f;
            main.startSpeed=0;main.startSize=7.4f;main.maxParticles=3;main.stopAction=ParticleSystemStopAction.Destroy;
            var emission=system.emission;emission.enabled=false;var shape=system.shape;shape.enabled=false;
            var animation=system.textureSheetAnimation;animation.enabled=true;animation.numTilesX=animation.numTilesY=8;
            animation.frameOverTime=new ParticleSystem.MinMaxCurve(1f,AnimationCurve.Linear(0,0,1,.999f));
            var renderer=obj.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=sheet;renderer.maxParticleSize=1;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            system.Play();system.Emit(1);
        }
    }
}
