using System;
using System.Collections.Generic;
using System.Text;
using AnimeCS.Content;
using UnityEngine;

namespace AnimeCS.Match
{
    internal sealed class ReplicatedMatchWorld : IMatchWorldView
    {
        private List<GroundPickup> _weapons=new(), _stagedWeapons=new();
        private List<Vector3> _kits=new(), _stagedKits=new();
        private Dictionary<int,GroundPickup> _cache=new(), _stagedCache=new();
        private readonly bool[] _pages=new bool[256];
        private int _stagingTick=-1, _pageCount, _pagesSeen;
        private Vector3? _objective;
        private readonly Elimination[] _kills=new Elimination[32];
        private readonly int[] _sequences=new int[32];
        public int EliminationCount {get; private set;}
        public IReadOnlyList<GroundPickup> GroundWeapons => _weapons;
        public IReadOnlyList<Vector3> GroundKits => _kits;
        public Vector3? ObjectiveAt(float frame) => _objective;

        internal void ResetScene()
        {
            _stagingTick=-1; _pageCount=_pagesSeen=0; _objective=null;
            _weapons.Clear(); _kits.Clear(); _cache.Clear();
            _stagedWeapons.Clear(); _stagedKits.Clear(); _stagedCache.Clear();
        }

        internal void Reset()
        {
            ResetScene(); EliminationCount=0;
            Array.Clear(_sequences,0,_sequences.Length);
        }

        public bool TryGetElimination(int sequence,out Elimination kill)
        {
            kill=default;
            if(sequence<1 || sequence>EliminationCount || _sequences[(sequence-1)%_kills.Length]!=sequence) return false;
            kill=_kills[(sequence-1)%_kills.Length]; return true;
        }

        internal void Receive(byte[] p,int size,ContentCatalog catalog,Dictionary<uint,string> ids)
        {
            if(!catalog) return;
            if(p[21]==SeatWorldWire.Scene) ReadScene(p,size,catalog,ids);
            else if(p[21]==SeatWorldWire.Kill) ReadKill(p,size,catalog,ids);
        }

        private void ReadScene(byte[] p,int size,ContentCatalog catalog,Dictionary<uint,string> ids)
        {
            if(size<SeatWorldWire.SceneHeader || p[24]==0 || p[23]>=p[24] || p[25]>SeatWorldWire.ItemsPerPage ||
                p[26]>1 || size!=SeatWorldWire.SceneHeader+p[25]*SeatWorldWire.ItemBytes) return;
            int page=p[23], count=p[24], tick=SeatWire.I32(p,17);
            if(tick<_stagingTick) return;
            if(tick!=_stagingTick)
            {
                _stagingTick=tick; _pageCount=count; _pagesSeen=0;
                Array.Clear(_pages,0,count);
                _stagedWeapons.Clear(); _stagedKits.Clear(); _stagedCache.Clear();
            }
            if(_pageCount!=count || _pages[page]) return;
            for(int at=SeatWorldWire.SceneHeader;at<size;at+=SeatWorldWire.ItemBytes)
                if(p[at]!=1 && p[at]!=2) return;
            for(int at=SeatWorldWire.SceneHeader;at<size;at+=SeatWorldWire.ItemBytes)
            {
                if(p[at]==2) { _stagedKits.Add(SeatWorldWire.Point(p,at+17)); continue; }
                int key=SeatWire.I32(p,at+1);
                if(key<=0 || _stagedCache.ContainsKey(key)) continue;
                if(!_cache.TryGetValue(key,out var pickup))
                {
                    var item=catalog.Find<ContentDefinition>(SeatWorldWire.Id(ids,p,at+5));
                    var weapon=item is WeaponDefinition gun ? new WeaponInstance(gun,SeatWorldWire.Id(ids,p,at+9)) : null;
                    var utility=item as UtilityDefinition;
                    if(weapon==null && !utility) continue;
                    var q=new Quaternion(SeatWire.I16(p,at+29)/32767f,SeatWire.I16(p,at+31)/32767f,
                        SeatWire.I16(p,at+33)/32767f,SeatWire.I16(p,at+35)/32767f);
                    var pose=new Pose(SeatWorldWire.Point(p,at+17),q.normalized);
                    pickup=new GroundPickup(pose,weapon,utility,SeatWire.I32(p,at+13),SeatWorldWire.Point(p,at+37),SeatWorldWire.Point(p,at+49));
                }
                _stagedWeapons.Add(pickup); _stagedCache.Add(key,pickup);
            }
            _pages[page]=true;
            if(++_pagesSeen!=count) return;
            _objective=p[26]==1 ? SeatWorldWire.Point(p,27) : (Vector3?)null;
            (_weapons,_stagedWeapons)=(_stagedWeapons,_weapons);
            (_kits,_stagedKits)=(_stagedKits,_kits);
            (_cache,_stagedCache)=(_stagedCache,_cache);
        }

        private void ReadKill(byte[] p,int size,ContentCatalog catalog,Dictionary<uint,string> ids)
        {
            if(size<44 || p[35]>LocalMatch.Seats || p[36]>=LocalMatch.Seats) return;
            int previous=SeatWire.I32(p,23), seq=SeatWire.I32(p,27);
            if(previous<0 || seq<=previous || (long)seq-previous>_kills.Length || seq<=EliminationCount) return;
            int at=42;
            if(!ReadName(p,size,ref at,out string from) || !ReadName(p,size,ref at,out string to) || at!=size) return;
            int slot=(seq-1)%_kills.Length;
            _sequences[slot]=seq;
            _kills[slot]=new Elimination(SeatWire.I32(p,31),SeatWire.I32(p,13),p[35]-1,p[36],from,to,
                catalog.Find<WeaponDefinition>(SeatWorldWire.Id(ids,p,38)),(p[37]&1)!=0?HitZone.Head:HitZone.Chest,(p[37]&2)!=0);
            EliminationCount=Math.Max(EliminationCount,previous);
            while(EliminationCount<int.MaxValue && _sequences[EliminationCount%_kills.Length]==EliminationCount+1) EliminationCount++;
        }

        private static bool ReadName(byte[] p,int size,ref int at,out string name)
        {
            name=null;
            if(at>=size) return false;
            int bytes=p[at++];
            if(bytes>SeatWorldWire.MaxNameBytes || bytes>size-at) return false;
            if(bytes!=0) name=Encoding.UTF8.GetString(p,at,bytes);
            at+=bytes; return true;
        }
    }
}
