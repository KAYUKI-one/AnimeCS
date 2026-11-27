using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace AnimeCS.Match
{
    public sealed partial class SeatDatagramClient : IDisposable
    {
        private readonly Socket _socket;
        private readonly byte[] _incoming = new byte[65536];
        private byte[] _hello = SeatWire.Packet(SeatWire.Hello, SeatWire.Header, 0);
        private string _ticket;
        private readonly byte[][] _history = new byte[SeatWire.InputWindow][];
        private readonly int[] _lengths = new int[SeatWire.InputWindow];
        private readonly SnapshotClock _clock = new SnapshotClock();
        private readonly SeatMotionPrediction _prediction;
        private readonly byte[] _heartbeat = new byte[SeatWire.Header + 8];
        private const double HelloIntervalSeconds = .5;
        internal const int ReconnectWindowSeconds = 2 * SeatWire.PeerTimeoutSeconds;
        private double _helloAt, _lastState, _lastSend, _reconnectUntil;
        private readonly byte[] _handlingRequest = new byte[SeatWire.Header];
        private bool _hasHandling, _orderedInput;
        private double _requestAt, _retryAt;
        private ulong _token, _retiredToken;
        private int _sequence, _accepted;

        public Vector3 PredictedPosition => _prediction.Position;
        public bool Connected => _token != 0 && !Reconnecting;
        internal bool Reconnecting => _reconnectUntil > 0;
        public bool Finished => View.Match.Phase == RoundPhase.Finished;
        internal bool CanQueueInput => Connected && !Finished && (!_orderedInput || _sequence-_accepted<_history.Length);
        public SeatView View { get; private set; }
        public SeatSnapshotBuffer Poses { get; } = new SeatSnapshotBuffer();
#if ANIMECS_RENDER_PROBE
        internal long ReceivedPayloadBytes { get; private set; }
        internal long SentPayloadBytes { get; private set; }
#endif

        public SeatDatagramClient(IPEndPoint server) : this(server, null) { }
        public SeatDatagramClient(IPEndPoint server, MovementTuning tuning, string ticket = null)
        {
            RenewTicket(ticket);
            _prediction = new SeatMotionPrediction(tuning);
            _socket = new Socket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                _socket.Connect(server);
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    _socket.IOControl(unchecked((int)0x9800000C), new byte[4], null);
                for (int i = 0; i < _history.Length; i++) _history[i] = new byte[SeatWire.MaxInputBytes];
                Hello(SnapshotClock.Now);
            }
            catch { _socket.Dispose(); throw; }
        }

        public bool SamplePose(out Vector3 position, out float yaw, out float pitch) =>
            Poses.Sample(_clock.Sample(SnapshotClock.Now), out position, out yaw, out pitch);

        internal void RenewTicket(string ticket)
        {
            if (string.IsNullOrEmpty(ticket) || ticket==_ticket) return;
            if (ticket.Length > 512) throw new ArgumentException("Invalid join ticket",nameof(ticket));
            var hello=SeatWire.Packet(SeatWire.Hello,SeatWire.Header+ticket.Length,0);
            Encoding.ASCII.GetBytes(ticket,0,ticket.Length,hello,SeatWire.Header);
            _ticket=ticket;
            _hello=hello;
        }

        private void Hello(double now)
        {
            _helloAt = now;
            Send(_hello, _hello.Length);
        }

        private bool Send(byte[] packet, int size)
        {
            try
            {
                int sent = _socket.Send(packet, 0, size, SocketFlags.None);
#if ANIMECS_RENDER_PROBE
                SentPayloadBytes += sent;
#endif
                return sent == size;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused ||
                                              ex.SocketErrorCode == SocketError.ConnectionReset) { return false; }
        }

        public void Poll() => Poll(SnapshotClock.Now);

        internal void Poll(double now)
        {
            if (_reconnectUntil < 0) return;
            if (!Connected && !Finished && now-_helloAt >= HelloIntervalSeconds && (!Reconnecting || now < _reconnectUntil)) Hello(now);
            if (Connected && now - _lastSend >= 1)
            {
                SeatWire.HeaderFor(_heartbeat,SeatWire.KeepAlive,SeatWire.Magic,_token);
                SeatWire.I32(_heartbeat,13,View.Match.Round);
                SeatWire.I32(_heartbeat,17,View.Match.Tick);
                Send(_heartbeat,_heartbeat.Length);
                _lastSend=now;
            }
            for (int n = 0; n < 32 && _socket.Poll(0, SelectMode.SelectRead); n++)
            {
                int size;
                try { size = _socket.Receive(_incoming); }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused ||
                                                  ex.SocketErrorCode == SocketError.ConnectionReset) { break; }
#if ANIMECS_RENDER_PROBE
                ReceivedPayloadBytes += size;
#endif
                var p = _incoming;
                if (size >= SeatWire.Header && p[4] == SeatRoster.Kind) { ReceiveRoster(p,size); continue; }
                if (size >= SeatWire.Header && (p[4] == 40 || p[4] == 41)) { ReceiveMatchWorld(p,size); continue; }
                if (size < SeatWire.ExtendedStateBytes || size > SeatWire.MaxHandlingBytes || SeatWire.U32(p,0) != SeatWire.Magic || p[4] != SeatWire.State) continue;
                ulong token = SeatWire.Token(p);
                int tick = SeatWire.I32(p,17), round = SeatWire.I32(p,13);
                bool newBinding = token != _token;
                if (token == 0 || token == _retiredToken || tick < 0 || round < 0 || p[21] >= LocalMatch.Seats || p[22] > (byte)RoundPhase.Practice ||
                    p[23] > (byte)Content.Side.Defend || p[42] > (byte)HeldSlot.Utility ||
                    _token != 0 && (newBinding && !Reconnecting || p[21] != View.Seat ||
                                    tick < View.Match.Tick || round < View.Match.Round)) continue;
                bool final = (RoundPhase)p[22] == RoundPhase.Finished;
                if (Finished && (!Connected || !final || round != View.Match.Round || tick != View.Match.Tick)) continue;
                if (!newBinding && tick == View.Match.Tick && !final) continue;
                int visible = SeatWire.I32(p,72);
                var received = new SeatView
                {
                    EquipTick=-100000, InspectTick=-100000, Seat=p[21], Side=(Content.Side)p[23], Position=Position(p,24),
                    Match=new PublicMatchView { Round=round, Tick=tick, Phase=(RoundPhase)p[22], RemainingTicks=SeatWire.I32(p,56),
                        Score0=SeatWire.I32(p,60), Score1=SeatWire.I32(p,64) },
                    Motion=new MotionContact { Velocity=new Vector2(SeatWire.I16(p,88),SeatWire.I16(p,90))*.01f,
                        VerticalSpeed=SeatWire.I16(p,92)*.01f, Crouch=p[94]/255f, Grounded=(p[95]&1)!=0 },
                    PredictionJustStopped=(p[95]&2)!=0, PredictionSpeed=SeatWire.I16(p,96)*.01f,
                    Credits=SeatWire.I32(p,68), Yaw=Mathf.Repeat(SeatWire.I16(p,36)*.01f,360), Pitch=SeatWire.I16(p,38)*.01f,
                    Health=p[40], Magazine=p[41], Held=(HeldSlot)p[42], ScopeLevel=p[43],
                    LastShotTick=SeatWire.I32(p,44), LastConfirmationTick=SeatWire.I32(p,48),
                    VisibleEnemy=(uint)visible<LocalMatch.Seats ? new Contact(visible,Position(p,76)) : (Contact?)null
                };
                bool handling = size > SeatWire.ExtendedStateBytes;
                if (handling && !SeatWire.ReadHandling(p,size,ref received,View)) continue;
                if (newBinding || View.Match.Round != round)
                {
                    ResetWorld();
                    _rosterRound = _rosterTick = -1;
                    _sequence = _accepted = 0;
                    _retryAt = 0;
                    Array.Clear(_lengths, 0, _lengths.Length);
                    Poses.Clear();
                    _clock.Reset(); _prediction.Clear();
                }
                if (newBinding)
                {
                    _retiredToken = _token;
                    _hasHandling = false;
                    _requestAt = 0;
                }
                if (handling) _hasHandling = true;
                if (_token != 0 && (View.Health == 0) != (p[40] == 0)) { Poses.Clear(); _clock.Reset(); _prediction.Clear(); }
                _reconnectUntil = 0;
                _token = token; _lastState = now;
                _orderedInput = (p[95] & SeatWire.OrderedInputCapability) != 0;
                _accepted = Math.Max(_accepted, Math.Min(_sequence, SeatWire.I32(p,52)));
                View = received;
                Poses.Push(tick, View.Position, View.Yaw, View.Pitch);
                _clock.Observe(tick,now);
                _prediction.Reconcile(View,_accepted);
            }
            if (Connected && now - _lastState > SeatWire.PeerTimeoutSeconds)
            {
                if (Finished) { _token = 0; return; }
                // Keep the previous token and its ordered history until the authority identifies this binding.
                _reconnectUntil = now + ReconnectWindowSeconds;
                Hello(now);
            }
            if (Reconnecting && now >= _reconnectUntil)
            {
                _token = 0;
                _reconnectUntil = -1;
                return;
            }
            if (Connected && !Finished && _orderedInput) RetryPending(now);
            if (Connected) TickWatch(now);
            if (Connected && !_hasHandling && now >= _requestAt)
            {
                SeatWire.HeaderFor(_handlingRequest,SeatWire.RequestHandling,SeatWire.Magic,_token);
                Send(_handlingRequest,_handlingRequest.Length);
                _requestAt = now + 1;
            }
        }

        private static Vector3 Position(byte[] p, int at) =>
            new Vector3(SeatWire.I32(p,at), SeatWire.I32(p,at+4), SeatWire.I32(p,at+8)) * .01f;

        // A driver may dequeue a purchase or consume a button edge as soon as Decide is called.
        internal bool SubmitFrom(ISeatDriver driver,in SeatView view) => CanQueueInput && Submit(driver.Decide(view));

        public bool Submit(SeatIntent intent)
        {
            if (!Connected || Finished) return false;
            int size = string.IsNullOrEmpty(intent.ItemId) ? 0 : Encoding.UTF8.GetByteCount(intent.ItemId);
            if (size > 80) return false;
            double now = SnapshotClock.Now;
            if (_orderedInput)
            {
                RetryPending(now);
                if (!CanQueueInput) return false;
            }
            int sequence = ++_sequence, slot = sequence % _history.Length;
            var p = _history[slot];
            SeatWire.HeaderFor(p,_orderedInput ? SeatWire.OrderedInput : SeatWire.Input,SeatWire.Magic,_token);
            SeatWire.I32(p,13,View.Match.Round); SeatWire.I32(p,17,View.Match.Tick);
            SeatWire.I32(p,21,sequence); SeatWire.U32(p,25,SeatWire.Flags(intent));
            SeatWire.I16(p,29,SeatWire.Angle(intent.Yaw)); SeatWire.I16(p,31,SeatWire.Angle(intent.Pitch));
            SeatWire.I16(p,33,SeatWire.Axis(intent.Move.x)); SeatWire.I16(p,35,SeatWire.Axis(intent.Move.y));
            SeatWire.I16(p,37,SeatWire.Axis(intent.BrakeTap.x)); SeatWire.I16(p,39,SeatWire.Axis(intent.BrakeTap.y));
            p[41]=(byte)(sbyte)Math.Sign(intent.Cycle); p[42]=(byte)intent.Select; p[43]=(byte)intent.Buy;
            p[44]=(byte)(intent.CancelPurchase?1:0); p[45]=(byte)size;
            if (size > 0) Encoding.UTF8.GetBytes(intent.ItemId,0,intent.ItemId.Length,p,SeatWire.InputBytes);
            int at = SeatWire.InputBytes + size;
            double observed = _clock.Sample(now);
            int tick = (int)Math.Floor(observed);
            SeatWire.I32(p,at,tick);
            SeatWire.I16(p,at+4,(int)((observed-tick)*65536));
            _lengths[slot] = at + SeatWire.TimestampBytes;
            if (!_orderedInput)
            {
                for (int seq = Math.Max(_accepted + 1, sequence - 2); seq < sequence; seq++)
                {
                    int previous = seq % _history.Length;
                    if (_lengths[previous] > 0) Send(_history[previous], _lengths[previous]);
                }
            }
            if (!Send(p, _lengths[slot]) && !_orderedInput) return false;
            _prediction.Record(_clock.InputTick(now),sequence,intent);
            _lastSend=now;
            return true;
        }

        private void RetryPending(double now)
        {
            if (_accepted >= _sequence || now < _retryAt) return;
            _retryAt = now + 3d / MatchRules.TickRate;
            int last = Math.Min(_sequence, _accepted + 3);
            for (int seq = _accepted + 1; seq <= last; seq++)
            {
                int slot = seq % _history.Length;
                var packet = _history[slot];
                // Refresh proof of the received snapshot, keeping the action's original observation timestamp.
                SeatWire.I32(packet,17,View.Match.Tick);
                Send(packet,_lengths[slot]);
            }
        }
        public void Dispose() => _socket.Dispose();
    }
}
