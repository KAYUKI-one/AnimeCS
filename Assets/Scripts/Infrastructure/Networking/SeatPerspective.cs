using System;
using System.Net.Sockets;
using AnimeCS.Content;
using UnityEngine;

namespace AnimeCS.Match
{
    // Negotiated extension. Each roster is independently decodable after packet loss.
    internal static class SeatPerspective
    {
        internal const byte Roster=40, World=41, Watch=42;
        internal const int CompactHeader=24, WatchBytes=22;

        internal static int Pack(byte[] source,int start,byte[] destination,int at)
        {
            while(start<source.Length)
            {
                bool zero=source[start]==0;
                int run=1;
                while(run<128 && start+run<source.Length && (source[start+run]==0)==zero) run++;
                destination[at++]=(byte)((zero?128:0)|(run-1));
                if(!zero) { Buffer.BlockCopy(source,start,destination,at,run); at+=run; }
                start+=run;
            }
            return at;
        }

        internal static bool Unpack(byte[] source,int size,int start,byte[] destination,int at)
        {
            while(start<size)
            {
                byte block=source[start++];
                int run=(block&127)+1;
                if(run>destination.Length-at) return false;
                if((block&128)!=0) Array.Clear(destination,at,run);
                else
                {
                    if(run>size-start) return false;
                    Buffer.BlockCopy(source,start,destination,at,run);
                    start+=run;
                }
                at+=run;
            }
            return at==destination.Length;
        }
    }

    public sealed partial class SeatDatagramHost
    {
        private sealed partial class Peer
        {
            internal bool WorldEnabled;
            internal int RequestedSeat=-1, WatchRound=-1, WatchSequence=-1;
            internal int SceneTick=-1, SceneRound=-1, SceneEye=-2, KillTick=-1, KillCount=-1;
        }
        private string _contentMap,_contentRevision;
        // Mixed single-byte zero and literal runs can expand to 1.5 times the input.
        private readonly byte[] _compactRoster=new byte[SeatRoster.Size*2];

        internal void SetContent(string map,string revision) { _contentMap=map; _contentRevision=revision; }

        private static void ResetPeerWorld(Peer peer)
        {
            peer.WorldEnabled=false;
            peer.RequestedSeat=peer.WatchRound=peer.WatchSequence=peer.SceneTick=peer.SceneRound=peer.KillTick=peer.KillCount=-1;
            peer.SceneEye=-2;
        }

        private static void ReceiveWatch(Peer peer,byte[] p,int length)
        {
            if(length!=SeatPerspective.WatchBytes || !peer.Handling || peer.Version!=SeatWire.Magic ||
                SeatWire.U32(p,0)!=peer.Version || SeatWire.Token(p)!=peer.Token || SeatWire.I32(p,13)!=peer.Round) return;
            int sequence=SeatWire.I32(p,17);
            if(sequence<0 || peer.WatchRound==peer.Round && sequence<peer.WatchSequence || p[21]>LocalMatch.Seats) return;
            peer.WatchRound=peer.Round; peer.WatchSequence=sequence;
            peer.RequestedSeat=p[21]-1; peer.WorldEnabled=true;
        }

        internal static int SelectPerspective(SeatView[] views,int viewer,int requested)
        {
            if(views[viewer].Health>0) return viewer;
            int first=viewer/LocalMatch.TeamSize*LocalMatch.TeamSize;
            int end=Math.Min(views.Length,first+LocalMatch.TeamSize);
            if(requested>=first && requested<end && views[requested].Health>0) return requested;
            for(int i=first;i<end;i++) if(views[i].Health>0) return i;
            return -1;
        }

        private void PublishCompactRoster(Peer peer,int eye,int identifiable)
        {
            var p=_compactRoster;
            Buffer.BlockCopy(_roster,0,p,0,SeatRoster.Offset);
            p[4]=SeatPerspective.Roster; p[21]=(byte)(eye+1); SeatWire.I16(p,22,identifiable);
            int size=SeatPerspective.Pack(_roster,SeatRoster.Offset,p,SeatPerspective.CompactHeader);
            _socket.SendTo(p,0,size,SocketFlags.None,peer.Endpoint);
        }
    }

    public sealed partial class SeatDatagramClient
    {
        private ContentCatalog _worldCatalog;
        private readonly byte[] _watch=new byte[SeatPerspective.WatchBytes];
        private readonly byte[] _expandedRoster=new byte[SeatRoster.Size];
        private int _watchSequence,_requestedSeat=-1,_eye=-1,_identified;
        private double _nextWatch;
        private SeatView _observer;
        private int _observerTick=-1;
        private readonly ReplicatedMatchWorld _worldView=new ReplicatedMatchWorld();
        internal IMatchWorldView WorldView => _worldView;

        private void ResetWorld()
        {
            _eye=_requestedSeat=_observerTick=-1; _identified=_watchSequence=0; _nextWatch=0;
            _observer=default; _worldView.Reset();
        }

        private void TickWatch(double now)
        {
            if(now<_nextWatch || !_hasHandling) return;
            SeatWire.HeaderFor(_watch,SeatPerspective.Watch,SeatWire.Magic,_token);
            SeatWire.I32(_watch,13,View.Match.Round); SeatWire.I32(_watch,17,_watchSequence);
            _watch[21]=(byte)(_requestedSeat+1);
            Send(_watch,_watch.Length); _nextWatch=now+1;
        }

        internal bool CanIdentifyAlly(int seat) => HasRoster && (uint)seat<LocalMatch.Seats &&
            View.Match.Tick-_rosterTick<MatchRules.TickRate && (_identified&(1<<seat))!=0;

        internal SeatView WatchTeam(int direction=0)
        {
            if(View.Health>0) return PlayView;
            if(direction!=0 && HasRoster)
            {
                int first=View.Seat/LocalMatch.TeamSize*LocalMatch.TeamSize;
                int current=_requestedSeat>=first && _requestedSeat<first+LocalMatch.TeamSize && _members[_requestedSeat].Health>0
                    ? _requestedSeat : _eye>=0 ? _eye : View.Seat;
                for(int i=1;i<=LocalMatch.TeamSize;i++)
                {
                    int seat=first+(current-first+LocalMatch.TeamSize+Math.Sign(direction)*i)%LocalMatch.TeamSize;
                    if(_members[seat].Health<=0) continue;
                    _requestedSeat=seat; _watchSequence++; _nextWatch=0; TickWatch(SnapshotClock.Now); break;
                }
            }
            if(HasRoster && _eye>=0 && _eye!=View.Seat && _observerTick>=0 && _observer.Seat==_eye &&
                View.Match.Tick-_observerTick<MatchRules.TickRate && _members[_eye].Health>0)
            {
                var watched=_observer;
                var actor=Member(_eye,out bool visible);
                if(visible)
                {
                    watched.Position=actor.Position; watched.Yaw=actor.Yaw; watched.Pitch=actor.Pitch;
                    watched.Match=PlayView.Match; return watched;
                }
            }
            return new SeatView{Match=PlayView.Match,Seat=-1,ObjectiveHolder=-1};
        }

        private void ReceiveMatchWorld(byte[] p,int length)
        {
            if(!Connected || length<SeatPerspective.CompactHeader || SeatWire.U32(p,0)!=SeatWire.Magic ||
                SeatWire.Token(p)!=_token || SeatWire.I32(p,13)!=View.Match.Round || SeatWire.I32(p,17)<0) return;
            if(p[4]==SeatPerspective.Roster)
            {
                int tick=SeatWire.I32(p,17), eye=p[21]-1;
                if(p[21]>LocalMatch.Seats || tick<=_rosterTick && _rosterRound==View.Match.Round ||
                    eye>=0 && eye/LocalMatch.TeamSize!=View.Seat/LocalMatch.TeamSize ||
                    !SeatPerspective.Unpack(p,length,SeatPerspective.CompactHeader,_expandedRoster,SeatRoster.Offset)) return;
                if(_eye!=eye)
                {
                    foreach(var poses in _memberPoses) poses?.Clear();
                    _observerTick=-1; _worldView.ResetScene();
                }
                _eye=eye; _identified=(ushort)SeatWire.I16(p,22);
                Buffer.BlockCopy(p,0,_expandedRoster,0,SeatRoster.Offset);
                ReceiveRoster(_expandedRoster,_expandedRoster.Length);
                return;
            }
            if(p[22]-1!=_eye || !HasRoster) return;
            if(p[21]==SeatWorldWire.Observer) ReadObserver(p,length);
            else _worldView.Receive(p,length,_worldCatalog,_ids);
        }
    }
}
