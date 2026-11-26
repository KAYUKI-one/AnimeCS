using System;
using UnityEngine;

namespace AnimeCS.Match
{
    // Called on the host simulation thread. Timestamps and RTT come from the host's transport clock.
    public sealed class HostSeatInput : ISeatDriver
    {
        private int _round=-1,_serverTick,_rewind,_sequence=-1,_receivedAt=-100,_ack;
        private SeatIntent _latest;
        private bool _fresh;
        public void ObserveRound(int round,int serverTick,float rttSeconds)
        {
            if(_round!=round)
            {
                _round=round;_sequence=-1;_fresh=false;_latest=default;_receivedAt=-100;
            }
            _serverTick=serverTick;
            float rtt=float.IsFinite(rttSeconds)?Mathf.Clamp(rttSeconds,0,.4f):0;
            _rewind=Mathf.Min(MatchRules.TickRate/5,Mathf.CeilToInt(rtt*.5f*MatchRules.TickRate)+2);
        }
        public bool Receive(int sequence,int acknowledgedTick,SeatIntent intent)
        {
            if (_round<0 || sequence<=_sequence || acknowledgedTick>_serverTick || acknowledgedTick<0 ||
                !float.IsFinite(intent.Yaw) || !float.IsFinite(intent.Pitch) || !float.IsFinite(intent.Move.x) ||
                !float.IsFinite(intent.Move.y) || !float.IsFinite(intent.BrakeTap.x) || !float.IsFinite(intent.BrakeTap.y)) return false;
            // Within a host tick keep the last axes and retain press edges until consumed.
            if(_fresh)
            {
                intent.FirePressed|=_latest.FirePressed; intent.Reload|=_latest.Reload; intent.Jump|=_latest.Jump;
                intent.Zoom|=_latest.Zoom; intent.DropHeld|=_latest.DropHeld; intent.Inspect|=_latest.Inspect;
                intent.ThrowUtility|=_latest.ThrowUtility;intent.SwitchWeapon|=_latest.SwitchWeapon;
                if(intent.Select==HeldSlot.Current)intent.Select=_latest.Select;
                if(intent.Buy==BuySlot.None){intent.Buy=_latest.Buy;intent.ItemId=_latest.ItemId;intent.CancelPurchase=_latest.CancelPurchase;}
                if(intent.Cycle==0)intent.Cycle=_latest.Cycle;
            }
            intent.Fire|=intent.FirePressed;
            _ack=acknowledgedTick; _receivedAt=_serverTick;_sequence=sequence;_latest=intent;_fresh=true;
            return true;
        }
        public SeatIntent Decide(in SeatView view)
        {
            bool recent=view.Match.Tick-_receivedAt<=3;
            var output=_fresh?_latest:new SeatIntent{Yaw=_latest.Yaw,Pitch=_latest.Pitch,
                Move=recent?_latest.Move:Vector2.zero,Walk=recent&&_latest.Walk,Crouch=recent&&_latest.Crouch,
                Interact=recent&&_latest.Interact,Fire=recent&&_latest.Fire};
            if(_sequence<0){output.Yaw=view.Yaw;output.Pitch=view.Pitch;}
            output.ObservedTick=Mathf.Clamp(_ack,Mathf.Max(0,view.Match.Tick+1-_rewind),view.Match.Tick+1);
            _fresh=false;
            return output;
        }
    }

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
        public bool Sample(float renderTick,out Vector3 pos,out float yaw,out float pitch)
        {
            pos=default;yaw=pitch=0;if(_count==0 || !float.IsFinite(renderTick))return false;
            var right=At(0);var left=right;
            renderTick=Mathf.Min(renderTick,right.Tick+2);
            for(int age=1;age<_count;age++)
            {
                left=At(age);
                if(left.Tick<=renderTick)break;
                right=left;
            }
            float t=right.Tick==left.Tick?0:Mathf.Max(0,(renderTick-left.Tick)/(right.Tick-left.Tick));
            pos=Vector3.LerpUnclamped(left.Position,right.Position,t);
            yaw=Mathf.Repeat(left.Yaw+Mathf.DeltaAngle(left.Yaw,right.Yaw)*t,360);
            pitch=Mathf.Clamp(Mathf.LerpUnclamped(left.Pitch,right.Pitch,t),-82,82);return true;
        }
    }
}
