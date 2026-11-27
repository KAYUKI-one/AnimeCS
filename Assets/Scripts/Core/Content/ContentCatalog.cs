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
        public string ContentVersion => Match.MatchFormat.ContentRevision;
        internal void SelectMap(MapDefinition map) => _map=map;
        [SerializeField, InspectorName("默认近战武器")] private WeaponDefinition _knife;
        public WeaponDefinition Knife => _knife;
        public CharacterDefinition Character => _character;
        public OutfitDefinition Outfit => _outfit;
        public IReadOnlyList<OutfitDefinition> SeatOutfits => _seatOutfits;
        public OutfitDefinition OutfitForSide(Side side, string preferred=null, int index=0)
        {
            var chosen=Find<OutfitDefinition>(preferred);
            if(chosen && !chosen.Archived && chosen.Character && !chosen.Character.Archived && chosen.Character.Faction==side) return chosen;
            OutfitDefinition first = null;
            int count = 0;
            foreach (var outfit in _seatOutfits)
            {
                if (!outfit || outfit.Archived || !outfit.Character || outfit.Character.Archived || outfit.Character.Faction != side) continue;
                first ??= outfit;
                count++;
            }
            if (count == 0) return null;
            int slot = Mathf.Max(0,index) % count;
            foreach (var outfit in _seatOutfits)
                if (outfit && !outfit.Archived && outfit.Character && !outfit.Character.Archived && outfit.Character.Faction == side && slot-- == 0) return outfit;
            return first;
        }
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

#if UNITY_EDITOR
        // The editor retains retired IDs and source art for migration and authoring.
        internal ContentCatalog ForPlayerBuild()
        {
            var playable = Instantiate(this);
            playable.name = name;
            playable._entries.RemoveAll(item => !item || item.Archived);
            playable._seatOutfits.RemoveAll(item => !item || item.Archived || !item.Character || item.Character.Archived);
            return playable;
        }
#endif
    }
}
