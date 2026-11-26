using UnityEngine;

namespace AnimeCS.Content
{
    public sealed class OutfitDefinition : ContentDefinition
    {
        [SerializeField, InspectorName("适配基础角色")] private CharacterDefinition _character;
        [SerializeField, InspectorName("第三人称服装模型")] private GameObject _model;
        [SerializeField, InspectorName("第一人称双臂 / 袖口")] private GameObject _arms;
        [SerializeField, InspectorName("手枪双手姿势")] private GameObject _sidearmArms;
        [SerializeField, InspectorName("可动第一人称手臂（Humanoid）")] private GameObject _viewRig;
        [SerializeField, InspectorName("所有本地衣柜均可使用")] private bool _included;
        [SerializeField, HideInInspector] private GarmentRegions[] _bodyRegions,_viewRegions;
        [SerializeField, HideInInspector] private Material _hiddenRegion;
        public GarmentRegions[] Regions(bool view) => view ? _viewRegions : _bodyRegions;
        public Material HiddenRegion => _hiddenRegion;
        public override ContentKind Kind => ContentKind.Outfit;
        [SerializeField, InspectorName("第三人称服装动作覆盖")] private RuntimeAnimatorController _stanceController;
        public RuntimeAnimatorController PoseController => _stanceController ? _stanceController : _character ? _character.Controller : null;
        public CharacterDefinition Character => _character;
        public GameObject Model => _model;
        public GameObject Arms => _arms;
        public GameObject ViewRig => _viewRig;
        public GameObject ViewArms(bool pistol) => pistol && _sidearmArms != null ? _sidearmArms : _arms;
        public bool Included => _included;
    }
}
