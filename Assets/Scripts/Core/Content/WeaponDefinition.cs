using System;
using UnityEngine;

namespace AnimeCS.Content
{
    public enum InspectStyle { Automatic, LongGun, Sidearm, Precision, StraightBlade, RingBlade, FoldingBlade }

    public sealed class WeaponDefinition : ContentDefinition
    {
        [SerializeField, InspectorName("持枪模型 · 第三人称 / 地面")] private GameObject _worldModel;
        [SerializeField, InspectorName("持枪模型 · 第一人称")] private GameObject _viewModel;
        [SerializeField, InspectorName("默认展示涂装")] private WeaponSkinDefinition _defaultSkin;
        [SerializeField, InspectorName("第三人称枪模位置偏移")] private Vector3 _worldOffset;
        [SerializeField, InspectorName("第三人称枪模旋转偏移")] private Vector3 _worldAngles;
        [SerializeField, InspectorName("第一人称枪模位置偏移")] private Vector3 _viewOffset;
        [SerializeField, InspectorName("第一人称枪模旋转偏移")] private Vector3 _viewAngles;
        [SerializeField, InspectorName("右手中指根接触点（枪模坐标）")] private Vector3 _grip = new Vector3(.026f, 0, .021f);
        [SerializeField, InspectorName("左手中指根接触点（枪模坐标）")] private Vector3 _support = new Vector3(.025f, .035f, .22f);
        [SerializeField, InspectorName("枪托靠肩点（无枪托时为零）")] private Vector3 _shoulderPad;
        [SerializeField, InspectorName("左手环握前端")] private bool _uprightSupport;
        [SerializeField, InspectorName("枪口中心（模型本地坐标）")] private Vector3 _muzzle;
        [SerializeField, InspectorName("独立手模枪口覆盖（可选）")] private Vector3 _viewMuzzle;
        public Vector3 Muzzle(bool view) => view && _viewMuzzle!=Vector3.zero ? _viewMuzzle : _muzzle;
        public Vector3 Grip => _grip;
        public Vector3 Support => _support;
        public Vector3 ShoulderPad => _shoulderPad;
        public bool UprightSupport => _uprightSupport;
        [SerializeField, InspectorName("武器类型")] private WeaponClass _class;
        [SerializeField, InspectorName("商店阵营")] private Availability _availability;
        [SerializeField, Min(0), InspectorName("价格")] private int _price = 500;
        [SerializeField, Min(1), InspectorName("基础伤害")] private int _damage = 30;
        [SerializeField, Range(0.01f, 1f), InspectorName("对甲系数")] private float _penetration = 0.8f;
        [SerializeField, Range(0.01f, 1f), InspectorName("每 12.7 米伤害系数")] private float _falloff = 0.98f;
        [SerializeField, Min(1), InspectorName("每分钟射速")] private int _rpm = 400;
        [SerializeField, Min(1), InspectorName("弹匣容量")] private int _magazine = 20;
        [SerializeField, Min(0), InspectorName("备弹")] private int _reserve = 60;
        [SerializeField, Min(0.02f), InspectorName("换弹秒数")] private float _reload = 2.2f;
        [SerializeField, Min(0.1f), InspectorName("奔跑速度")] private float _runSpeed = 6f;
        [SerializeField, InspectorName("消音")] private bool _suppressed;
        public override ContentKind Kind => ContentKind.Weapon;
        [SerializeField, InspectorName("持握动画模型（可含 ReloadInsert 节点）")] private GameObject _handlingModel;
        public GameObject WorldModel => _worldModel;
        internal override GameObject DropModel => _worldModel;
        public GameObject PosedModel(bool first) => _handlingModel && (!first || !_viewModel || _viewModel==_worldModel)
            ? _handlingModel : first ? ViewModel : WorldModel;
        public GameObject ViewModel => _viewModel != null ? _viewModel : _worldModel;
        public WeaponSkinDefinition DefaultSkin => _defaultSkin;
        public Vector3 Offset(bool firstPerson) => firstPerson ? _viewOffset : _worldOffset;
        public Quaternion Rotation(bool firstPerson) => Quaternion.Euler(firstPerson ? _viewAngles : _worldAngles);
        [SerializeField, InspectorName("检视动作模板")] private InspectStyle _inspect;
        public InspectStyle Inspect => _inspect!=InspectStyle.Automatic ? _inspect : _class switch
        {
            WeaponClass.Knife=>InspectStyle.StraightBlade, WeaponClass.Pistol=>InspectStyle.Sidearm,
            WeaponClass.LightSniper or WeaponClass.HeavySniper=>InspectStyle.Precision,_=>InspectStyle.LongGun
        };
        public WeaponClass Class => _class;
        public int Price => _price;
        public int Magazine => _magazine;
        public int Reserve => _reserve;
        public float ReloadSeconds => _reload;
        public float RunSpeed => _runSpeed;
        public float ShotsPerMinute => _rpm;
        public float SoundRadius => _suppressed ? 18f : 38f;
        public bool Suppressed => _suppressed;
        [SerializeField, InspectorName("狙击镜与散布")] private SniperOptics _optics = new SniperOptics();
        public SniperOptics Optics => _class == WeaponClass.LightSniper || _class == WeaponClass.HeavySniper ? _optics : null;
        public bool Automatic => _class == WeaponClass.Smg || _class == WeaponClass.Rifle;
        public bool CanBuy(Side side) => _class!=WeaponClass.Knife && (_availability == Availability.Both ||
            _availability == (side == Side.Attack ? Availability.Attack : Availability.Defend));
        public bool IsSteady(float speed, bool grounded) => grounded && speed <= _runSpeed * 0.34f;

        public int DamageAt(float meters, HitZone zone, bool vest, bool helmet) =>
            DamageAt(meters, zone, zone==HitZone.Head ? helmet : vest);

        public int DamageAt(float meters, HitZone zone, bool armored)
        {
            double multiplier = zone == HitZone.Head ? 4d : zone == HitZone.Abdomen ? 1.25d :
                zone == HitZone.Legs ? 0.75d : 1d;
            double armor = armored && zone != HitZone.Legs ? _penetration : 1d;
            return (int)Math.Floor(_damage * Math.Pow(_falloff, Math.Max(0f, meters) / 12.7d) * multiplier * armor);
        }
    }
}
