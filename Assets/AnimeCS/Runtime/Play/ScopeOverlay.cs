using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed class ScopeOverlay : VisualElement
    {
        private float _settled = 1f;
        internal ScopeOverlay()
        {
            name = "scope-lens";
            pickingMode = PickingMode.Ignore;
            AddToClassList("fill");
            style.display = DisplayStyle.None;
            generateVisualContent += Draw;
        }

        internal void Present(bool visible, float stability)
        {
            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (Mathf.Abs(_settled-stability) < .001f) return;
            _settled = stability;
            MarkDirtyRepaint();
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
            p.strokeColor = new Color(.035f,.037f,.032f,Mathf.Lerp(.48f,1f,_settled));
            p.lineWidth = Mathf.Lerp(2.7f,1.25f,_settled);
            p.BeginPath();
            p.MoveTo(center-Vector2.right*radius); p.LineTo(center+Vector2.right*radius);
            p.MoveTo(center-Vector2.up*radius); p.LineTo(center+Vector2.up*radius);
            p.Stroke();
        }
    }
}

