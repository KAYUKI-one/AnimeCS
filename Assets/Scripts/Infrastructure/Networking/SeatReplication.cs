using System;
using UnityEngine;

namespace AnimeCS.Match
{
    // Called on the host simulation thread. Timestamps and RTT come from the host's transport clock.
    public sealed class HostSeatInput : ISeatDriver
    {
        private struct Command
        {
            internal int Sequence, ObservedTick;
            internal ushort Fraction;
            internal SeatIntent Intent;
        }

        private readonly Command[] _pending = new Command[SeatWire.InputWindow];
        private int _nextOrdered = 1;
        private bool _ordered;
        private int _round=-1,_serverTick,_rewind,_sequence=-1,_receivedAt=-100,_ack;
        private SeatIntent _latest;
        private bool _fresh;
        private ushort _fraction;
        internal int AcceptedSequence => _sequence;
        internal int AppliedSequence { get; private set; } = -1;
        public void ObserveRound(int round,int serverTick,float rttSeconds)
        {
            if (_round != round) ResetRound(round);
            _serverTick=serverTick;
            float rtt=float.IsFinite(rttSeconds)?Mathf.Clamp(rttSeconds,0,.4f):0;
            _rewind=Mathf.Min(MatchRules.TickRate/5,Mathf.CeilToInt(rtt*.5f*MatchRules.TickRate)+2);
        }

        private void ResetRound(int round)
        {
            _round = round;
            _sequence = AppliedSequence = -1;
            _fresh = false;
            _latest = default;
            _receivedAt = -100;
            Array.Clear(_pending, 0, _pending.Length);
            _nextOrdered = 1;
            _ack = 0;
            _fraction = 0;
        }
        public bool Receive(int sequence,int acknowledgedTick,SeatIntent intent)
            => Receive(sequence, acknowledgedTick, acknowledgedTick, 0, intent);

        internal bool Receive(int sequence, int acknowledgedTick, int observedTick, ushort fraction, SeatIntent intent)
        {
            if (_ordered || sequence <= _sequence || !Valid(acknowledgedTick, observedTick, intent)) return false;
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

        private bool Valid(int acknowledgedTick, int observedTick, in SeatIntent intent) =>
            _round >= 0 && acknowledgedTick >= 0 && acknowledgedTick <= _serverTick &&
            observedTick >= 0 && observedTick <= (long)acknowledgedTick + 2 &&
            float.IsFinite(intent.Yaw) && float.IsFinite(intent.Pitch) &&
            float.IsFinite(intent.Move.x) && float.IsFinite(intent.Move.y) &&
            float.IsFinite(intent.BrakeTap.x) && float.IsFinite(intent.BrakeTap.y);

        internal bool ReceiveOrdered(int sequence, int acknowledgedTick, int observedTick, ushort fraction, in SeatIntent intent)
        {
            if (sequence < _nextOrdered || (long)sequence - _nextOrdered >= _pending.Length ||
                !Valid(acknowledgedTick, observedTick, intent)) return false;
            ref var slot = ref _pending[sequence % _pending.Length];
            if (slot.Sequence == sequence) return false;
            slot = new Command { Sequence=sequence, ObservedTick=observedTick, Fraction=fraction, Intent=intent };
            _ordered = true;
            _sequence = Math.Max(_sequence, sequence);
            return true;
        }

        private static bool HasAction(in SeatIntent intent, in SeatIntent previous) =>
            intent.FirePressed || intent.Fire != previous.Fire || intent.Interact != previous.Interact ||
            intent.Reload || intent.SwitchWeapon || intent.DropObjective || intent.ThrowUtility || intent.Jump ||
            intent.DropHeld || intent.Zoom || intent.Inspect || intent.LowerScope || intent.Cycle != 0 ||
            intent.Select != HeldSlot.Current || intent.Buy != BuySlot.None || intent.BrakeTap != Vector2.zero;

        private void ConsumeOrdered(int simulationTick)
        {
            // Consecutive analogue samples can share a tick. Each one-shot action gets its own
            // simulation tick and its own view angles/time; ACK never crosses a hole or a queued action.
            while (true)
            {
                ref var slot = ref _pending[_nextOrdered % _pending.Length];
                if (slot.Sequence != _nextOrdered) return;
                bool action = HasAction(slot.Intent, _latest);
                _latest = slot.Intent;
                _latest.Fire |= _latest.FirePressed;
                _ack = slot.ObservedTick;
                _fraction = slot.Fraction;
                _receivedAt = simulationTick;
                _fresh = true;
                AppliedSequence = _nextOrdered++;
                slot = default;
                if (action) return;
            }
        }

        public SeatIntent Decide(in SeatView view)
        {
            if (view.Match.Round != _round) ResetRound(view.Match.Round);
            if (_ordered) ConsumeOrdered(view.Match.Tick);
            bool recent=view.Match.Tick-_receivedAt<=3;
            var output=_fresh?_latest:new SeatIntent{Yaw=_latest.Yaw,Pitch=_latest.Pitch,
                Move=recent?_latest.Move:Vector2.zero,Walk=recent&&_latest.Walk,Crouch=recent&&_latest.Crouch,
                Interact=recent&&_latest.Interact,Fire=recent&&_latest.Fire};
            if(!_fresh && AppliedSequence<0){output.Yaw=view.Yaw;output.Pitch=view.Pitch;}
            output.ObservedTick=Mathf.Clamp(_ack,Mathf.Max(0,view.Match.Tick+1-_rewind),view.Match.Tick+1);
            output.ObservedFraction = output.ObservedTick == _ack && _ack < view.Match.Tick + 1 ? _fraction : (ushort)0;
            if (!_ordered) AppliedSequence = _sequence;
            _fresh=false;
            return output;
        }
    }
}
