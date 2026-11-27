using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using UnityEngine;

namespace AnimeCS.Match
{
    public sealed partial class SeatDatagramHost : IDisposable
    {
        private sealed partial class Peer
        {
            internal IPEndPoint Endpoint;
            internal ulong Token;
            internal uint Version;
            internal bool Handling;
            internal long LastContact;
            internal float Rtt = .08f;
            internal int Round = -1, LastTick = -1;
            internal HostSeatInput Input = new HostSeatInput();
            internal readonly long[] SentAt = new long[64];
            internal readonly int[] SentTick = new int[64];
        }

        private readonly Socket _socket;
        private readonly Peer[] _peers;
        private readonly byte[] _incoming = new byte[65536];
        private readonly byte[] _state = new byte[SeatWire.MaxHandlingBytes];
        private EndPoint _sender;
        private readonly string _battleSecret;
        private string _room;
        private long _roomGeneration;
        internal string Room => _room;
        internal bool HasPlayers => Array.Exists(_peers,peer=>peer.Endpoint!=null);
        internal Action RoomStarted;
        internal Action<int, string> SeatJoined;

        public IPEndPoint LocalEndpoint => (IPEndPoint)_socket.LocalEndPoint;
        public HostSeatInput Input => _peers[0].Input;
        public bool Connected => _peers[0].Endpoint != null;
        internal HostSeatInput Driver(int seat) => _peers[seat].Endpoint == null ? null : _peers[seat].Input;

        public SeatDatagramHost(IPEndPoint endpoint) : this(endpoint, 1) { }

        internal SeatDatagramHost(IPEndPoint endpoint, int seats, string battleSecret = null)
        {
            _battleSecret = battleSecret;
            if (seats < 1 || seats > LocalMatch.Seats) throw new ArgumentOutOfRangeException(nameof(seats));
            _peers = new Peer[seats];
            for (int i = 0; i < seats; i++) _peers[i] = new Peer();
            _sender = new IPEndPoint(endpoint.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0);
            _socket = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                _socket.Bind(endpoint);
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    _socket.IOControl(unchecked((int)0x9800000C), new byte[4], null);
            }
            catch { _socket.Dispose(); throw; }
        }

        private readonly SeatView[] _single = new SeatView[1];
        public void Poll(in SeatView view) { _single[0] = view; Poll(_single); }
        public void Publish(in SeatView view) => Publish(_peers[0], view);

        internal void Poll(SeatView[] views)
        {
            long now = Stopwatch.GetTimestamp();
            for (int i = 0; i < _peers.Length; i++)
            {
                var peer = _peers[i];
                var match = views[i].Match;
                if (peer.Endpoint != null && (now - peer.LastContact > SeatWire.PeerTimeoutSeconds * Stopwatch.Frequency || match.Tick < peer.LastTick))
                    peer.Endpoint = null;
                if (peer.Round != match.Round) Array.Fill(peer.SentTick, -1);
                peer.Round = match.Round;
                peer.LastTick = match.Tick;
                peer.Input.ObserveRound(match.Round, match.Tick, peer.Rtt);
            }
            Span<byte> key = stackalloc byte[8];
            for (int n = 0; n < 32 * _peers.Length && _socket.Poll(0, SelectMode.SelectRead); n++)
            {
                int length = _socket.ReceiveFrom(_incoming, ref _sender);
                if (!SeatWire.Known(_incoming, length)) continue;
                int index = -1, free = -1;
                for (int i = 0; i < _peers.Length; i++)
                {
                    if (_sender.Equals(_peers[i].Endpoint)) { index = i; break; }
                    if (free < 0 && _peers[i].Endpoint == null) free = i;
                }
                uint version = SeatWire.U32(_incoming, 0);
                if (_incoming[4] == SeatWire.Hello && length <= SeatWire.Header + 512)
                {
                    if (!string.IsNullOrEmpty(_battleSecret))
                    {
                        if (length <= SeatWire.Header) continue;
                        string ticket = System.Text.Encoding.ASCII.GetString(_incoming, SeatWire.Header, length - SeatWire.Header);
                        if (!BattleTicket.Read(_battleSecret, ticket, out var room, out int assigned, out string id, out long generation,
                            _contentMap, _contentRevision) || assigned >= _peers.Length) continue;
                        if (_room != room)
                        {
                            if (generation <= _roomGeneration) continue;
                            _roomGeneration = generation;
                            _room = room;
                            index = -1;
                            RoomStarted?.Invoke();
                            for (int i = 0; i < _peers.Length; i++)
                            {
                                _peers[i].Endpoint = null;
                                _peers[i].Round = views[i].Match.Round;
                                _peers[i].LastTick = views[i].Match.Tick;
                            }
                        }
                        if (index >= 0 && assigned != index || index < 0 && _peers[assigned].Endpoint != null) continue;
                        free = assigned;
                        if (index < 0) SeatJoined?.Invoke(assigned,id);
                    }
                    else if (length != SeatWire.Header) continue;
                    if (index < 0)
                    {
                        if (free < 0) continue;
                        index = free;
                        var peer = _peers[index];
                        var sender = (IPEndPoint)_sender;
                        peer.Endpoint = new IPEndPoint(sender.Address, sender.Port);
                        RandomNumberGenerator.Fill(key);
                        peer.Token = BitConverter.ToUInt64(key) | 1UL;
                        peer.Handling = false;
                        peer.Version = version;
                        peer.Input = new HostSeatInput();
                        ResetPeerWorld(peer);
                        peer.LastContact = now;
                        peer.Rtt = .08f;
                        Array.Fill(peer.SentTick, -1);
                        peer.Input.ObserveRound(peer.Round, peer.LastTick, peer.Rtt);
                    }
                    if (_peers[index].Version == version) Publish(_peers[index], views[index]);
                }
                else if (index >= 0 && _incoming[4] == SeatWire.RequestHandling && length == SeatWire.Header &&
                    version == SeatWire.Magic && version == _peers[index].Version && SeatWire.Token(_incoming) == _peers[index].Token)
                {
                    _peers[index].Handling = true;
                }
                else if (index >= 0 && _incoming[4] == SeatWire.KeepAlive && length == SeatWire.Header + 8 && version == SeatWire.Magic)
                {
                    var peer = _peers[index];
                    int ack = SeatWire.I32(_incoming,17);
                    if (SeatWire.Token(_incoming)==peer.Token && SeatWire.I32(_incoming,13)==peer.Round &&
                        ack >= 0 && peer.SentTick[ack % 64]==ack) peer.LastContact=now;
                }
                else if (index >= 0 && _incoming[4] == 42)
                    ReceiveWatch(_peers[index], _incoming, length);
                else if (index >= 0 && (_incoming[4] == SeatWire.Input ||
                    version == SeatWire.Magic && _incoming[4] == SeatWire.OrderedInput))
                    Receive(_peers[index], length, version, now);
            }
        }

        private void Receive(Peer peer, int length, uint version, long now)
        {
            var p = _incoming;
            if (peer.Version != version || SeatWire.Token(p) != peer.Token || !SeatWire.ValidInput(p, length, version)
                || SeatWire.I32(p, 13) != peer.Round) return;
            int ack = SeatWire.I32(p, 17);
            if (ack < 0 || peer.SentTick[ack % 64] != ack) return;
            int at = SeatWire.InputBytes + p[45];
            int tick = version == SeatWire.Magic ? SeatWire.I32(p, at) : ack;
            ushort fraction = version == SeatWire.Magic ? unchecked((ushort)SeatWire.I16(p, at + 4)) : (ushort)0;
            int sequence = SeatWire.I32(p, 21);
            var intent = SeatWire.Intent(p);
            bool accepted = p[4] == SeatWire.OrderedInput
                ? peer.Input.ReceiveOrdered(sequence, ack, tick, fraction, intent)
                : peer.Input.Receive(sequence, ack, tick, fraction, intent);
            if (!accepted) return;
            float elapsed = (float)((now - peer.SentAt[ack % 64]) / (double)Stopwatch.Frequency);
            peer.Rtt = Mathf.Lerp(peer.Rtt, Mathf.Clamp(elapsed, 0, .4f), .25f);
            peer.LastContact = now;
        }

        internal void Publish(SeatView[] views)
        {
            for (int i = 0; i < _peers.Length; i++) Publish(_peers[i], views[i]);
        }

        private void Publish(Peer peer, in SeatView v)
        {
            if (peer.Endpoint == null) return;
            var p = _state;
            SeatWire.HeaderFor(p, SeatWire.State, peer.Version, peer.Token);
            SeatWire.I32(p,13,v.Match.Round); SeatWire.I32(p,17,v.Match.Tick);
            p[21]=(byte)v.Seat; p[22]=(byte)v.Match.Phase; p[23]=(byte)v.Side;
            Position(p, 24, v.Position);
            SeatWire.I16(p,36,SeatWire.Angle(v.Yaw)); SeatWire.I16(p,38,SeatWire.Angle(v.Pitch));
            p[40]=(byte)Mathf.Clamp(v.Health,0,255); p[41]=(byte)Mathf.Clamp(v.Magazine,0,255); p[42]=(byte)v.Held; p[43]=(byte)v.ScopeLevel;
            SeatWire.I32(p,44,v.LastShotTick); SeatWire.I32(p,48,v.LastConfirmationTick);
            int length = SeatWire.StateBytes;
            if (peer.Version == SeatWire.Magic)
            {
                length = peer.Handling ? SeatWire.WriteHandling(p,v) : SeatWire.ExtendedStateBytes;
                SeatWire.I32(p,52,peer.Input.AppliedSequence);
                SeatWire.I32(p,56,v.Match.RemainingTicks);
                SeatWire.I32(p,60,v.Match.Score0); SeatWire.I32(p,64,v.Match.Score1);
                SeatWire.I32(p,68,v.Credits);
                SeatWire.I32(p,72,v.VisibleEnemy?.Seat ?? -1);
                Position(p,76,v.VisibleEnemy?.Position ?? Vector3.zero);
                SeatWire.I16(p,88,Mathf.RoundToInt(v.Motion.Velocity.x * 100));
                SeatWire.I16(p,90,Mathf.RoundToInt(v.Motion.Velocity.y * 100));
                SeatWire.I16(p,92,Mathf.RoundToInt(v.Motion.VerticalSpeed * 100));
                p[94] = (byte)Mathf.RoundToInt(Mathf.Clamp01(v.Motion.Crouch) * 255);
                p[95] = (byte)((v.Motion.Grounded ? 1 : 0) | (v.PredictionJustStopped ? 2 : 0) | SeatWire.OrderedInputCapability);
                SeatWire.I16(p,96,Mathf.Clamp(Mathf.RoundToInt(v.PredictionSpeed * 100),0,32767));
            }
            int slot = v.Match.Tick % 64;
            if (peer.SentTick[slot] != v.Match.Tick)
            {
                peer.SentTick[slot] = v.Match.Tick;
                peer.SentAt[slot] = Stopwatch.GetTimestamp();
            }
            _socket.SendTo(p, 0, length, SocketFlags.None, peer.Endpoint);
        }

        private static void Position(byte[] p, int at, Vector3 point)
        {
            SeatWire.I32(p,at,Mathf.RoundToInt(point.x*100));
            SeatWire.I32(p,at+4,Mathf.RoundToInt(point.y*100));
            SeatWire.I32(p,at+8,Mathf.RoundToInt(point.z*100));
        }
        public void Dispose() => _socket.Dispose();
    }
}
