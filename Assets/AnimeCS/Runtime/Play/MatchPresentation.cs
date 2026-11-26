using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    [CreateAssetMenu(menuName = "AnimeCS/界面与交火反馈")]
    public sealed class MatchPresentation : ScriptableObject
    {
        [Header("UI Toolkit 布局")]
        public VisualTreeAsset lobby, hud;
        public StyleSheet theme;
        [Header("队伍大厅")]
        public GameObject lobbySet;
        public AnimeCS.Content.WeaponDefinition lobbyWeapon;
        [Range(20f,55f)] public float lobbyFieldOfView = 29f;
        [Header("日光环境")] public Material daylightSky;
        public UnityEngine.Rendering.VolumeProfile daylightGrade;
        [ColorUsage(false)] public Color skyLight = new Color(.76f,.81f,.86f), horizonLight=new Color(.53f,.56f,.60f), groundLight=new Color(.35f,.32f,.29f);
        [Range(.2f,3)] public float sunIntensity=1.36f;
        [Header("交火反馈")]
        [Range(0,1)] public float handlingMotion = 1;
        public bool hitConfirm = true;
        [InspectorName("第一人称长枪构图偏移（米）")] public Vector3 longGunViewOffset=new(.023f,-.034f,0);
        [Range(0,2)] public float cameraKickDegrees=.58f;
        public bool tracers = true, surfaceMarks = true, hitReaction = true;
        public Material tracerMaterial, impactMaterial;
        [InspectorName("枪口火光材质")] public Material muzzleMaterial;
        public AudioClip shotSound, suppressedSound, pistolSound, marksmanSound, alternateRifleSound, deviceBurstSound;
        public AudioClip[] bootSteps;
        public Material deviceBlast;
        public GameObject recoveryKit;
        [Range(.015f,.18f)] public float tracerSeconds = .055f;
        [Range(.003f,.09f)] public float tracerWidth = .025f;
        [Range(.03f,.4f)] public float markSize = .12f;
        [Range(1,160)] public int maxMarks = 64;
        [Range(.06f,.50f)] public float flinchSeconds = .22f;
        [Range(0,20)] public float flinchDegrees = 9;
        [Range(0,12)] public float shotLiftDegrees = 4.2f;
        [Range(.05f,.4f)] public float shotReturnSeconds = .16f;
    }
}
