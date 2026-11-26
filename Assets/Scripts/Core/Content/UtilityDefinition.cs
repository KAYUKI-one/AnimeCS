using UnityEngine;

namespace AnimeCS.Content
{
    public sealed class UtilityDefinition : ContentDefinition
    {
        [SerializeField, InspectorName("效果类型")] private UtilityEffect _effect;
        [SerializeField, Min(0), InspectorName("价格")] private int _price = 200;
        [SerializeField, InspectorName("场景模型")] private GameObject _model;
        [SerializeField, Min(0), InspectorName("落地至生效（秒）")] private float _delay = 0.5f;
        [SerializeField, Min(0.1f), InspectorName("效果半径（米）")] private float _radius = 3f;
        [SerializeField, Min(0.02f), InspectorName("持续时间（秒）")] private float _duration = 9f;
        [SerializeField, InspectorName("烟幕材质")] private Material _cloud;
        public override ContentKind Kind => ContentKind.Utility;
        public UtilityEffect Effect => _effect;
        public int Price => _price;
        public GameObject Model => _model;
        internal override GameObject DropModel => _model;
        public float Delay => _delay;
        public float Radius => _radius;
        public float Duration => _duration;
        public Material Cloud => _cloud;
    }
}
