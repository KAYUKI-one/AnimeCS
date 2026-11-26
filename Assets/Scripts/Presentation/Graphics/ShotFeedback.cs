using AnimeCS.Match;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimeCS.Play
{
    internal sealed class ShotFeedback
    {
        private readonly MatchPresentation _style;
        private readonly Transform _root;
        private readonly LineRenderer[] _trails = new LineRenderer[24];
        private readonly int[] _expire = new int[24], _birth = new int[24];
        private readonly Vector3[] _from = new Vector3[24], _to = new Vector3[24];
        private SurfaceMarkBatch _marks;
        internal int SurfaceMarkCount => _marks?.Count ?? 0;
        private int _trail, _tick;
        internal ShotFeedback(MatchPresentation style, Transform parent)
        {
            _style = style;
            _root = new GameObject("交火反馈").transform;
            _root.SetParent(parent,false);
        }
        internal void Dispose(){_marks?.Dispose(); _marks=null; if(_root)Object.Destroy(_root.gameObject);}
        internal void Warm()
        {
            if (_style.surfaceMarks && _style.impactMaterial)
                _marks ??= new SurfaceMarkBatch(_style.impactMaterial, Mathf.Clamp(_style.maxMarks, 1, 4096));
            if (!_style.tracers || !_style.tracerMaterial || _trails[0]) return;
            for (int i=0; i<_trails.Length; i++)
            {
                var go = new GameObject("曳光");
                go.transform.SetParent(_root,false);
                var line = _trails[i] = go.AddComponent<LineRenderer>();
                line.enabled=false; line.sharedMaterial=_style.tracerMaterial; line.positionCount=2;
                line.alignment=LineAlignment.View; line.textureMode=LineTextureMode.Stretch;
                line.shadowCastingMode=ShadowCastingMode.Off; line.receiveShadows=false;
            }
        }
        internal void Reset()
        {
            foreach (var line in _trails) if (line) line.enabled = false;
            _marks?.Reset();
        }
        internal void Advance(int tick)
        {
            _tick = tick;
            for (int i = 0; i < _trails.Length; i++)
                if (_trails[i] && _expire[i] <= tick) _trails[i].enabled = false;
        }
        internal void Render(float alpha)
        {
            _marks?.Draw();
            for (int i = 0; i < _trails.Length; i++)
            {
                var line = _trails[i];
                if (!line || !line.enabled) continue;
                float age = Mathf.Clamp01((_tick + alpha - _birth[i]) / (_expire[i] - _birth[i]));
                var path = _to[i] - _from[i];
                float tail = Mathf.Clamp(5f / Mathf.Max(.1f, path.magnitude), .06f, .73f);
                float head = Mathf.Lerp(tail * .68f, 1f + tail, age);
                line.SetPosition(0, _from[i] + path * Mathf.Clamp01(head - tail));
                line.SetPosition(1, _from[i] + path * Mathf.Clamp01(head));
                var color = Color.white; color.a = 1 - Mathf.SmoothStep(0,1,Mathf.InverseLerp(.67f,1,age));
                line.startColor = line.endColor = color;
            }
        }

        internal void Show(Vector3 start, Vector3 end, Vector3 normal, bool drawTrail, bool wall)
        {
            if (drawTrail && _style.tracers && _style.tracerMaterial && (end-start).sqrMagnitude > .3f)
            {
                int i = _trail++ % _trails.Length;
                if (!_trails[i]) Warm();
                var line = _trails[i];
                float width = _style.tracerWidth * Mathf.Lerp(1, 2.1f, Mathf.InverseLerp(4,50,Vector3.Distance(start,end)));
                line.startWidth = width * .42f; line.endWidth = width;
                _from[i] = start; _to[i] = end; _birth[i] = _tick;
                _expire[i] = _tick + MatchRules.Ticks(_style.tracerSeconds);
                line.enabled = true;
            }
            if (!wall || !_style.surfaceMarks || !_style.impactMaterial) return;
            if (_marks==null) Warm();
            _marks.Add(end, normal, _style.markSize);
        }
    }
}
