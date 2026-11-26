using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace AnimeCS.Match
{
    public sealed class SeatDatagramClient : IDisposable
    {
        private readonly Socket _socket;
        private readonly byte[] _incoming = new byte[65536];
        private readonly byte[] _hello = SeatWire.Packet(SeatWire.Hello, SeatWire.Header, 0);
        private readonly byte[][] _history = new byte[3][];
        private readonly int[] _lengths = new int[3];
        private readonly SnapshotClock _clock = new SnapshotClock();
        private readonly SeatMotionPrediction _prediction;
        private readonly byte[] _heartbeat = new byte[SeatWire.Header + 8];
        private double _helloAt, _lastState, _lastSend;
        private ulong _token;
        private int _sequence, _accepted;

        public Vector3 PredictedPosition => _prediction.Position;
        public bool Connected => _token != 0;
        public SeatView View { get; private set; }
        public SeatSnapshotBuffer Poses { get; } = new SeatSnapshotBuffer();

        public SeatDatagramClient(IPEndPoint server) : this(server, null) { }
        public SeatDatagramClient(IPEndPoint server, MovementTuning tuning)
        {
            _prediction = new SeatMotionPrediction(tuning);
            _socket = new Socket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                _socket.Connect(server);
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    _socket.IOControl(unchecked((int)0x9800000C), new byte[4], null);
                for (int i = 0; i < _history.Length; i++) _history[i] = new byte[SeatWire.MaxInputBytes];
                Hello();
            }
            catch { _socket.Dispose(); throw; }
        }

        public bool SamplePose(out Vector3 position, out float yaw, out float pitch) =>
            Poses.Sample(_clock.Sample(SnapshotClock.Now), out position, out yaw, out pitch);

        private void Hello()
        {
            _helloAt = SnapshotClock.Now;
            Send(_hello, _hello.Length);
        }

        private bool Send(byte[] packet, int size)
        {
            try { return _socket.Send(packet, 0, size, SocketFlags.None) == size; }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused ||
                                              ex.SocketErrorCode == SocketError.ConnectionReset) { return false; }
        }

        public void Poll()
        {
            double now = SnapshotClock.Now;
            if (Connected && now - _lastState > 5)
            {
                _token = 0;
                Poses.Clear();
                _clock.Reset(); _prediction.Clear();
            }
            if (!Connected && now - _helloAt > .5) Hello();
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
                var p = _incoming;
                if (size != SeatWire.ExtendedStateBytes || SeatWire.U32(p,0) != SeatWire.Magic || p[4] != SeatWire.State) continue;
                ulong token = SeatWire.Token(p);
                int tick = SeatWire.I32(p,17), round = SeatWire.I32(p,13);
                if (token == 0 || tick < 0 || round < 0 || p[21] >= LocalMatch.Seats || p[22] > (byte)RoundPhase.Practice ||
                    p[23] > (byte)Content.Side.Defend || p[42] > (byte)HeldSlot.Utility ||
                    Connected && (token != _token || tick <= View.Match.Tick || round < View.Match.Round)) continue;
                if (!Connected || View.Match.Round != round)
                {
                    _sequence = _accepted = 0;
                    Array.Clear(_lengths, 0, _lengths.Length);
                    Poses.Clear();
                    _clock.Reset(); _prediction.Clear();
                }
                if (Connected && (View.Health == 0) != (p[40] == 0)) { Poses.Clear(); _clock.Reset(); _prediction.Clear(); }
                _token = token; _lastState = now;
                _accepted = Math.Max(_accepted, Math.Min(_sequence, SeatWire.I32(p,52)));
                int visible = SeatWire.I32(p,72);
                View = new SeatView
                {
                    Seat=p[21], Side=(Content.Side)p[23], Position=Position(p,24),
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
                Poses.Push(tick, View.Position, View.Yaw, View.Pitch);
                _clock.Observe(tick,now);
                _prediction.Reconcile(View,_accepted);
            }
        }

        private static Vector3 Position(byte[] p, int at) =>
            new Vector3(SeatWire.I32(p,at), SeatWire.I32(p,at+4), SeatWire.I32(p,at+8)) * .01f;

        public bool Submit(SeatIntent intent)
        {
            if (!Connected) return false;
            int size = string.IsNullOrEmpty(intent.ItemId) ? 0 : Encoding.UTF8.GetByteCount(intent.ItemId);
            if (size > 80) return false;
            int sequence = ++_sequence, slot = sequence % _history.Length;
            var p = _history[slot];
            SeatWire.HeaderFor(p,SeatWire.Input,SeatWire.Magic,_token);
            SeatWire.I32(p,13,View.Match.Round); SeatWire.I32(p,17,View.Match.Tick);
            SeatWire.I32(p,21,sequence); SeatWire.U32(p,25,SeatWire.Flags(intent));
            SeatWire.I16(p,29,SeatWire.Angle(intent.Yaw)); SeatWire.I16(p,31,SeatWire.Angle(intent.Pitch));
            SeatWire.I16(p,33,SeatWire.Axis(intent.Move.x)); SeatWire.I16(p,35,SeatWire.Axis(intent.Move.y));
            SeatWire.I16(p,37,SeatWire.Axis(intent.BrakeTap.x)); SeatWire.I16(p,39,SeatWire.Axis(intent.BrakeTap.y));
            p[41]=(byte)(sbyte)Math.Sign(intent.Cycle); p[42]=(byte)intent.Select; p[43]=(byte)intent.Buy;
            p[44]=(byte)(intent.CancelPurchase?1:0); p[45]=(byte)size;
            if (size > 0) Encoding.UTF8.GetBytes(intent.ItemId,0,intent.ItemId.Length,p,SeatWire.InputBytes);
            int at = SeatWire.InputBytes + size;
            double now = SnapshotClock.Now;
            double observed = _clock.Sample(now);
            int tick = (int)Math.Floor(observed);
            SeatWire.I32(p,at,tick);
            SeatWire.I16(p,at+4,(int)((observed-tick)*65536));
            _lengths[slot] = at + SeatWire.TimestampBytes;
            for (int seq = Math.Max(_accepted + 1, sequence - 2); seq < sequence; seq++)
            {
                int previous = seq % _history.Length;
                if (_lengths[previous] > 0) Send(_history[previous], _lengths[previous]);
            }
            if (!Send(p, _lengths[slot])) return false;
            _prediction.Record(_clock.InputTick(now),sequence,intent);
            _lastSend=now;
            return true;
        }
        public void Dispose() => _socket.Dispose();
    }
}
