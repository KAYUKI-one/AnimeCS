using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace AnimeCS.Match
{
    // One explicitly bound human seat. The default local demo opens no socket.
    internal static class SeatWire
    {
        internal const uint Magic=0x53434102;
        internal const byte Hello=1, State=2, Input=3;
        internal const int Header=13, StateBytes=52, InputBytes=46;
        internal static void U32(byte[] p,int at,uint n)=>BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(at,4),n);
        internal static uint U32(byte[] p,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(at,4));
        internal static void I32(byte[] p,int at,int n)=>U32(p,at,unchecked((uint)n));
        internal static int I32(byte[] p,int at)=>unchecked((int)U32(p,at));
        internal static void I16(byte[] p,int at,int n)=>BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(at,2),(short)n);
        internal static short I16(byte[] p,int at)=>BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(at,2));
        internal static ulong Token(byte[] p)=>BinaryPrimitives.ReadUInt64LittleEndian(p.AsSpan(5,8));
        internal static byte[] Packet(byte kind,int size,ulong token)
        {
            var p=new byte[size];U32(p,0,Magic);p[4]=kind;
            BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(5,8),token);return p;
        }
        internal static bool Valid(byte[] p,byte kind,int size)=>p.Length==size && U32(p,0)==Magic && p[4]==kind;
        internal static int Angle(float x)=>Mathf.RoundToInt(Mathf.DeltaAngle(0,x)*100f);
        internal static int Axis(float x)=>Mathf.RoundToInt(Mathf.Clamp(x,-1,1)*32767f);
        internal static uint Flags(SeatIntent i)=>(i.Fire?1u:0)|(i.FirePressed?2u:0)|(i.Jump?4u:0)|(i.Crouch?8u:0)|
            (i.Walk?16u:0)|(i.Interact?32u:0)|(i.Reload?64u:0)|(i.Zoom?128u:0)|(i.DropHeld?256u:0)|
            (i.Inspect?512u:0)|(i.ThrowUtility?1024u:0)|(i.SwitchWeapon?2048u:0)|(i.LowerScope?4096u:0)|(i.DropObjective?8192u:0);
        internal static bool ValidInput(byte[] p)=>p.Length>=InputBytes && p.Length<=InputBytes+80 && p.Length==InputBytes+p[45] && U32(p,0)==Magic && p[4]==Input;
        internal static SeatIntent Intent(byte[] p)
        {
            uint f=U32(p,25);
            return new SeatIntent{Fire=(f&1)!=0,FirePressed=(f&2)!=0,Jump=(f&4)!=0,Crouch=(f&8)!=0,Walk=(f&16)!=0,
                Interact=(f&32)!=0,Reload=(f&64)!=0,Zoom=(f&128)!=0,DropHeld=(f&256)!=0,Inspect=(f&512)!=0,
                ThrowUtility=(f&1024)!=0,SwitchWeapon=(f&2048)!=0,LowerScope=(f&4096)!=0,DropObjective=(f&8192)!=0,
                Yaw=I16(p,29)*.01f,Pitch=I16(p,31)*.01f,Move=new Vector2(I16(p,33),I16(p,35))/32767f,
                BrakeTap=new Vector2(I16(p,37),I16(p,39))/32767f,Cycle=(sbyte)p[41],Select=(HeldSlot)p[42],
                Buy=(BuySlot)p[43],CancelPurchase=p[44]!=0,ItemId=p[45]>0?System.Text.Encoding.UTF8.GetString(p,InputBytes,p[45]):null};
        }
    }

    public sealed class SeatDatagramHost : IDisposable
    {
        private readonly UdpClient _socket;
        private IPEndPoint _peer;
        private ulong _token;
        private readonly long[] _sent=new long[64];
        private readonly int[] _sentTick=new int[64];
        private long _lastContact;
        private float _rtt=.08f;
        private int _lastTick=-1;
        public IPEndPoint LocalEndpoint=>(IPEndPoint)_socket.Client.LocalEndPoint;
        public HostSeatInput Input {get;private set;}=new HostSeatInput();
        public bool Connected=>_peer!=null;
        public SeatDatagramHost(IPEndPoint endpoint)
        {
            _socket=new UdpClient(endpoint);
            if(Environment.OSVersion.Platform==PlatformID.Win32NT)
                _socket.Client.IOControl(unchecked((int)0x9800000C),new byte[4],null); // Ignore advisory ICMP port-unreachable.
        }
        public void Poll(in SeatView view)
        {
            long now=Stopwatch.GetTimestamp();
            if(_peer!=null && (now-_lastContact>5*Stopwatch.Frequency || view.Match.Tick<_lastTick)){_peer=null;Input=new HostSeatInput();}
            _lastTick=view.Match.Tick;
            Input.ObserveRound(view.Match.Round,view.Match.Tick,_rtt);
            for(int i=0;i<32 && _socket.Available>0;i++)
            {
                IPEndPoint sender=null;var p=_socket.Receive(ref sender);
                if(SeatWire.Valid(p,SeatWire.Hello,SeatWire.Header))
                {
                    if(_peer==null)
                    {
                        _peer=sender;_token=BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(),0);
                        _lastContact=now;Array.Fill(_sentTick,-1);
                    }
                    if(_peer.Equals(sender)) Publish(view);
                    continue;
                }
                if(_peer==null || !_peer.Equals(sender) || !SeatWire.ValidInput(p) ||
                    SeatWire.Token(p)!=_token || SeatWire.I32(p,13)!=view.Match.Round)continue;
                int ack=SeatWire.I32(p,17);
                if(ack<0 || _sentTick[ack%64]!=ack || p[42]>(byte)HeldSlot.Utility || p[43]>(byte)BuySlot.Helmet || p[44]>1 || Math.Abs((int)(sbyte)p[41])>1)continue;
                if(!Input.Receive(SeatWire.I32(p,21),ack,SeatWire.Intent(p)))continue;
                float elapsed=(float)((now-_sent[ack%64])/(double)Stopwatch.Frequency);
                _rtt=Mathf.Lerp(_rtt,Mathf.Clamp(elapsed,0,.4f),.25f); _lastContact=now;
            }
            Input.ObserveRound(view.Match.Round,view.Match.Tick,_rtt);
        }
        public void Publish(in SeatView v)
        {
            if(_peer==null)return;
            var p=SeatWire.Packet(SeatWire.State,SeatWire.StateBytes,_token);
            SeatWire.I32(p,13,v.Match.Round);SeatWire.I32(p,17,v.Match.Tick);p[21]=(byte)v.Seat;p[22]=(byte)v.Match.Phase;p[23]=(byte)v.Side;
            SeatWire.I32(p,24,Mathf.RoundToInt(v.Position.x*100));SeatWire.I32(p,28,Mathf.RoundToInt(v.Position.y*100));SeatWire.I32(p,32,Mathf.RoundToInt(v.Position.z*100));
            SeatWire.I16(p,36,SeatWire.Angle(v.Yaw));SeatWire.I16(p,38,SeatWire.Angle(v.Pitch));
            p[40]=(byte)Mathf.Clamp(v.Health,0,255);p[41]=(byte)Mathf.Clamp(v.Magazine,0,255);p[42]=(byte)v.Held;p[43]=(byte)v.ScopeLevel;
            SeatWire.I32(p,44,v.LastShotTick);SeatWire.I32(p,48,v.LastConfirmationTick);
            int slot=v.Match.Tick%64;
            if(_sentTick[slot]!=v.Match.Tick){_sentTick[slot]=v.Match.Tick;_sent[slot]=Stopwatch.GetTimestamp();}
            _socket.Send(p,p.Length,_peer);
        }
        public void Dispose()=>_socket.Dispose();
    }

    public sealed class SeatDatagramClient : IDisposable
    {
        private readonly UdpClient _socket;
        private ulong _token;
        private int _sequence;
        private long _hello,_lastState;
        public bool Connected=>_token!=0;
        public SeatView View {get;private set;}
        public SeatSnapshotBuffer Poses {get;}=new SeatSnapshotBuffer();
        public SeatDatagramClient(IPEndPoint server)
        {
            _socket=new UdpClient(server.AddressFamily);_socket.Connect(server);
            if(Environment.OSVersion.Platform==PlatformID.Win32NT)
                _socket.Client.IOControl(unchecked((int)0x9800000C),new byte[4],null);
            Hello();
        }
        private void Hello(){var p=SeatWire.Packet(SeatWire.Hello,SeatWire.Header,0);_socket.Send(p,p.Length);_hello=Stopwatch.GetTimestamp();}
        public void Poll()
        {
            long now=Stopwatch.GetTimestamp();
            if(Connected && now-_lastState>5*Stopwatch.Frequency){_token=0;_sequence=0;Poses.Clear();}
            if(!Connected && now-_hello>Stopwatch.Frequency/2)Hello();
            for(int i=0;i<32 && _socket.Available>0;i++)
            {
                IPEndPoint from=null;var p=_socket.Receive(ref from);
                if(!SeatWire.Valid(p,SeatWire.State,SeatWire.StateBytes))continue;
                ulong token=SeatWire.Token(p);int tick=SeatWire.I32(p,17),round=SeatWire.I32(p,13);
                if(token==0 || tick<0 || round<0 || (Connected && (token!=_token || tick<=View.Match.Tick)))continue;
                if(!Connected || View.Match.Round!=round || (View.Health==0)!=(p[40]==0))Poses.Clear();
                _token=token; _lastState=now;
                View=new SeatView{Seat=p[21],Match=new PublicMatchView{Round=round,Tick=tick,Phase=(RoundPhase)p[22]},
                    Side=(Content.Side)p[23],Position=new Vector3(SeatWire.I32(p,24),SeatWire.I32(p,28),SeatWire.I32(p,32))*.01f,
                    Yaw=Mathf.Repeat(SeatWire.I16(p,36)*.01f,360),Pitch=SeatWire.I16(p,38)*.01f,Health=p[40],Magazine=p[41],
                    Held=(HeldSlot)p[42],ScopeLevel=p[43],LastShotTick=SeatWire.I32(p,44),LastConfirmationTick=SeatWire.I32(p,48)};
                Poses.Push(tick,View.Position,View.Yaw,View.Pitch);
            }
        }
        public bool Submit(SeatIntent intent)
        {
            if(!Connected)return false;
            int size=string.IsNullOrEmpty(intent.ItemId)?0:System.Text.Encoding.UTF8.GetByteCount(intent.ItemId);
            if(size>80)return false;
            var p=SeatWire.Packet(SeatWire.Input,SeatWire.InputBytes+size,_token);
            p[43]=(byte)intent.Buy;p[44]=(byte)(intent.CancelPurchase?1:0);p[45]=(byte)size;
            if(size>0)System.Text.Encoding.UTF8.GetBytes(intent.ItemId,0,intent.ItemId.Length,p,SeatWire.InputBytes);
            SeatWire.I32(p,13,View.Match.Round);SeatWire.I32(p,17,View.Match.Tick);SeatWire.I32(p,21,++_sequence);SeatWire.U32(p,25,SeatWire.Flags(intent));
            SeatWire.I16(p,29,SeatWire.Angle(intent.Yaw));SeatWire.I16(p,31,SeatWire.Angle(intent.Pitch));SeatWire.I16(p,33,SeatWire.Axis(intent.Move.x));SeatWire.I16(p,35,SeatWire.Axis(intent.Move.y));
            SeatWire.I16(p,37,SeatWire.Axis(intent.BrakeTap.x));SeatWire.I16(p,39,SeatWire.Axis(intent.BrakeTap.y));p[41]=(byte)(sbyte)Math.Sign(intent.Cycle);p[42]=(byte)intent.Select;
            _socket.Send(p,p.Length);return true;
        }
        public void Dispose()=>_socket.Dispose();
    }
}
