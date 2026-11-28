using UnityEngine;

namespace AnimeCS.Content
{
    public sealed class CharacterDefinition : ContentDefinition
    {
        [SerializeField, InspectorName("基础角色模型")] private GameObject _model;
        [SerializeField, InspectorName("角色立绘")] private Texture2D _portrait;
        [SerializeField, InspectorName("比分栏头像")] private Texture2D _icon;
        [SerializeField, InspectorName("攻方肩背标识")] private GameObject _attackInsignia;
        [SerializeField, InspectorName("守方肩背标识")] private GameObject _defendInsignia;
        [SerializeField, InspectorName("动画控制器（可选）")] private RuntimeAnimatorController _controller;
        [SerializeField, InspectorName("持枪挂点路径（可选）")] private string _weaponSocket;
        [SerializeField, InspectorName("刘海投影网格")] private Mesh _fringeMesh;
        [SerializeField, InspectorName("刘海投影材质")] private Material _fringeMaterial;
        public override ContentKind Kind => ContentKind.Character;
        [SerializeField, InspectorName("所属阵营")] private Side _faction;
        public Side Faction => _faction;
        public static string FactionName(Side side) => side == Side.Attack ? "余烬" : "苍盾";
        public GameObject Model => _model;
        public Texture2D Portrait => _portrait;
        [SerializeField, InspectorName("对阵卡正面立绘")] private Texture2D _battlePortrait;
        public Texture2D BattlePortrait => _battlePortrait ? _battlePortrait : _portrait;
        public Texture2D Icon => _icon ? _icon : _portrait;
        [SerializeField, InspectorName("头饰在头骨上的偏移")] private Vector3 _headwearOffset;
        public Vector3 HeadwearOffset=>_headwearOffset;
        [SerializeField, InspectorName("眼间点（头骨本地坐标，可选）")] private Vector3 _faceAnchor;
        [SerializeField, Min(0), InspectorName("待机时眼间点离地高度（米，可选）")] private float _standingEyes;
        [SerializeField, Range(1f,1.06f), InspectorName("对局头部视觉比例")] private float _combatHeadScale=1.025f;
        public float CombatHeadScale => Mathf.Clamp(_combatHeadScale,1f,1.06f);
        public Vector3 FaceAnchor=>_faceAnchor;
        public float StandingEyes=>_standingEyes;
        public GameObject Insignia(Side side) => side == Side.Attack ? _attackInsignia : _defendInsignia;
        public RuntimeAnimatorController Controller => _controller;
        public string WeaponSocket => _weaponSocket;
        public Mesh FringeMesh => _fringeMesh;
        public Material FringeMaterial => _fringeMaterial;
    }
}
