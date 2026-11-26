using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimeCS.Content
{
    public sealed class ContentCatalog : ScriptableObject
    {
        [SerializeField] private List<ContentDefinition> _entries = new List<ContentDefinition>();
        [SerializeField, InspectorName("启动地图")] private MapDefinition _map;
        [SerializeField, InspectorName("基础角色")] private CharacterDefinition _character;
        [SerializeField, InspectorName("默认服装")] private OutfitDefinition _outfit;
        [SerializeField, InspectorName("本地对手与队友的角色轮换")] private List<OutfitDefinition> _seatOutfits = new List<OutfitDefinition>();
        [SerializeField, InspectorName("攻方初始手枪")] private WeaponDefinition _attackPistol;
        [SerializeField, InspectorName("守方初始手枪")] private WeaponDefinition _defendPistol;
        private Dictionary<string, ContentDefinition> _index;
        public IReadOnlyList<ContentDefinition> Entries => _entries;
        public MapDefinition Map => _map;
        internal void SelectMap(MapDefinition map) => _map=map;
        [SerializeField, InspectorName("默认近战武器")] private WeaponDefinition _knife;
        public WeaponDefinition Knife => _knife;
        public CharacterDefinition Character => _character;
        public OutfitDefinition Outfit => _outfit;
        public IReadOnlyList<OutfitDefinition> SeatOutfits => _seatOutfits;
        public WeaponDefinition StartingPistol(Side side) => side == Side.Attack ? _attackPistol : _defendPistol;

        public T Find<T>(string id) where T : ContentDefinition
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_index == null)
            {
                _index = new Dictionary<string, ContentDefinition>(StringComparer.Ordinal);
                foreach (var entry in _entries)
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.Id)) _index.TryAdd(entry.Id, entry);
            }
            return _index.TryGetValue(id, out var value) ? value as T : null;
        }

        private void OnEnable() => _index = null;
        private void OnValidate() => _index = null;
    }
}
