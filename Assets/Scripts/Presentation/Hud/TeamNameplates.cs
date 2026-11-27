using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed class TeamNameplates
    {
        internal readonly struct Entry
        {
            internal readonly string Name;
            internal readonly Vector2 Viewport;
            internal readonly bool Visible;
            internal Entry(string name,Vector2 viewport) { Name=name; Viewport=viewport; Visible=true; }
        }
        private readonly VisualElement _layer;
        private readonly Label[] _tags;
        private readonly VisualElement[] _chevrons;
        private readonly Rect[] _placed;
        private readonly bool[] _shown;
        internal bool HasLayout => _layer.contentRect.width>1 && _layer.contentRect.height>1;
        public TeamNameplates(VisualElement hud,int count)
        {
            _tags=new Label[count]; _chevrons=new VisualElement[count]; _placed=new Rect[count]; _shown=new bool[count];
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

        public void Present(IReadOnlyList<Entry> entries,bool assist)
        {
            float width=_layer.contentRect.width, height=_layer.contentRect.height;
            int placed=0;
            for(int slot=0;slot<_tags.Length;slot++)
            {
                var tag=_tags[slot];var mark=_chevrons[slot];
                var entry=entries[slot];
                bool show=entry.Visible;
                if(show)
                {
                    var point=entry.Viewport;
                    var id=entry.Name;
                    if(tag.text!=id)tag.text=id;
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
                bool cue=assist&&show;
                if((mark.style.display.value==DisplayStyle.Flex)!=cue)mark.style.display=cue?DisplayStyle.Flex:DisplayStyle.None;
                if(_shown[slot]!=show){_shown[slot]=show;tag.style.display=show?DisplayStyle.Flex:DisplayStyle.None;}
            }
        }
    }
}
