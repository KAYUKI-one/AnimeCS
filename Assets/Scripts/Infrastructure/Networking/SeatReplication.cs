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
        private ushort _fraction;
        internal int AcceptedSequence => _sequence;
        internal int AppliedSequence { get; private set; } = -1;
        public void ObserveRound(int round,int serverTick,float rttSeconds)
        {
            if(_round!=round)
            {
                _round=round;_sequence=AppliedSequence=-1;_fresh=false;_latest=default;_receivedAt=-100;
            }
            _serverTick=serverTick;
            float rtt=float.IsFinite(rttSeconds)?Mathf.Clamp(rttSeconds,0,.4f):0;
            _rewind=Mathf.Min(MatchRules.TickRate/5,Mathf.CeilToInt(rtt*.5f*MatchRules.TickRate)+2);
        }
        public bool Receive(int sequence,int acknowledgedTick,SeatIntent intent)
            => Receive(sequence, acknowledgedTick, acknowledgedTick, 0, intent);

        internal bool Receive(int sequence, int acknowledgedTick, int observedTick, ushort fraction, SeatIntent intent)
        {
            if (_round<0 || sequence<=_sequence || acknowledgedTick>_serverTick || acknowledgedTick<0 ||
                !float.IsFinite(intent.Yaw) || !float.IsFinite(intent.Pitch) || !float.IsFinite(intent.Move.x) ||
                !float.IsFinite(intent.Move.y) || !float.IsFinite(intent.BrakeTap.x) || !float.IsFinite(intent.BrakeTap.y)) return false;
            if (observedTick < 0 || observedTick > (long)acknowledgedTick + 2) return false;
            if (_fresh && _latest.FirePressed && !intent.FirePressed)
            {
                observedTick = _ack;
                fraction = _fraction;
                intent.Yaw = _latest.Yaw;
                intent.Pitch = _latest.Pitch;
            }
            // Within a host tick keep the last axes and retain press edges until consumed.
            if(_fresh)
            {
                intent.LowerScope |= _latest.LowerScope;
                intent.DropObjective |= _latest.DropObjective;
                intent.FirePressed|=_latest.FirePressed; intent.Reload|=_latest.Reload; intent.Jump|=_latest.Jump;
                intent.Zoom|=_latest.Zoom; intent.DropHeld|=_latest.DropHeld; intent.Inspect|=_latest.Inspect;
                intent.ThrowUtility|=_latest.ThrowUtility;intent.SwitchWeapon|=_latest.SwitchWeapon;
                if(intent.Select==HeldSlot.Current)intent.Select=_latest.Select;
                if(intent.Buy==BuySlot.None){intent.Buy=_latest.Buy;intent.ItemId=_latest.ItemId;intent.CancelPurchase=_latest.CancelPurchase;}
                if(intent.Cycle==0)intent.Cycle=_latest.Cycle;
            }
            intent.Fire|=intent.FirePressed;
            _ack=observedTick; _fraction=fraction; _receivedAt=_serverTick;_sequence=sequence;_latest=intent;_fresh=true;
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
            output.ObservedFraction = output.ObservedTick == _ack && _ack < view.Match.Tick + 1 ? _fraction : (ushort)0;
            AppliedSequence = _sequence;
            _fresh=false;
            return output;
        }
    }
}
