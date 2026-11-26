using AnimeCS.Match;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed class AimReticle : VisualElement
    {
        private float _gap=1.6f;
        internal AimReticle()
        {
            pickingMode=PickingMode.Ignore;
            style.position=Position.Absolute;
            style.left=style.top=-12;
            style.width=style.height=24;
            generateVisualContent+=Draw;
        }

        internal void SetMotion(in MotionContact motion,float fullSpeed)
        {
            float fraction=motion.Velocity.magnitude/Mathf.Max(.1f,fullSpeed);
            float next=1.6f+(motion.Grounded?Mathf.InverseLerp(.34f,1f,fraction):1f)*3f;
            if(Mathf.Abs(next-_gap)<.1f)return;
            _gap=next;MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var p=context.painter2D;
            Vector2 center=new(12,12);
            void Stroke(Vector2 axis)
            {
                p.BeginPath();p.MoveTo(center+axis*_gap);p.LineTo(center+axis*(_gap+2.9f));p.Stroke();
            }
            for(int pass=0;pass<2;pass++)
            {
                p.lineWidth=pass==0?2.4f:1.25f;
                p.strokeColor=pass==0?new Color(.035f,.075f,.065f,.85f):new Color(.56f,1f,.75f);
                Stroke(Vector2.left);Stroke(Vector2.right);Stroke(Vector2.up);Stroke(Vector2.down);
            }
        }
    }
}
