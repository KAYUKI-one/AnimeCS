using System;
using UnityEngine;

namespace AnimeCS.Match
{
    // The host records authoritative hit volumes after movement. Queries never move live colliders.
    public sealed class HitboxHistory
    {
        private struct Frame
        {
            public int Tick, Life;
            public Vector3 Head, Body;
            public float Height;
            public bool Alive;
        }
        private readonly Frame[,] _frames;
        private readonly int[] _life;
        private readonly bool[] _alive;
        private readonly int _window;
        private readonly float _headRadius, _bodyRadius;

        public HitboxHistory(int tickRate, int seats, float headRadius=.15f, float bodyRadius=.26f)
        {
            if (tickRate<1 || seats<1) throw new ArgumentOutOfRangeException();
            _window=Mathf.Max(1,Mathf.FloorToInt(.2f*tickRate));
            _frames=new Frame[_window+1,seats]; _life=new int[seats]; _alive=new bool[seats];
            _headRadius=headRadius; _bodyRadius=bodyRadius;
        }
        public void Invalidate(int seat) { _alive[seat]=false; _life[seat]++; }
        public void Record(int tick,int seat,Vector3 head,Vector3 body,float bodyHeight,bool alive)
        {
            if(_alive[seat]!=alive) _life[seat]++;
            _alive[seat]=alive;
            _frames[tick%(_window+1),seat]=new Frame{Tick=tick, Life=_life[seat], Alive=alive,
                Head=head,Body=body,Height=Mathf.Max(2*_bodyRadius,bodyHeight)};
        }
        public int ClampedTick(int serverTick,int requestedTick)=>Mathf.Clamp(requestedTick,Math.Max(0,serverTick-_window),serverTick);
        public Vector3 BodyAt(int tick,int seat)=>_frames[tick%(_window+1),seat].Body;
        public float HeightAt(int tick,int seat)=>_frames[tick%(_window+1),seat].Height;

        public bool Trace(int serverTick,int requestedTick,int shooter,Ray ray,float maxDistance,out int seat,out bool headshot,out float distance)
        {
            int tick=ClampedTick(serverTick,requestedTick);
            seat=-1; headshot=false; distance=maxDistance;
            for(int i=0;i<_alive.Length;i++)
            {
                var f=_frames[tick%(_window+1),i];
                if(i==shooter || !_alive[i] || !f.Alive || f.Life!=_life[i] || f.Tick!=tick) continue;
                float head=Sphere(ray,f.Head,_headRadius);
                float half=Mathf.Max(0,f.Height*.5f-_bodyRadius);
                var low=f.Body-Vector3.up*half; var high=f.Body+Vector3.up*half;
                float body=Mathf.Min(Sphere(ray,low,_bodyRadius),Sphere(ray,high,_bodyRadius));
                Vector3 o=ray.origin-f.Body,d=ray.direction;
                float a=d.x*d.x+d.z*d.z,b=o.x*d.x+o.z*d.z,c=o.x*o.x+o.z*o.z-_bodyRadius*_bodyRadius;
                float disc=b*b-a*c;
                if(a>1e-7f && disc>=0)
                {
                    float t=(-b-Mathf.Sqrt(disc))/a;
                    if(t>=0 && Mathf.Abs(o.y+d.y*t)<=half) body=Mathf.Min(body,t);
                }
                float near=Mathf.Min(head,body);
                if(near>=distance)continue;
                distance=near; seat=i; headshot=head<=body;
            }
            return seat>=0;
        }
        private static float Sphere(Ray ray,Vector3 center,float radius)
        {
            var delta=ray.origin-center;
            float b=Vector3.Dot(delta,ray.direction),c=delta.sqrMagnitude-radius*radius;
            float disc=b*b-c;
            if(disc<0)return float.PositiveInfinity;
            float root=Mathf.Sqrt(disc);
            if(-b+root<0)return float.PositiveInfinity;
            return Mathf.Max(0,-b-root);
        }
    }

    public static class ShotPattern
    {
        // Unsigned integer avalanche is stable across managed and IL2CPP players.
        private static uint Mix(uint x)
        {
            unchecked { x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;return x^(x>>16); }
        }
        public static Vector2 Sample(int matchSeed,int seat,int sequence,float spreadDegrees,bool centerWeighted=false)
        {
            uint key=Mix((uint)matchSeed ^ Mix((uint)seat+1) ^ Mix((uint)sequence+0x85ebca6bu));
            float angle=(key>>8)*(2f*Mathf.PI/16777216f);
            float sample=(Mix(key+0x9e3779b9u)>>8)/16777216f;
            float radius=(centerWeighted?sample:Mathf.Sqrt(sample))*spreadDegrees;
            return new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
        }
    }
}
