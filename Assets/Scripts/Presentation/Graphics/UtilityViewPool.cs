using System.Collections.Generic;
using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;

namespace AnimeCS.Play
{
    internal sealed class UtilityViewPool
    {
        private readonly Transform _parent;
        private Transform _root;
        private readonly List<ThrownUtilityView> _idle=new();
        private readonly Stack<SmokeParticleView> _smoke=new();

        internal UtilityViewPool(Transform parent) => _parent=parent;

        internal IThrownUtilityView Rent(UtilityDefinition item, Vector3 position)
        {
            if (!_root)
            {
                _root=new GameObject("投掷物表现").transform;
                _root.SetParent(_parent,false);
            }
            for (int i=0;i<_idle.Count;i++)
            {
                var view=_idle[i];
                if (view.Item!=item) continue;
                _idle.RemoveAt(i); view.Show(position); return view;
            }
            return new ThrownUtilityView(item,position,_root,this);
        }

        internal ISmokeFieldView Smoke(Vector3 point,float radius,Material material)
        {
            var cloud=_smoke.Count>0 ? _smoke.Pop() : new SmokeParticleView(_root,this);
            cloud.Show(point,radius,material); return cloud;
        }

        internal void Return(ThrownUtilityView view, GameObject model)
        {
            model.SetActive(false);
            if (_idle.Count<LocalMatch.Seats) _idle.Add(view); else Object.Destroy(model);
        }

        internal void Return(SmokeParticleView view, GameObject model)
        {
            model.SetActive(false);
            if (_smoke.Count<LocalMatch.TeamSize) _smoke.Push(view); else Object.Destroy(model);
        }

        internal void Dispose() { if (_root) Object.Destroy(_root.gameObject); }
    }
}
