using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed class EliminationHud
    {
        private const int FeedTicks=6*MatchRules.TickRate, MedalTicks=2*MatchRules.TickRate;
        private readonly ContentCatalog _catalog;
        private readonly VisualElement _feed,_medalHost;
        private readonly KillEmblem _emblem;
        private readonly FeedRow[] _rows=new FeedRow[5];
        private readonly int[] _counts=new int[LocalMatch.Seats],_last=new int[LocalMatch.Seats];
        private int _seen,_round;

        internal EliminationHud(VisualElement root,ContentCatalog catalog)
        {
            _catalog=catalog; _feed=root.Q("kill-feed"); _medalHost=root.Q("kill-medal");
            _emblem=new KillEmblem(); _medalHost.Add(_emblem);
            for(int i=0;i<_rows.Length;i++) { _rows[i]=new FeedRow();_feed.Add(_rows[i].Root); }
        }

        internal void Reset()
        {
            _seen=0; _round=-1;
            foreach(var row in _rows)row.Hide();
        }

        internal void Present(IMatchWorldView session,in PublicMatchView clock,int watched,int local,bool visible)
        {
            if (_round!=clock.Round)
            {
                _round=clock.Round; System.Array.Clear(_counts,0,_counts.Length);
                System.Array.Fill(_last,-100000);
            }
            for(int seq=Mathf.Max(_seen+1,session.EliminationCount-31);seq<=session.EliminationCount;seq++)
            {
                if (!session.TryGetElimination(seq,out var e) || e.Round!=_round || e.Attacker<0) continue;
                _counts[e.Attacker]++; _last[e.Attacker]=e.Tick;
            }
            _seen=session.EliminationCount;
            for(int i=0;i<_rows.Length;i++)
            {
                int seq=_seen-i;
                if (session.TryGetElimination(seq,out var e) && e.Round==_round && clock.Tick-e.Tick<FeedTicks)
                    _rows[i].Show(seq,e,local,_catalog);
                else _rows[i].Hide();
            }
            _feed.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            _medalHost.style.visibility=visible&&watched>=0?Visibility.Visible:Visibility.Hidden;
            if (watched>=0) _emblem.Set(_counts[watched],clock.Tick-_last[watched]<MedalTicks);
        }

        private sealed class FeedRow
        {
            internal readonly VisualElement Root=new VisualElement{pickingMode=PickingMode.Ignore};
            private readonly Label _from=new Label(),_victim=new Label(),_weapon=new Label(),_badges=new Label();
            private readonly Image _glyph=new Image{scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
            private int _sequence;
            internal FeedRow()
            {
                Root.AddToClassList("kill-row");
                _from.AddToClassList("kill-player");_victim.AddToClassList("kill-player");
                _weapon.AddToClassList("kill-tool");_badges.AddToClassList("kill-tags");_glyph.AddToClassList("kill-glyph");
                Root.Add(_from);Root.Add(_glyph);Root.Add(_weapon);Root.Add(_badges);Root.Add(_victim);Hide();
            }
            internal void Hide() { if(_sequence==-1)return;_sequence=-1;Root.style.display=DisplayStyle.None; }
            internal void Show(int seq,in Elimination kill,int local,ContentCatalog catalog)
            {
                if(_sequence==seq)return;_sequence=seq;Root.style.display=DisplayStyle.Flex;
                _from.text=kill.Attacker<0?"装置":kill.From; _victim.text=kill.To;
                var gun=catalog.Find<WeaponDefinition>(kill.WeaponId);_glyph.image=gun?gun.Glyph:null;
                _glyph.style.display=_glyph.image?DisplayStyle.Flex:DisplayStyle.None;
                _weapon.text=gun?gun.DisplayName:"爆炸";
                _badges.text=(kill.Airborne?"空中 ":"")+(kill.Headshot?"爆头":kill.Knife?"刀杀":"");
                _badges.style.display=kill.Airborne||kill.Headshot||kill.Knife?DisplayStyle.Flex:DisplayStyle.None;
                _from.EnableInClassList("kill-ally",kill.Attacker>=0 && kill.Attacker/LocalMatch.TeamSize==local/LocalMatch.TeamSize);
                _victim.EnableInClassList("kill-ally",kill.Victim/LocalMatch.TeamSize==local/LocalMatch.TeamSize);
                Root.EnableInClassList("own-kill",kill.Attacker==local);Root.EnableInClassList("own-death",kill.Victim==local);
            }
        }

        private sealed class KillEmblem : VisualElement
        {
            private int _count=-1; private bool _lit;
            internal KillEmblem() { name="kill-insignia";pickingMode=PickingMode.Ignore;style.flexGrow=1;generateVisualContent+=Draw; }
            internal void Set(int count,bool lit)
            {
                if (_count==count&&_lit==lit)return;_count=count;_lit=lit;
                tooltip="本回合击杀 "+count;EnableInClassList("confirmed",lit);MarkDirtyRepaint();
            }
            private void Draw(MeshGenerationContext ctx)
            {
                var p=ctx.painter2D;var center=contentRect.center;float r=Mathf.Min(contentRect.width,contentRect.height)*.40f;
                if(r<2)return;
                Vector2 Pt(float x,float y)=>center+new Vector2(x*r,y*r);
                var ink=_lit?new Color(1f,.85f,.45f):_count>0?new Color(.91f,.82f,.59f):new Color(.74f,.78f,.73f,.8f);
                p.BeginPath();p.fillColor=new Color(.085f,.12f,.13f,.88f);p.Arc(center,r,0,360);p.Fill();
                p.BeginPath();p.strokeColor=ink;p.lineWidth=_lit?2.4f:1.4f;p.Arc(center,r-1,0,360);p.Stroke();
                // Broken crosshair and paired chevrons remain legible over a bright floor.
                p.lineWidth=_lit?2.6f:1.8f;p.BeginPath();
                p.MoveTo(Pt(-.74f,-.19f));p.LineTo(Pt(-.39f,-.12f));p.LineTo(Pt(0,.26f));p.LineTo(Pt(.39f,-.12f));p.LineTo(Pt(.74f,-.19f));
                p.MoveTo(Pt(-.43f,.14f));p.LineTo(Pt(0,.54f));p.LineTo(Pt(.43f,.14f));p.Stroke();
                p.BeginPath();p.fillColor=ink;p.MoveTo(Pt(0,-.63f));p.LineTo(Pt(.19f,-.32f));p.LineTo(Pt(0,-.10f));p.LineTo(Pt(-.19f,-.32f));p.ClosePath();p.Fill();
                int n=Mathf.Min(5,_count);
                for(int i=0;i<n;i++){p.BeginPath();p.Arc(Pt((i-(n-1)*.5f)*.30f,1.22f),r*.095f,0,360);p.Fill();}
            }
        }
    }
}

