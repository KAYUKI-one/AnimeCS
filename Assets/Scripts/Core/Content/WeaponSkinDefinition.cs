using UnityEngine;

namespace AnimeCS.Content
{
    public sealed class WeaponSkinDefinition : ContentDefinition
    {
        [SerializeField, InspectorName("适配枪械")] private WeaponDefinition _weapon;
        [SerializeField, InspectorName("替换材质")] private Material _material;
        [SerializeField, InspectorName("基础色贴图")] private Texture2D _texture;
        [SerializeField, Min(0), InspectorName("渲染器材质槽")] private int _materialSlot;
        [SerializeField, InspectorName("随试玩提供")] private bool _included;
        public bool Included => _included;
        public override ContentKind Kind => ContentKind.Skin;
        public WeaponDefinition Weapon => _weapon;
        public Material Material => _material;
        public Texture2D Texture => _texture;
        public int MaterialSlot => _materialSlot;
    }
}
