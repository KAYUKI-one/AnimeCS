using AnimeCS.Match;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed class TeamNameplates
    {
        private readonly VisualElement _layer;
        private readonly Label[] _tags = new Label[LocalMatch.TeamSize];
        private readonly VisualElement[] _chevrons=new VisualElement[LocalMatch.TeamSize];
        private readonly Rect[] _placed = new Rect[LocalMatch.TeamSize];
        private readonly bool[] _shown = new bool[LocalMatch.TeamSize];
        public TeamNameplates(VisualElement hud)
        {
            _layer=hud.Q("team-ids");
            if (_layer==null)
            {
                _layer=new VisualElement { name="team-ids",pickingMode=PickingMode.Ignore };
                _layer.StretchToParentSize();
                hud.Insert(0,_layer);
            }
            for (int i=0;i<_tags.Length;i++)
            {
                var tag = new Label { name="team-id-"+i, pickingMode=PickingMode.Ignore, enableRichText=false };
                tag.AddToClassList("teammate-id"); tag.style.display=DisplayStyle.None;
                _tags[i]=tag; _layer.Add(tag);
                var mark=new VisualElement{name="team-chevron-"+i,pickingMode=PickingMode.Ignore};
                mark.AddToClassList("team-chevron");mark.generateVisualContent+=PaintChevron;
                mark.style.display=DisplayStyle.None;_chevrons[i]=mark;_layer.Add(mark);
            }
        }

        private static void PaintChevron(MeshGenerationContext context)
        {
            var p=context.painter2D;
            p.BeginPath();p.MoveTo(new Vector2(1,1));p.LineTo(new Vector2(11,1));p.LineTo(new Vector2(6,7));p.ClosePath();
            p.strokeColor=new Color(.04f,.12f,.15f,.96f);p.lineWidth=2;p.Stroke();
            p.fillColor=new Color(.38f,.94f,.99f);p.Fill();
        }

        public void Present(in SeatView self, in SeatView watched, Camera camera, ISeatPort[] ports,
            ArenaPawn[] actors, AppearancePresenter[] models, ArenaWorld world, bool assist, float alpha)
        {
            float width=_layer.contentRect.width, height=_layer.contentRect.height;
            bool active=watched.Seat>=0 && !watched.Blinded && !watched.InsideSmoke && !self.Match.Paused &&
                self.Match.Phase!=RoundPhase.Finished && width>1 && height>1;
            int first=self.Seat/LocalMatch.TeamSize*LocalMatch.TeamSize;
            int placed=0;
            for(int slot=0;slot<_tags.Length;slot++)
            {
                int seat=first+slot;var tag=_tags[slot];var mark=_chevrons[slot];
                bool show=active && seat!=watched.Seat && actors[seat].Alive;
                if(show)
                {
                    var head=models[seat].LabelAnchor(alpha);
                    var point=camera.WorldToViewportPoint(head);
                    var chest=camera.WorldToViewportPoint(actors[seat].transform.position+Vector3.up*actors[seat].Height*.57f);
                    bool InFrame(Vector3 p) => p.z>=camera.nearClipPlane && p.x>=0 && p.x<=1 && p.y>=0 && p.y<=1;
                    show=(InFrame(point)||InFrame(chest)) && world.VisibleAlly(camera.transform.position,seat,head-Vector3.up*.21f);
                    if(show)
                    {
                        var id=ports[seat].Read().DisplayId;
                        if(tag.text!=id)tag.text=id;
                        if(point.z<camera.nearClipPlane)point=chest;
                        float labelWidth=Mathf.Clamp(tag.resolvedStyle.width,46,Mathf.Min(width,260));
                        if(float.IsNaN(labelWidth))labelWidth=62;
                        float x=Mathf.Clamp(point.x*width, labelWidth*.5f+3,width-labelWidth*.5f-3);
                        float y=Mathf.Clamp((1-point.y)*height,24,height-3);
                        if(assist)
                        {
                            mark.style.left=x;mark.style.top=y;
                            y-=8;
                        }
                        var bounds=new Rect(x-labelWidth*.5f-2,y-19,labelWidth+4,20);
                        for(int previous=0;previous<placed;previous++)
                            if(bounds.Overlaps(_placed[previous]))bounds.y=_placed[previous].yMin-bounds.height-1;
                        _placed[placed++]=bounds;
                        tag.style.left=x;tag.style.top=Mathf.Max(20,bounds.yMax);
                    }
                }
                bool cue=assist&&show;
                if((mark.style.display.value==DisplayStyle.Flex)!=cue)mark.style.display=cue?DisplayStyle.Flex:DisplayStyle.None;
                models[seat].SetFriendlyCue(cue);
                if(_shown[slot]!=show){_shown[slot]=show;tag.style.display=show?DisplayStyle.Flex:DisplayStyle.None;}
            }
        }
    }
}
