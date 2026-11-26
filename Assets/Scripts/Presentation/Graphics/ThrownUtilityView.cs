using AnimeCS.Content;
using UnityEngine;

namespace AnimeCS.Play
{
    internal sealed class ThrownUtilityView : IThrownUtilityView
    {
        internal UtilityDefinition Item { get; }
        private readonly UtilityViewPool _pool;
        private readonly GameObject _model;

        internal ThrownUtilityView(UtilityDefinition item, Vector3 position, Transform parent, UtilityViewPool pool)
        {
            Item = item;
            _pool = pool;
            _model = AppearancePresenter.NewRoot(item.DisplayName, parent);
            AppearancePresenter.AddVisual(item.Model, _model.transform, Vector3.zero);
            Show(position);
        }

        internal void Show(Vector3 position) { Move(position); _model.SetActive(true); }
        public void Move(Vector3 position) => _model.transform.position = position;
        public ISmokeFieldView CreateSmoke(Vector3 center, float radius) =>
            Item.Cloud ? _pool.Smoke(center,radius,Item.Cloud) : null;
        public void Dispose() => _pool.Return(this,_model);
    }
}
