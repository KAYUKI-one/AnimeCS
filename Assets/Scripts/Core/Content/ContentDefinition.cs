using UnityEngine;

namespace AnimeCS.Content
{
    public enum ContentKind { Map, Character, Outfit, Weapon, Skin, Utility, Wearable }
    public enum Side { Attack, Defend }
    public enum Availability { Both, Attack, Defend }
    public enum WeaponClass { Pistol, Smg, Rifle, LightSniper, HeavySniper, Knife }
    public enum HitZone { Head, Chest, Abdomen, Legs }
    public enum Site { None, A, B }
    public enum UtilityEffect { Smoke, Flash, Echo }

    public abstract class ContentDefinition : ScriptableObject
    {
        [SerializeField, HideInInspector] private string _id;
        [SerializeField, InspectorName("显示名称")] private string _displayName;
        [SerializeField, InspectorName("库存缩略图")] private Texture2D _thumbnail;
        [SerializeField, InspectorName("局内物品剪影")] private Texture2D _glyph;
        public Texture2D Glyph => _glyph;
        public Texture2D Thumbnail => _thumbnail;
        [SerializeField, InspectorName("从试玩衣柜中归档")] private bool _archived;
        public bool Archived => _archived;
        public string Id => _id;
        public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? name : _displayName;
        public abstract ContentKind Kind { get; }
        internal virtual GameObject DropModel => null;
    }
}
