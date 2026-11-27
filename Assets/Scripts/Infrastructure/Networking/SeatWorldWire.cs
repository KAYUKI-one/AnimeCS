using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using AnimeCS.Content;
using UnityEngine;

namespace AnimeCS.Match
{
    internal static class SeatWorldWire
    {
        internal const byte Scene=0, Kill=1, Observer=2;
        internal const int SceneHeader=39, ItemBytes=61, ItemsPerPage=18;
        internal const int MaxNameBytes=128;
        internal static string Id(Dictionary<uint,string> ids,byte[] p,int at) =>
            ids.TryGetValue(SeatWire.U32(p,at),out string value)?value:null;
        internal static Vector3 Point(byte[] p,int at) => new Vector3(SeatWire.I32(p,at),SeatWire.I32(p,at+4),SeatWire.I32(p,at+8))*.01f;
    }

    public sealed partial class SeatDatagramHost
    {
        private sealed class PickupIdentity { internal int Id; }
        private int _nextPickupId;
        private readonly ConditionalWeakTable<GroundPickup,PickupIdentity> _dropIds=new();
        private readonly List<GroundPickup> _visibleDrops=new();
        private readonly List<Vector3> _visibleKits=new();
        private readonly byte[] _worldPacket=new byte[1200];
        private readonly byte[] _observerHandling=new byte[SeatWire.MaxHandlingBytes];

        private void WorldHeader(Peer peer,in PublicMatchView clock,int eye,byte kind)
        {
            SeatWire.HeaderFor(_worldPacket,SeatPerspective.World,SeatWire.Magic,peer.Token);
            SeatWire.I32(_worldPacket,13,clock.Round); SeatWire.I32(_worldPacket,17,clock.Tick);
            _worldPacket[21]=kind; _worldPacket[22]=(byte)(eye+1);
        }

        private void PublishWorld(Peer peer,int eye,in SeatView self,SeatView[] views,IMatchWorld world,LocalMatch match)
        {
            var clock=self.Match;
            // Terminal ticks stop advancing. Keyframe repeats must still recover a lost final datagram.
            bool reset=peer.SceneRound!=clock.Round || peer.SceneEye!=eye || clock.Phase==RoundPhase.Finished;
            if(reset || clock.Tick-peer.SceneTick>=MatchRules.TickRate/10)
            {
                SendScene(peer,eye,world,match,clock);
                peer.SceneTick=clock.Tick;
            }
            if(reset || peer.KillCount!=match.EliminationCount || clock.Tick-peer.KillTick>=MatchRules.TickRate)
            {
                SendKills(peer,eye,match,clock);
                peer.KillCount=match.EliminationCount; peer.KillTick=clock.Tick;
            }
            peer.SceneRound=clock.Round; peer.SceneEye=eye;
            if(eye>=0 && eye!=self.Seat) SendObserver(peer,views[eye]);
        }

        private void SendScene(Peer peer,int eye,IMatchWorld world,LocalMatch match,in PublicMatchView clock)
        {
            _visibleDrops.Clear(); _visibleKits.Clear();
            var objective=match.ObjectiveAt(clock.Tick);
            if(eye<0 || !objective.HasValue || !world.SeesPoint(eye,objective.Value)) objective=null;
            if(eye>=0)
            {
                var drops=match.GroundWeapons;
                for(int i=0;i<drops.Count;i++) if(world.SeesPoint(eye,drops[i].At(clock.Tick))) _visibleDrops.Add(drops[i]);
                var kits=match.GroundKits;
                for(int i=0;i<kits.Count;i++) if(world.SeesPoint(eye,kits[i])) _visibleKits.Add(kits[i]);
            }
            WorldHeader(peer,clock,eye,SeatWorldWire.Scene);
            var p=_worldPacket;
            p[26]=(byte)(objective.HasValue?1:0);
            Position(p,27,objective.GetValueOrDefault());
            int total=_visibleDrops.Count+_visibleKits.Count;
            int pages=Math.Max(1,Math.Min(255,(total+SeatWorldWire.ItemsPerPage-1)/SeatWorldWire.ItemsPerPage));
            for(int page=0;page<pages;page++)
            {
                int first=page*SeatWorldWire.ItemsPerPage;
                int count=Math.Min(total-first,SeatWorldWire.ItemsPerPage);
                p[23]=(byte)page; p[24]=(byte)pages; p[25]=(byte)count;
                int at=SeatWorldWire.SceneHeader;
                for(int i=first;i<first+count;i++,at+=SeatWorldWire.ItemBytes)
                {
                    if(i<_visibleDrops.Count) WriteDrop(p,at,_visibleDrops[i]);
                    else
                    {
                        Array.Clear(p,at,SeatWorldWire.ItemBytes); p[at]=2;
                        Position(p,at+17,_visibleKits[i-_visibleDrops.Count]);
                    }
                }
                _socket.SendTo(p,0,at,SocketFlags.None,peer.Endpoint);
            }
        }

        private void WriteDrop(byte[] p,int at,GroundPickup drop)
        {
            if(!_dropIds.TryGetValue(drop,out var key)) _dropIds.Add(drop,key=new PickupIdentity{Id=++_nextPickupId});
            p[at]=1; SeatWire.I32(p,at+1,key.Id); SeatWire.U32(p,at+5,SeatRoster.Id(drop.Item?.Id));
            SeatWire.U32(p,at+9,SeatRoster.Id(drop.Weapon?.SkinId)); SeatWire.I32(p,at+13,drop.Tick);
            Position(p,at+17,drop.Position);
            var q=drop.Rest.rotation;
            SeatWire.I16(p,at+29,SeatWire.Axis(q.x)); SeatWire.I16(p,at+31,SeatWire.Axis(q.y));
            SeatWire.I16(p,at+33,SeatWire.Axis(q.z)); SeatWire.I16(p,at+35,SeatWire.Axis(q.w));
            Position(p,at+37,drop.Launch); Position(p,at+49,drop.Apex);
        }

        private void SendKills(Peer peer,int eye,LocalMatch match,in PublicMatchView clock)
        {
            int last=match.EliminationCount, first=last+1;
            while(first>1 && match.TryGetElimination(first-1,out var previous) && previous.Round==clock.Round) first--;
            WorldHeader(peer,clock,eye,SeatWorldWire.Kill);
            var p=_worldPacket; SeatWire.I32(p,23,first-1);
            for(int seq=first;seq<=last;seq++)
            {
                if(!match.TryGetElimination(seq,out var kill)) continue;
                SeatWire.I32(p,27,seq); SeatWire.I32(p,31,kill.Tick);
                p[35]=(byte)(kill.Attacker+1); p[36]=(byte)kill.Victim;
                p[37]=(byte)((kill.Headshot?1:0)|(kill.Airborne?2:0));
                SeatWire.U32(p,38,SeatRoster.Id(kill.WeaponId));
                int at=42; WriteName(p,ref at,kill.From); WriteName(p,ref at,kill.To);
                _socket.SendTo(p,0,at,SocketFlags.None,peer.Endpoint);
            }
        }

        private static void WriteName(byte[] p,ref int at,string name)
        {
            int count=string.IsNullOrEmpty(name)?0:Encoding.UTF8.GetByteCount(name);
            if(count>SeatWorldWire.MaxNameBytes) count=0;
            p[at++]=(byte)count;
            if(count>0) at+=Encoding.UTF8.GetBytes(name,0,name.Length,p,at);
        }

        private void SendObserver(Peer peer,in SeatView v)
        {
            WorldHeader(peer,v.Match,v.Seat,SeatWorldWire.Observer);
            var p=_worldPacket;
            p[23]=(byte)v.Magazine; p[24]=(byte)v.ScopeLevel;
            SeatWire.I32(p,25,v.LastConfirmationTick); SeatWire.I32(p,29,v.Credits); p[33]=(byte)(v.Bolting?1:0);
            WritePrivate(_roster,v);
            Buffer.BlockCopy(_roster,SeatRoster.PrivateOffset,p,34,52);
            int len=SeatWire.WriteHandling(_observerHandling,v)-SeatWire.ExtendedStateBytes;
            Buffer.BlockCopy(_observerHandling,SeatWire.ExtendedStateBytes,p,86,len);
            _socket.SendTo(p,0,86+len,SocketFlags.None,peer.Endpoint);
        }
    }

    public sealed partial class SeatDatagramClient
    {
        private readonly byte[] _observedHandling=new byte[SeatWire.MaxHandlingBytes];
        private void ReadObserver(byte[] p,int size)
        {
            int tick=SeatWire.I32(p,17), eye=p[22]-1, bytes=size-86;
            if(eye<0 || eye==View.Seat || eye!=_eye || View.Health>0 || !_visible[eye] || _members[eye].Health<=0 ||
                tick<_observerTick || bytes<20 || bytes>SeatWire.MaxHandlingBytes-SeatWire.ExtendedStateBytes) return;
            var v=_members[eye];
            v.Magazine=p[23]; v.ScopeLevel=p[24]; v.LastConfirmationTick=SeatWire.I32(p,25);
            v.Credits=SeatWire.I32(p,29); v.Bolting=p[33]!=0;
            ReadPrivate(ref v,p,34);
            Buffer.BlockCopy(p,86,_observedHandling,SeatWire.ExtendedStateBytes,bytes);
            if(!SeatWire.ReadHandling(_observedHandling,SeatWire.ExtendedStateBytes+bytes,ref v,_observer)) return;
            _observer=v; _observerTick=tick;
        }
    }
}
