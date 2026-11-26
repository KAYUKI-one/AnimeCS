using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed class ScopeOverlay : VisualElement
    {
        private readonly VisualElement _reticle=new();
        private bool _visible;
        private float _settled = 1f;
        internal ScopeOverlay()
        {
            name = "scope-lens";
            pickingMode = PickingMode.Ignore;
            AddToClassList("fill");
            style.visibility = Visibility.Hidden;
            generateVisualContent += Draw;
            _reticle.pickingMode=PickingMode.Ignore; _reticle.AddToClassList("fill");
            _reticle.generateVisualContent+=DrawReticle; Add(_reticle);
        }

        internal void Present(bool visible, float stability)
        {
            if(_visible!=visible){_visible=visible; style.visibility=visible?Visibility.Visible:Visibility.Hidden;}
            if(!visible)return;
            if (Mathf.Abs(_settled-stability) < .001f) return;
            _settled = stability;
            _reticle.style.opacity=Mathf.Lerp(.52f,1f,stability);
        }

        private void Draw(MeshGenerationContext context)
        {
            var box = contentRect;
            var center = box.center;
            float radius = Mathf.Min(box.width, box.height) * .465f;
            var p = context.painter2D;
            p.fillColor = new Color(.012f,.015f,.014f,1f);
            p.BeginPath();
            p.MoveTo(box.min); p.LineTo(new Vector2(box.xMax,box.yMin));
            p.LineTo(box.max); p.LineTo(new Vector2(box.xMin,box.yMax)); p.ClosePath();
            p.MoveTo(center + Vector2.right * radius); p.Arc(center,radius,0,360); p.ClosePath();
            p.Fill(FillRule.OddEven);
            p.strokeColor = new Color(.045f,.05f,.047f,1f);
            p.lineWidth = 3f;
            p.BeginPath(); p.Arc(center,radius,0,360); p.Stroke();
        }
        private void DrawReticle(MeshGenerationContext context)
        {
            var box=_reticle.contentRect; var center=box.center;
            float radius=Mathf.Min(box.width,box.height)*.465f;
            var p=context.painter2D;
            p.strokeColor=new Color(.035f,.037f,.032f,1);
            p.lineWidth=1.4f;
            p.BeginPath();
            p.MoveTo(center-Vector2.right*radius); p.LineTo(center+Vector2.right*radius);
            p.MoveTo(center-Vector2.up*radius); p.LineTo(center+Vector2.up*radius);
            p.Stroke();
        }
    }
}

