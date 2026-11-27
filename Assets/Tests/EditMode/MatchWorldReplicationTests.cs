using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using AnimeCS.Content;
using AnimeCS.Editor;
using AnimeCS.Match;
using NUnit.Framework;
using UnityEngine;

namespace AnimeCS.Tests
{
    public class MatchWorldReplicationTests
    {
        [Test] public void SignedTicketRequiresTheRunningMapAndContentRevision()
        {
            string room=Guid.NewGuid().ToString("N"), secret=Guid.NewGuid().ToString();
            long expires=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+60;
            string ticket=BattleTicket.Issue(secret,room,3,expires,2,"玩家","map.dust2","2");
            bool Accept(string map,string revision)=>BattleTicket.Read(secret,ticket,out _,out _,out _,out _,map,revision);
            Assert.IsTrue(Accept("map.dust2","2"));
            Assert.IsFalse(Accept("map.broadcast-station","2"));
            Assert.IsFalse(Accept("map.dust2","1"));
        }

        [Test] public void IndependentCompactRostersPreserveEveryByteAndRejectTruncatedFrames()
        {
            byte[] source=new byte[SeatRoster.Size], restored=new byte[source.Length], wire=new byte[source.Length*2];
            var rng=new System.Random(77);
            for(int trial=0;trial<5;trial++)
            {
                rng.NextBytes(source);
                for(int i=SeatRoster.Offset;i<source.Length;i++) if(trial==0 || i%(trial+1)!=0) source[i]=0;
                int size=SeatPerspective.Pack(source,SeatRoster.Offset,wire,SeatPerspective.CompactHeader);
                Assert.IsTrue(SeatPerspective.Unpack(wire,size,SeatPerspective.CompactHeader,restored,SeatRoster.Offset));
                for(int i=SeatRoster.Offset;i<source.Length;i++) Assert.AreEqual(source[i],restored[i]);
                Assert.IsFalse(SeatPerspective.Unpack(wire,size-1,SeatPerspective.CompactHeader,restored,SeatRoster.Offset));
            }
        }

        [Test] public void GroundObjectsChangeAtomicallyAcrossPagesAndRetainTheirModelIdentity()
        {
            var catalog=ContentLibrary.Catalog;
            var gun=catalog.StartingPistol(Side.Attack);
            var ids=new Dictionary<uint,string>{{SeatRoster.Id(gun.Id),gun.Id}};
            var world=new ReplicatedMatchWorld();
            byte[] Page(int tick,int page,int pages,byte type)
            {
                var p=new byte[SeatWorldWire.SceneHeader+(type==0?0:SeatWorldWire.ItemBytes)];
                SeatWire.I32(p,17,tick); p[23]=(byte)page; p[24]=(byte)pages; p[25]=(byte)(type==0?0:1);
                p[26]=1; SeatWire.I32(p,27,tick*100);
                if(type==0) return p;
                int at=SeatWorldWire.SceneHeader;
                p[at]=type; SeatWire.I32(p,at+1,13); SeatWire.U32(p,at+5,SeatRoster.Id(gun.Id));
                SeatWire.I32(p,at+17,300); SeatWire.I16(p,at+35,32767);
                return p;
            }
            void Receive(byte[] p)=>world.Receive(p,p.Length,catalog,ids);
            Receive(Page(5,0,1,1));
            var first=world.GroundWeapons[0];
            Assert.AreSame(gun,first.Weapon.Definition);
            Receive(Page(6,1,2,2)); Receive(Page(6,1,2,2));
            Assert.IsEmpty(world.GroundKits);
            Assert.AreEqual(5f,world.ObjectiveAt(6).Value.x);
            Receive(Page(6,0,2,1));
            Assert.AreSame(first,world.GroundWeapons[0]); Assert.AreEqual(1,world.GroundKits.Count);
            Receive(Page(8,0,1,0)); Receive(Page(7,0,1,1));
            Assert.IsEmpty(world.GroundWeapons); Assert.IsEmpty(world.GroundKits);
        }

        [Test] public void KillFeedWaitsForTheGapAndDeduplicatesRetransmission()
        {
            var catalog=ContentLibrary.Catalog; var world=new ReplicatedMatchWorld();
            var gun=catalog.StartingPistol(Side.Attack);
            var ids=new Dictionary<uint,string>{{SeatRoster.Id(gun.Id),gun.Id}};
            void Receive(int seq)
            {
                var p=new byte[49]; p[21]=SeatWorldWire.Kill; SeatWire.I32(p,13,3);
                SeatWire.I32(p,23,8); SeatWire.I32(p,27,seq); SeatWire.I32(p,31,100+seq);
                p[35]=2; p[36]=6; p[37]=3; SeatWire.U32(p,38,SeatRoster.Id(gun.Id));
                p[42]=3; Encoding.UTF8.GetBytes("星",0,1,p,43); p[46]=2; p[47]=66;p[48]=50;
                world.Receive(p,p.Length,catalog,ids);
            }
            Receive(10); Assert.AreEqual(8,world.EliminationCount);
            Receive(9); Receive(9); Assert.AreEqual(10,world.EliminationCount);
            Assert.IsTrue(world.TryGetElimination(10,out var hit));
            Assert.AreEqual("星",hit.From); Assert.AreEqual("B2",hit.To); Assert.IsTrue(hit.Headshot && hit.Airborne);
            Assert.AreEqual(gun.Id,hit.WeaponId); Assert.AreEqual(6,hit.Victim);
        }

        private sealed class Sight : IMatchWorld
        {
            public Contact? Observe(int eye,int first,int count) =>
                first<LocalMatch.TeamSize || first==eye+LocalMatch.TeamSize ? new Contact(first,Vector3.up) : (Contact?)null;
            public bool SeesPoint(int eye,Vector3 point)=>true;
            public void Spawn(int seat,Vector3 p,float yaw) {}
            public void SyncMovement() {}
            public void SetAlive(int seat,bool alive) {}
            public Vector3 Position(int seat)=>Vector3.right*seat;
            public Vector3 LastGround(int seat)=>Position(seat);
            public MotionContact Move(int seat,Vector2 velocity,float yaw,float pitch,bool jump,bool crouch)=>default;
            public ArenaHit? Trace(int seat,Vector2 offset,bool suppressed)=>null;
        }

        [Test] public void DeadSeatReceivesOnlyTheChosenLivingTeammatesVisionAudioAndEquipment()
        {
            var rules=ScriptableObject.CreateInstance<MatchRules>();
            try
            {
                var catalog=ContentLibrary.Catalog; var sight=new Sight();
                var match=new LocalMatch(catalog,rules,sight,17,FirstSide.Attack,null);
                using var host=new SeatDatagramHost(new IPEndPoint(IPAddress.Loopback,0),LocalMatch.Seats);
                using var client=new SeatDatagramClient(host.LocalEndpoint);
                client.BindCatalog(catalog);
                var views=new SeatView[LocalMatch.Seats];
                for(int i=0;i<views.Length;i++)
                {
                    views[i]=match.BindSeat(i).Read(); views[i].Health=100; views[i].Magazine=10+i;
                    views[i].Heard=new SoundClue{Kind=SoundKind.Step,Tick=20+i,Bearing=i%8};
                }
                int tick=0;
                void Advance()
                {
                    tick++;
                    for(int i=0;i<views.Length;i++) views[i].Match.Tick=tick;
                    host.Poll(views); host.Publish(views); host.PublishRoster(views,sight,match);
                    client.Poll();
                }
                bool Eventually(Func<bool> f)
                {
                    for(int i=0;i<100;i++) { Advance(); if(f()) return true; }
                    return false;
                }
                Assert.IsTrue(Eventually(()=>client.CanIdentifyAlly(1)));
                Assert.AreEqual(0,SeatDatagramHost.SelectPerspective(views,0,6));
                views[0].Health=0;
                Assert.IsTrue(Eventually(()=>client.WatchTeam().Seat==1));
                Assert.AreEqual(11,client.WatchTeam().Magazine); Assert.AreEqual(21,client.WatchTeam().Heard?.Tick);
                client.Member(6,out bool seen); Assert.IsTrue(seen);
                client.Member(7,out seen); Assert.IsFalse(seen);
                client.WatchTeam(1);
                Assert.IsTrue(Eventually(()=>client.WatchTeam().Seat==2));
                Assert.AreEqual(12,client.WatchTeam().Magazine);
                client.Member(6,out seen); Assert.IsFalse(seen);
                client.Member(7,out seen); Assert.IsTrue(seen);
                for(int i=0;i<LocalMatch.TeamSize;i++) views[i].Health=0;
                Assert.IsTrue(Eventually(()=>client.WatchTeam().Seat==-1));
                Assert.IsNull(client.WatchTeam().Heard);
                Assert.IsFalse(client.CanIdentifyAlly(1));
                client.Member(7,out seen); Assert.IsFalse(seen);
            }
            finally { UnityEngine.Object.DestroyImmediate(rules); }
        }
    }
}
