using System;
using UnityEngine;

namespace AnimeCS.Content
{
    public enum WearSlot { Headwear, Top, Gloves, Bottom, Shoes }

    [Serializable]
    public struct WornItems : IEquatable<WornItems>
    {
        public string headwear, top, gloves, bottom, shoes;
        public bool Equals(WornItems other) => headwear==other.headwear && top==other.top && gloves==other.gloves && bottom==other.bottom && shoes==other.shoes;
        public string Get(WearSlot slot) => slot switch {
            WearSlot.Headwear => headwear, WearSlot.Top => top, WearSlot.Gloves => gloves,
            WearSlot.Bottom => bottom, WearSlot.Shoes => shoes, _ => null };
        public void Set(WearSlot slot, string id)
        {
            switch (slot)
            {
                case WearSlot.Headwear: headwear=id; break;
                case WearSlot.Top: top=id; break;
                case WearSlot.Gloves: gloves=id; break;
                case WearSlot.Bottom: bottom=id; break;
                case WearSlot.Shoes: shoes=id; break;
            }
        }
    }

    [Serializable]
    public struct WearableFit { public CharacterDefinition character; public GameObject model, viewModel; }
    public sealed class WearableDefinition : ContentDefinition
    {
        [SerializeField, InspectorName("部位")] private WearSlot _slot;
        [SerializeField, InspectorName("适配角色（留空通用）")] private CharacterDefinition _character;
        [SerializeField, InspectorName("第三人称配件")] private GameObject _model;
        [SerializeField, InspectorName("第一人称配件")] private GameObject _viewModel;
        [SerializeField, InspectorName("替换部位材质")] private Material _material;
        [SerializeField, InspectorName("基础衣柜物品")] private bool _included;
        public override ContentKind Kind => ContentKind.Wearable;
        public WearSlot Slot => _slot;
        [SerializeField, InspectorName("按角色适配的配件模型")] private WearableFit[] _fits;
        public GameObject Model(bool firstPerson,CharacterDefinition character=null)
        {
            if(character && _fits!=null)foreach(var fit in _fits)if(fit.character==character)return firstPerson?fit.viewModel:fit.model;
            return firstPerson ? _viewModel : _model;
        }
        public Material Material => _material;
        public bool Included => _included;
        public bool Fits(CharacterDefinition character) => !_character || _character == character;
    }
}