using System;
using UnityEngine;

namespace AnimeCS.Content
{
    [Serializable]
    public struct MapRegion
    {
        [InspectorName("地名")] public string Name;
        [InspectorName("中心")] public Vector3 Center;
    }

    public sealed class MapDefinition : ContentDefinition
    {
        [SerializeField, InspectorName("地图模型 / 预制体")] private GameObject _geometry;
        [SerializeField, InspectorName("雷达平面图（北向上）")] private Texture2D _radar;
        [SerializeField, InspectorName("雷达中心 X/Z")] private Vector2 _radarCenter;
        [SerializeField, InspectorName("雷达宽深（米）")] private Vector2 _radarExtent = new Vector2(120,120);
        // A scene is stored as a stable Editor reference and a player-readable build path.
        [SerializeField, HideInInspector] private UnityEngine.Object _scene;
        [SerializeField, HideInInspector] private string _scenePath;
        [SerializeField, InspectorName("攻方出生位（5 个）")] private Vector3[] _attackSpawns = SpawnLine(-42);
        [SerializeField, InspectorName("守方出生位（5 个）")] private Vector3[] _defendSpawns = SpawnLine(43);
        [SerializeField, InspectorName("展台 A 中心")] private Vector3 _siteA = new Vector3(-25, 0, 10);
        [SerializeField, InspectorName("录音棚 B 中心")] private Vector3 _siteB = new Vector3(25, 0, 10);
        [SerializeField, Min(0.5f), InspectorName("安装区半径")] private float _siteRadius = 4;
        [SerializeField, InspectorName("区域与报点")] private MapRegion[] _regions = Array.Empty<MapRegion>();
        [SerializeField, InspectorName("攻方至 A 途经点")] private Vector3[] _attackRouteA = { new Vector3(-25, 0, -31) };
        [SerializeField, InspectorName("攻方至 B 途经点")] private Vector3[] _attackRouteB = { new Vector3(25, 0, -31) };
        [SerializeField, InspectorName("守方至 A 途经点")] private Vector3[] _defendRouteA = { new Vector3(-25, 0, 33) };
        [SerializeField, InspectorName("守方至 B 途经点")] private Vector3[] _defendRouteB = { new Vector3(25, 0, 33) };
        public System.Collections.Generic.IReadOnlyList<Vector3> Route(Side side, Site site) => side == Side.Attack
            ? (site == Site.A ? _attackRouteA : _attackRouteB) : (site == Site.A ? _defendRouteA : _defendRouteB);
        [SerializeField, InspectorName("练枪地图")] private bool _practice;
        public bool Practice => _practice;
        public override ContentKind Kind => ContentKind.Map;
        [SerializeField, InspectorName("可选的 AI 导航网格")] private UnityEngine.AI.NavMeshData _navigation;
        public UnityEngine.AI.NavMeshData Navigation => _navigation;
        public GameObject Geometry => _geometry;
        public Texture2D Radar => _radar;
        [SerializeField, InspectorName("雷达北向旋转")] private float _radarYaw;
        public float RadarYaw => _radarYaw;
        public Vector2 RadarPoint(Vector3 world)
        {
            var local=Quaternion.Euler(0,_radarYaw,0)*new Vector3(world.x-_radarCenter.x,0,world.z-_radarCenter.y);
            return new Vector2(.5f+local.x/Mathf.Max(1,_radarExtent.x),.5f-local.z/Mathf.Max(1,_radarExtent.y));
        }
        public string ScenePath => _scenePath;
        [SerializeField,HideInInspector] private string _lightingScenePath;
        [SerializeField,InspectorName("对局调色")] private UnityEngine.Rendering.VolumeProfile _combatGrade;
        public UnityEngine.Rendering.VolumeProfile CombatGrade=>_combatGrade;
        public string PlayScenePath => string.IsNullOrEmpty(_scenePath)?_lightingScenePath:_scenePath;
        public Vector3 SitePosition(Site site) => site == Site.B ? _siteB : _siteA;
        public float Radius => _siteRadius;
        [SerializeField, Min(1), InspectorName("出生购买区半径")] private float _buyRadius = 5;
        public bool InBuyZone(Side side, Vector3 position)
        {
            var spawns = side == Side.Attack ? _attackSpawns : _defendSpawns;
            foreach (var spawn in spawns)
            {
                var delta = position - spawn;
                if (Mathf.Abs(delta.y) < 2.4f && delta.x * delta.x + delta.z * delta.z <= _buyRadius * _buyRadius) return true;
            }
            return false;
        }
        public Vector3 Spawn(Side side, int slot) => (side == Side.Attack ? _attackSpawns : _defendSpawns)[slot];
        [SerializeField] private float[] _attackYaw, _defendYaw;
        public float SpawnYaw(Side side, int index)
        {
            var angles=side==Side.Attack ? _attackYaw : _defendYaw;
            return angles!=null && index<angles.Length ? angles[index] : side==Side.Attack ? 0 : 180;
        }
        public int SpawnCount(Side side) => (side == Side.Attack ? _attackSpawns : _defendSpawns)?.Length ?? 0;
        private static Vector3[] SpawnLine(float z)
        {
            var result = new Vector3[5];
            for (int i = 0; i < result.Length; i++) result[i] = new Vector3((i - 2) * 2.5f, 0.1f, z);
            return result;
        }
        public string NearestRegion(Vector3 position)
        {
            float nearest = float.MaxValue;
            string region = "";
            foreach (var entry in _regions)
            {
                float distance = (entry.Center - position).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                region = entry.Name;
            }
            return region;
        }
    }
}
