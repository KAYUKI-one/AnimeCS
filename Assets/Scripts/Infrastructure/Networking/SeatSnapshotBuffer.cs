using System;
using UnityEngine;

namespace AnimeCS.Match
{
    public sealed class SeatSnapshotBuffer
    {
        private struct Frame { public int Tick; public Vector3 Position; public float Yaw,Pitch; }
        private readonly Frame[] _frames=new Frame[16];
        private int _head,_count;
        public void Clear(){_head=_count=0;}
        private Frame At(int age)=>_frames[(_head-1-age+2*_frames.Length)%_frames.Length];
        public bool Push(int tick,Vector3 position,float yaw,float pitch)
        {
            if((_count>0 && tick<=At(0).Tick) || tick<0 || !float.IsFinite(position.x) || !float.IsFinite(position.y) ||
                !float.IsFinite(position.z)||!float.IsFinite(yaw)||!float.IsFinite(pitch))return false;
            _frames[_head]=new Frame{Tick=tick,Position=position,Yaw=yaw,Pitch=pitch};
            _head=(_head+1)%_frames.Length;_count=Math.Min(_count+1,_frames.Length);return true;
        }
        public bool Sample(float renderTick,out Vector3 pos,out float yaw,out float pitch) =>
            Sample((double)renderTick,out pos,out yaw,out pitch);
        public bool Sample(double renderTick,out Vector3 pos,out float yaw,out float pitch)
        {
            pos=default;yaw=pitch=0;if(_count==0 || double.IsNaN(renderTick) || double.IsInfinity(renderTick))return false;
            var right=At(0);var left=right;
            renderTick=Math.Min(renderTick,right.Tick+2d);
            for(int age=1;age<_count;age++)
            {
                left=At(age);
                if(left.Tick<=renderTick)break;
                right=left;
            }
            float t=right.Tick==left.Tick?0:(float)Math.Max(0,(renderTick-left.Tick)/(right.Tick-left.Tick));
            pos=Vector3.LerpUnclamped(left.Position,right.Position,t);
            // A turn can stop between packets. Hold the last confirmed look while position extrapolates.
            float turn=Mathf.Min(t,1f);
            yaw=Mathf.Repeat(left.Yaw+Mathf.DeltaAngle(left.Yaw,right.Yaw)*turn,360);
            pitch=Mathf.Clamp(Mathf.LerpUnclamped(left.Pitch,right.Pitch,turn),-82,82);return true;
        }
    }
}
