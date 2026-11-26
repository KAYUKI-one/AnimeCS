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
        private readonly Transform[] _marks;
        private int _trail, _mark, _tick;
        internal ShotFeedback(MatchPresentation style, Transform parent)
        {
            _style = style;
            _root = new GameObject("交火反馈").transform;
            _root.SetParent(parent,false);
            _marks = new Transform[Mathf.Clamp(style.maxMarks,1,160)];
        }
        internal void Dispose(){if(_root)Object.Destroy(_root.gameObject);}
        internal void Reset()
        {
            foreach (var line in _trails) if (line) line.enabled = false;
            foreach (var mark in _marks) if (mark) mark.gameObject.SetActive(false);
        }
        internal void Advance(int tick)
        {
            _tick = tick;
            for (int i = 0; i < _trails.Length; i++)
                if (_trails[i] && _expire[i] <= tick) _trails[i].enabled = false;
        }
        internal void Render(float alpha)
        {
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
                var line = _trails[i];
                if (!line)
                {
                    var go = new GameObject("曳光");
                    go.transform.SetParent(_root,false);
                    _trails[i] = line = go.AddComponent<LineRenderer>();
                    line.sharedMaterial = _style.tracerMaterial; line.positionCount=2;
                    line.alignment = LineAlignment.View; line.textureMode = LineTextureMode.Stretch;
                    line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
                }
                float width = _style.tracerWidth * Mathf.Lerp(1, 2.1f, Mathf.InverseLerp(4,50,Vector3.Distance(start,end)));
                line.startWidth = width * .42f; line.endWidth = width;
                _from[i] = start; _to[i] = end; _birth[i] = _tick;
                _expire[i] = _tick + MatchRules.Ticks(_style.tracerSeconds);
                line.enabled = true; Render(0);
            }
            if (!wall || !_style.surfaceMarks || !_style.impactMaterial) return;
            int k = _mark++ % _marks.Length;
            if (!_marks[k])
            {
                var go = new GameObject("弹孔");
                _marks[k] = go.transform; go.transform.SetParent(_root,false);
                go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = _style.impactMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
            var mark = _marks[k];
            mark.SetPositionAndRotation(end + normal*.009f,Quaternion.LookRotation(-normal)*Quaternion.Euler(0,0,_mark*137.5f));
            mark.localScale = Vector3.one*_style.markSize; mark.gameObject.SetActive(true);
        }
    }
}
