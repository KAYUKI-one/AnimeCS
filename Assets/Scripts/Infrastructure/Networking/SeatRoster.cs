using System;
using System.Collections.Generic;
using System.Net.Sockets;
using AnimeCS.Content;
using UnityEngine;

namespace AnimeCS.Match
{
    internal static class SeatRoster
    {
        internal const byte Kind = 5;
        internal const int Offset = 21, Stride = 62, PrivateOffset = Offset + LocalMatch.Seats * Stride, Size = PrivateOffset + 52;
        internal static uint Id(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            uint hash = 2166136261;
            foreach (char c in value) hash = unchecked((hash ^ c) * 16777619);
            return hash;
        }
    }

    public sealed partial class SeatDatagramHost
    {
        private readonly byte[] _roster = new byte[SeatRoster.Size];
        internal void PublishRoster(SeatView[] views, IMatchWorld world, LocalMatch match=null)
        {
            for (int viewer = 0; viewer < _peers.Length; viewer++)
            {
                var peer = _peers[viewer];
                if (peer.Endpoint == null || peer.Version != SeatWire.Magic || !peer.Handling) continue;
                var self = views[viewer];
                int eye=peer.WorldEnabled ? SelectPerspective(views,viewer,peer.RequestedSeat) : self.Health>0 ? viewer : -1;
                int identifiable=0;
                SeatWire.HeaderFor(_roster, SeatRoster.Kind, SeatWire.Magic, peer.Token);
                SeatWire.I32(_roster,13,self.Match.Round); SeatWire.I32(_roster,17,self.Match.Tick);
                for (int i = 0; i < views.Length; i++)
                {
                    int at = SeatRoster.Offset + i*SeatRoster.Stride;
                    Array.Clear(_roster,at,SeatRoster.Stride);
                    var actor = views[i];
                    bool ally=actor.Side==self.Side;
                    bool sight=eye>=0 && actor.Health>0 && (eye==i || world.Observe(eye,i,1).HasValue);
                    if(ally && sight) identifiable|=1<<i;
                    bool seen=ally || sight;
                    _roster[at] = (byte)actor.Side;
                    SeatWire.U32(_roster,at+2,SeatRoster.Id(actor.OutfitId));
                    _roster[at+6] = (byte)(seen ? 1 : 0);
                    _roster[at+7] = (byte)(actor.Health > 0 ? 1 : 0);
                    if (!seen) continue;
                    _roster[at+1] = (byte)actor.Health;
                    Position(_roster,at+8,actor.Position);
                    SeatWire.I16(_roster,at+20,SeatWire.Angle(actor.Yaw));
                    SeatWire.I16(_roster,at+22,SeatWire.Angle(actor.Pitch));
                    SeatWire.U32(_roster,at+24,SeatRoster.Id(actor.WeaponId));
                    SeatWire.U32(_roster,at+28,SeatRoster.Id(actor.SkinId));
                    SeatWire.I32(_roster,at+32,actor.LastShotTick);
                    _roster[at+36] = (byte)actor.Held;
                    _roster[at+37] = (byte)Mathf.RoundToInt(actor.Motion.Crouch*255);
                    SeatWire.U32(_roster,at+38,SeatRoster.Id(actor.UtilityId));
                    for (int slot=0;slot<5;slot++)
                        SeatWire.U32(_roster,at+42+4*slot,SeatRoster.Id(actor.Worn.Get((WearSlot)slot)));
                }
                WritePrivate(_roster,self);
                if(peer.WorldEnabled)
                {
                    PublishCompactRoster(peer,eye,identifiable);
                    if(match!=null) PublishWorld(peer,eye,self,views,world,match);
                }
                else _socket.SendTo(_roster,0,_roster.Length,SocketFlags.None,peer.Endpoint);
            }
        }

        private static void WritePrivate(byte[] p,in SeatView v)
        {
            const int at=SeatRoster.PrivateOffset;
            SeatWire.U32(p,at,SeatRoster.Id(v.PrimaryId)); SeatWire.U32(p,at+4,SeatRoster.Id(v.SidearmId));
            SeatWire.U32(p,at+8,SeatRoster.Id(v.KnifeId)); SeatWire.U32(p,at+12,SeatRoster.Id(v.UtilityId));
            SeatWire.I32(p,at+16,v.Reserve); SeatWire.I32(p,at+20,v.BuyRemaining);
            SeatWire.I32(p,at+24,v.LastHitTick); SeatWire.I32(p,at+28,v.ObjectiveHolder);
            SeatWire.I16(p,at+32,Mathf.RoundToInt(v.Magnification*100)); p[at+34]=(byte)Mathf.RoundToInt(255*v.ScopeStability);
            p[at+35]=(byte)Mathf.RoundToInt(255*v.InteractionFraction);
            p[at+36]=(byte)((v.CanBuy?1:0)|(v.Armor?2:0)|(v.Helmet?4:0)|(v.HasKit?8:0)|(v.HasObjective?16:0)|
                (v.Blinded?32:0)|(v.InsideSmoke?64:0)|(v.CanStartShot?128:0));
            p[at+37]=(byte)v.Match.PlantedSite; p[at+38]=(byte)v.Match.Winner; p[at+39]=(byte)v.Match.Reason;
            p[at+40]=(byte)(v.Utilities.Count(UtilityEffect.Smoke) | v.Utilities.Count(UtilityEffect.Flash)<<2 | v.Utilities.Count(UtilityEffect.Echo)<<4);
            p[at+41]=(byte)v.LastHitZone;
            SeatWire.U32(p,at+42,SeatRoster.Id(v.Draft.Primary)); SeatWire.U32(p,at+46,SeatRoster.Id(v.Draft.Sidearm));
            p[at+50]=(byte)((v.Draft.Armor?1:0)|(v.Draft.Helmet?2:0)|(v.Draft.Kit?4:0));
            p[at+51]=(byte)(v.Draft.Utilities.Count(UtilityEffect.Smoke) | v.Draft.Utilities.Count(UtilityEffect.Flash)<<2 | v.Draft.Utilities.Count(UtilityEffect.Echo)<<4);
        }
    }

    public sealed partial class SeatDatagramClient
    {
        private readonly Dictionary<uint,string> _ids = new();
        private readonly SeatView[] _members = new SeatView[LocalMatch.Seats];
        private readonly SeatSnapshotBuffer[] _memberPoses = new SeatSnapshotBuffer[LocalMatch.Seats];
        private readonly bool[] _visible = new bool[LocalMatch.Seats];
        private int _rosterTick = -1, _rosterRound = -1;
#if ANIMECS_RENDER_PROBE
        internal int AcceptedInputSequence => _accepted;
        internal int ReceivedRosterTick => _rosterTick;
        internal SeatView ReceivedMember(int seat) => _members[seat];
#endif
        internal bool HasRoster => (Connected || Finished) && _rosterRound == View.Match.Round && _rosterTick >= 0;
        internal void BindCatalog(ContentCatalog catalog)
        {
            _worldCatalog=catalog;
            foreach (var item in catalog.Entries)
                if (item) _ids.Add(SeatRoster.Id(item.Id),item.Id);
        }
        private string ReadId(byte[] packet,int at) => _ids.TryGetValue(SeatWire.U32(packet,at),out var id) ? id : null;

        private void ReceiveRoster(byte[] p,int length)
        {
            if (!Connected || length != SeatRoster.Size || SeatWire.U32(p,0) != SeatWire.Magic || SeatWire.Token(p) != _token) return;
            int tick = SeatWire.I32(p,17), round = SeatWire.I32(p,13);
            if (round != View.Match.Round || tick < 0 || round == _rosterRound && tick <= _rosterTick) return;
            for (int i=0;i<_members.Length;i++)
            {
                int at = SeatRoster.Offset + i*SeatRoster.Stride;
                var motion = _memberPoses[i] ??= new SeatSnapshotBuffer();
                bool visible = p[at+6] == 1;
                if (round != _rosterRound || visible != _visible[i]) motion.Clear();
                _visible[i] = visible;
                var actor = new SeatView { Seat=i, Side=(Side)p[at], OutfitId=ReadId(p,at+2), Match=View.Match,
                    Health=visible ? p[at+1] : p[at+7], Position=Position(p,at+8), Yaw=SeatWire.I16(p,at+20)*.01f,
                    Pitch=SeatWire.I16(p,at+22)*.01f, WeaponId=ReadId(p,at+24), SkinId=ReadId(p,at+28),
                    LastShotTick=SeatWire.I32(p,at+32), Held=(HeldSlot)p[at+36], UtilityId=ReadId(p,at+38),
                    Motion=new MotionContact { Crouch=p[at+37]/255f }, InspectTick=-10000, ReloadStart=-10000, EquipTick=-10000 };
                for (int slot=0;slot<5;slot++) actor.Worn.Set((WearSlot)slot,ReadId(p,at+42+4*slot));
                _members[i]=actor;
                if (visible) motion.Push(tick,actor.Position,actor.Yaw,actor.Pitch);
            }
            System.Buffer.BlockCopy(p,SeatRoster.PrivateOffset,_private,0,_private.Length);
            _rosterRound = round;
            _rosterTick = tick;
        }
        private readonly byte[] _private = new byte[52];
        internal SeatView PlayView
        {
            get
            {
                var v=View;
                if (!HasRoster) return v;
                var appearance=_members[v.Seat];
                v.Worn=appearance.Worn; v.SkinId=appearance.SkinId;
                v.WeaponId??=appearance.WeaponId; v.OutfitId??=appearance.OutfitId;
                ReadPrivate(ref v,_private,0);
                return v;
            }
        }
        private void ReadPrivate(ref SeatView v,byte[] p,int at)
        {
            v.PrimaryId=ReadId(p,at); v.SidearmId=ReadId(p,at+4); v.KnifeId=ReadId(p,at+8); v.UtilityId=ReadId(p,at+12);
            v.Reserve=SeatWire.I32(p,at+16); v.BuyRemaining=SeatWire.I32(p,at+20); v.LastHitTick=SeatWire.I32(p,at+24); v.ObjectiveHolder=SeatWire.I32(p,at+28);
            v.Magnification=SeatWire.I16(p,at+32)*.01f; v.ScopeStability=p[at+34]/255f; v.InteractionFraction=p[at+35]/255f;
            int flags=p[at+36];
            v.CanBuy=(flags&1)!=0; v.Armor=(flags&2)!=0; v.Helmet=(flags&4)!=0; v.HasKit=(flags&8)!=0;
            v.HasObjective=(flags&16)!=0; v.Blinded=(flags&32)!=0; v.InsideSmoke=(flags&64)!=0; v.CanStartShot=(flags&128)!=0;
            v.Match.PlantedSite=(Site)p[at+37]; v.Match.Winner=(Side)p[at+38]; v.Match.Reason=(WinReason)p[at+39];
            v.Utilities=Counts(p[at+40]); v.LastHitZone=(HitZone)p[at+41];
            v.Draft=new PurchaseDraft { Primary=ReadId(p,at+42), Sidearm=ReadId(p,at+46), Armor=(p[at+50]&1)!=0,
                Helmet=(p[at+50]&2)!=0, Kit=(p[at+50]&4)!=0, Utilities=Counts(p[at+51]) };
        }
        private static UtilityCounts Counts(byte packed)
        {
            var counts=new UtilityCounts();
            for (int i=0;i<3;i++)
                for (int n=0;n<((packed>>(i*2))&3);n++) counts.Add((UtilityEffect)i);
            return counts;
        }
        internal SeatView Member(int seat,out bool visible)
        {
            var member = _members[seat];
            visible = HasRoster && _visible[seat] && View.Match.Tick-_rosterTick < MatchRules.TickRate;
            if (visible && _memberPoses[seat].Sample(_clock.Sample(SnapshotClock.Now),out var pos,out var yaw,out var pitch))
            { member.Position=pos; member.Yaw=yaw; member.Pitch=pitch; }
            return member;
        }
    }
}
