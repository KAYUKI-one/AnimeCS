using UnityEngine;
using UnityEngine.Rendering;

namespace AnimeCS.Play
{
    // The density lobes drive both the sight test and the particles. Shot openings age on the match clock.
    internal sealed class SmokeField
    {
        private const int Count=29, Holes=6;
        private readonly Vector3 _source;
        private readonly float _radius;
        private readonly Vector3[] _offset=new Vector3[Count], _center=new Vector3[Count];
        private readonly float[] _size=new float[Count];
        private readonly Vector4[] _entry=new Vector4[Holes], _exit=new Vector4[Holes];
        private readonly float[] _opened=new float[Holes];
        private readonly ParticleSystem.Particle[] _puffs=new ParticleSystem.Particle[Count];
        private readonly GameObject _root;
        private readonly ParticleSystem _system;
        private readonly ParticleSystemRenderer _renderer;
        private readonly MaterialPropertyBlock _properties=new();
        private float _age,_growth;
        private int _next;
        internal SmokeField(Vector3 source,float radius,int seed,Transform parent,Material material)
        {
            _source=source; _radius=radius;
            for(int i=0;i<Holes;i++)_opened[i]=-100f;
            var random=new System.Random(seed);
            for(int i=0;i<Count;i++)
            {
                float angle=i*2.39996f+(float)random.NextDouble()*.27f;
                float ring=i==0?0:Mathf.Sqrt((i%10)/9f)*radius*.70f;
                var flat=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*ring;
                if(ring>.02f && Physics.Raycast(source+Vector3.up*.35f,flat.normalized,out var wall,ring,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                    flat=flat.normalized*Mathf.Max(0,wall.distance-.23f);
                flat.y=radius*(.21f+.12f*(i/10)+.17f*(float)random.NextDouble());
                _offset[i]=flat;_size[i]=radius*(.45f+.12f*(float)random.NextDouble());
                _puffs[i].startLifetime=100;_puffs[i].remainingLifetime=99;
                _puffs[i].randomSeed=(uint)(i+1);_puffs[i].rotation=360f*(float)random.NextDouble();
            }
            _root=new GameObject("烟幕粒子");_root.transform.SetParent(parent,true);_root.transform.position=source;
            _system=_root.AddComponent<ParticleSystem>();_system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=_system.main;main.playOnAwake=false;main.loop=false;main.maxParticles=Count;main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=_system.emission;emission.enabled=false;var shape=_system.shape;shape.enabled=false;
            _renderer=_root.GetComponent<ParticleSystemRenderer>();_renderer.renderMode=ParticleSystemRenderMode.Billboard;
            _renderer.sortMode=ParticleSystemSortMode.Distance;_renderer.shadowCastingMode=ShadowCastingMode.Off;_renderer.receiveShadows=false;
            if(material)_renderer.sharedMaterial=material;else _renderer.enabled=false;
            _system.Play();_system.Pause();Advance(0,1);
        }

        internal void Advance(float seconds,float remaining)
        {
            _age=seconds;
            _growth=Mathf.SmoothStep(.22f,1f,Mathf.Clamp01(seconds/.48f))*Mathf.SmoothStep(.03f,1f,Mathf.Clamp01(remaining/.85f));
            for(int i=0;i<Count;i++)
            {
                var wisp=new Vector3(Mathf.Sin(seconds*.57f+i),Mathf.Sin(seconds*.83f+i*.5f)*.55f,Mathf.Cos(seconds*.51f-i))*.10f;
                _center[i]=_source+(_offset[i]+wisp)*_growth;
                _puffs[i].position=_center[i];_puffs[i].startSize=_size[i]*_growth*2.5f;
                float light=.65f+.19f*_offset[i].y/_radius+.035f*Mathf.Sin(i*2.1f);
                _puffs[i].startColor=new Color(light*1.04f,light,light*.96f,.88f*Mathf.Clamp01(remaining*1.8f));
            }
            _system.SetParticles(_puffs,Count);
            for(int i=0;i<Holes;i++)_entry[i].w=HoleRadius(i);
            _properties.SetVectorArray("_HoleA",_entry);_properties.SetVectorArray("_HoleB",_exit);_renderer.SetPropertyBlock(_properties);
        }

        private float HoleRadius(int i)=>.29f*(1-Mathf.SmoothStep(0,1,Mathf.Clamp01((_age-_opened[i])/.42f)));
        private static float DistanceToSegment(Vector3 point,Vector3 a,Vector3 b)
        {
            var delta=b-a;return (point-a-delta*Mathf.Clamp01(Vector3.Dot(point-a,delta)/Mathf.Max(.00001f,delta.sqrMagnitude))).sqrMagnitude;
        }
        internal void Punch(Vector3 start,Vector3 end)
        {
            if((end-start).sqrMagnitude<.001f || DistanceToSegment(_source+Vector3.up*_radius*.40f,start,end)>_radius*_radius*1.8f)return;
            int i=_next++%Holes;_entry[i]=start;_exit[i]=end;_opened[i]=_age;_entry[i].w=HoleRadius(i);
            _properties.SetVectorArray("_HoleA",_entry);_properties.SetVectorArray("_HoleB",_exit);_renderer.SetPropertyBlock(_properties);
        }
        private bool Dense(Vector3 point)
        {
            for(int j=0;j<Holes;j++)
            {
                float width=HoleRadius(j);
                if(width>.02f && DistanceToSegment(point,_entry[j],_exit[j])<width*width)return false;
            }
            for(int i=0;i<Count;i++)
            {
                var delta=point-_center[i];delta.y*=1.12f;float r=_size[i]*_growth;
                if(delta.sqrMagnitude<r*r)return true;
            }
            return false;
        }
        internal bool Occludes(Vector3 start,Vector3 end)
        {
            var center=_source+Vector3.up*_radius*.40f; var delta=end-start;float length=delta.magnitude;
            if(length<.001f)return Dense(start);
            var dir=delta/length;
            float project=Vector3.Dot(center-start,dir); float range=_radius*1.36f;
            if(project+range<0 || project-range>length || (start+dir*project-center).sqrMagnitude>range*range)return false;
            float from=Mathf.Max(0,project-range), to=Mathf.Min(length,project+range);
            for(float d=from;d<to;d+=.20f)if(Dense(start+dir*d))return true;
            return Dense(start+dir*to);
        }
        internal void Dispose()=>AppearancePresenter.Retire(_root);
    }
}

