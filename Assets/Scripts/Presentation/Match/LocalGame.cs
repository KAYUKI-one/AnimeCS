using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AnimeCS.Content;
using AnimeCS.Match;
using AnimeCS.Profile;
using StylizedCharacters;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace AnimeCS.Play
{
    public sealed partial class LocalGame : MonoBehaviour
    {
        [SerializeField, InspectorName("资源目录")] private ContentCatalog _catalog;
        [SerializeField, InspectorName("比赛规则")] private MatchRules _rules;
        [SerializeField, InspectorName("玩家输入")] private InputActionAsset _actions;
        [SerializeField, InspectorName("比赛相机")] private Camera _camera;
        [SerializeField, InspectorName("比赛面板")] private UIDocument _document;
        [SerializeField, InspectorName("界面布局与交火效果")] private MatchPresentation _presentation;
        [SerializeField, InspectorName("目标包模型")] private GameObject _objectiveModel;
        [SerializeField, InspectorName("比赛提示音")] private MatchAudioClips _audioClips = new MatchAudioClips();
        [SerializeField, InspectorName("首发阵营")] private FirstSide _firstSide = FirstSide.Attack;
        [SerializeField, InspectorName("比赛种子")] private int _seed = 1979;
        private ContentCatalog _runtimeCatalog;
        private readonly ArenaPawn[] _actors = new ArenaPawn[LocalMatch.Seats];
        private readonly AppearancePresenter[] _avatars = new AppearancePresenter[LocalMatch.Seats];
        private readonly ISeatPort[] _ports = new ISeatPort[LocalMatch.Seats];
        private readonly SeatView[] _hudSeats = new SeatView[LocalMatch.Seats];
        private readonly ISeatDriver[] _drivers = new ISeatDriver[LocalMatch.Seats];
        private readonly Dictionary<GroundPickup, GameObject> _drops = new();
        private readonly HashSet<GroundPickup> _presentDrops = new();
        private readonly List<GroundPickup> _retired = new();
        private ArenaWorld _world;
        private KeyboardSeat _keyboard;
        private MatchHud _hud;
        private TeamNameplates _teamIds;
        private readonly TeamNameplates.Entry[] _teamTags=new TeamNameplates.Entry[LocalMatch.TeamSize];
        private LocalMatch _match;
        private int _cameraMask;
        private float _matchFov;
        private CharacterCamera _characterCamera;
        public bool ThirdPerson => _characterCamera?.ThirdPerson == true;
        private SeatDatagramHost _remote;
        private int _remoteSeat=MatchFormat.TeamSize;
        private bool _discardFrameTime;
        public System.Net.IPEndPoint OpenSeatLink(System.Net.IPEndPoint endpoint,int seat=MatchFormat.TeamSize)
        {
            if(seat<1 || seat>=LocalMatch.Seats)throw new ArgumentOutOfRangeException(nameof(seat));
            var socket=new SeatDatagramHost(endpoint);_remote?.Dispose();_remote=socket;_remoteSeat=seat;
            return _remote.LocalEndpoint;
        }
        private double _time;
        private Scene _loadedMap;
        private GameObject _mapRoot;
        private UnityEngine.AI.NavMeshDataInstance _navigation;
        private bool _changingMap, _queuedStart, _preparing;
        private Coroutine _entry;
        private MapDefinition _requestedMap, _lastBombMap;
        public MapDefinition SelectedMap => _requestedMap ? _requestedMap : _catalog.Map;
        public MatchMode Mode => SelectedMap.Practice ? MatchMode.AimPractice : _onlineRequested || _remote!=null ? MatchMode.Online : MatchMode.BotMatch;
        internal MapDefinition LastBombMap => _lastBombMap;
        private GameObject _objective;
        private readonly List<GameObject> _kitModels = new List<GameObject>();
        private bool _focused = true;
        private LocalProfileStore _profiles;
        private SeatAudio _audio;
        private LobbyScreen _lobby;
        private LobbyPreview _preview;
        private readonly List<LobbyMember> _teammates = new List<LobbyMember>(MatchFormat.TeamSize - 1);
        public int LobbyPartyCount => _preview?.Count ?? 0;
        public string LobbyWeaponId => Account?.State != null ? DisplayWeapon(Account.State.showSkinId) : PlayerPrefs.GetString("AnimeCS.LobbyWeapon",_presentation.lobbyWeapon ? _presentation.lobbyWeapon.Id : null);
        private VisualElement _hudHost, _lobbyHost;
        private BattleLoadingScreen _loading;
        public bool InLobby { get; private set; }
        public FirstSide OpeningSide { get => _firstSide; set => _firstSide=value; }
        public float Sensitivity { get => _keyboard.Sensitivity; set { _keyboard.Sensitivity=Mathf.Clamp(value,.025f,.30f); PlayerPrefs.SetFloat("AnimeCS.Mouse",_keyboard.Sensitivity); } }
        public float Volume { get => _audio.Volume; set { _audio.Volume=Mathf.Clamp01(value); PlayerPrefs.SetFloat("AnimeCS.Volume",_audio.Volume); } }
        private const string AspectKey="AnimeCS.Video.Aspect", CuesKey="AnimeCS.Hud.FriendlyCues";
        private PlayAspect _aspect;
        private bool _friendlyCues=true;
        public bool FriendlyCues
        {
            get => _friendlyCues;
            set { _friendlyCues=value; PlayerPrefs.SetInt(CuesKey,value?1:0); }
        }
        public PlayAspect AspectMode
        {
            get => _aspect;
            set
            {
                _aspect=(PlayAspect)Mathf.Clamp((int)value,0,2);
                PlayerPrefs.SetInt(AspectKey,(int)_aspect);
                ApplyAspect();
            }
        }

        private void ApplyAspect()
        {
            if (!_camera) return;
            if(InLobby || _aspect==PlayAspect.Native)
            {
                _camera.ResetAspect();
                return;
            }
            float ratio=_aspect==PlayAspect.Stretched ? 4f/3f : 16f/9f;
            _camera.aspect=ratio;
        }

        public bool Ready => (_online?.Connected == true || _online?.Finished == true || _match != null) && !_preparing && !InLobby;
        internal InputActionAsset InputActions => _actions;
        public PublicMatchView MatchState => Ready ? (_online?.PlayView.Match ?? _match.Public) : default;
        public ISeatPort Player => _ports[_online?.View.Seat ?? 0];
        public IReadOnlyList<RoundReport> PostMatchReports =>
            Ready && _match!=null && _match.Public.Phase == RoundPhase.Finished ? _match.Reports : Array.Empty<RoundReport>();
        public WardrobeProfile ReadWardrobe() => Account?.State == null ? _profiles.Load(_catalog) : AccountWardrobe();
        public void SaveWardrobe(WardrobeProfile profile)
        { profile.Repair(_catalog); _profiles.Save(profile); if (InLobby) ShowParty(profile); }

        // A party provider supplies real teammates only. Local match seats are allocated independently.
        public void SetLobbyTeammates(IReadOnlyList<LobbyMember> members)
        {
            _teammates.Clear();
            if (members != null)
                for (int i=0;i<Mathf.Min(MatchFormat.TeamSize - 1,members.Count);i++) _teammates.Add(members[i]);
            if (InLobby) ShowParty(ReadWardrobe());
        }
        public void SetLobbyWeapon(string id)
        {
            if (Account?.State != null || !_catalog.Find<WeaponDefinition>(id)) return;
            PlayerPrefs.SetString("AnimeCS.LobbyWeapon",id);
            if (InLobby) ShowParty(ReadWardrobe());
        }
        internal void PreviewWardrobe(WardrobeLoadout wear,string gun,bool first,bool inspectItem=false,bool unarmed=false,
            WornItems? previewWorn=null, string previewSkin=null)
        {
            _preview.Show(new LobbyMember(wear.outfitId,gun,previewSkin??wear.SkinFor(gun),previewWorn??wear.worn,
                unarmed:unarmed),Array.Empty<LobbyMember>());
            _preview.SetPageFraming(inspectItem ? .55f : .50f);
            _preview.SetInspection(first);
        }
        internal void InspectItem(ContentDefinition item) => _preview.ShowItem(item);
        internal void EndInspection()
        {
            if (!InLobby || _preview==null) return;
            _preview.ClearItem(); _preview.SetPageFraming(.50f); _preview.SetInspection(false); ShowParty(ReadWardrobe());
        }
        internal void CenterPreview(float viewportX, bool showcase = false, bool equipment = false) =>
            _preview.SetPageFraming(viewportX, showcase, equipment);
        internal void RotateLobby(float angle) => _preview.Rotate(angle);
        private void ShowParty(WardrobeProfile profile)
        {
            string id = LobbyWeaponId;
            var state = Account?.State;
            var current = profile.current;
            var outfit = _catalog.Find<OutfitDefinition>(current.outfitId);
            var character = state == null ? (outfit ? outfit.Character : null) : _catalog.Find<CharacterDefinition>(state.showCharacterId);
            var worn = character ? current.WornFor(character.Faction) : current.worn;
            var self = state == null ? new LobbyMember(current.outfitId,id,current.SkinFor(id),worn) :
                DisplayMember(state.id,state.showCharacterId,state.showSkinId,worn);
            _preview.Show(self,_teammates);
        }

        public void SetSeatDisplayId(int seat,string id) => _match?.SetSeatDisplayId(seat,id);

        /// <summary>Replace a seat's input provider at the next simulation tick (local AI, playback or remote input).</summary>
        public void SetSeatDriver(int seat, ISeatDriver driver)
        {
            if (!Ready || (uint)seat >= LocalMatch.Seats) throw new InvalidOperationException("比赛未启动或席位无效。");
            _drivers[seat] = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        internal DesktopGraphics GraphicsOptions { get; private set; }

        private IEnumerator Start()
        {
            if (_catalog == null || _rules == null || _actions == null || _camera == null || _document == null ||
                _catalog.Map == null || _catalog.Character == null || _catalog.Outfit == null ||
                _catalog.StartingPistol(Side.Attack) == null || _catalog.StartingPistol(Side.Defend) == null)
                throw new InvalidOperationException("启动引用未齐全。请检查 LocalGame 与 AnimeCS 资源目录。");
            _catalog=_runtimeCatalog=Instantiate(_catalog); _lastBombMap=_catalog.Map.Practice ? null : _catalog.Map;
            GraphicsOptions=new DesktopGraphics();
            ApplyDaylight();
            InitializeMatch();
            if (GraphicsOptions.CanWarm(_presentation.lobbyShaders))
            {
                InLobby=false; _preparing=true;
                _loading.Boot();
                yield return GraphicsOptions.WarmUp(_presentation.lobbyShaders,ShowWarmup);
                _loading.style.display=DisplayStyle.None;
                _preparing=false; InLobby=true;
            }
            yield return null;
            var args=Environment.GetCommandLineArgs();int address=Array.IndexOf(args,"-seat-host");
            if(address>=0 && address+1<args.Length)
            {
                string value=args[address+1];int colon=value.LastIndexOf(':');
                if(colon>0 && System.Net.IPAddress.TryParse(value.Substring(0,colon).Trim('[',']'),out var ip) &&
                    int.TryParse(value.Substring(colon+1),out int port) && port>0 && port<=65535)
                { OpenSeatLink(new System.Net.IPEndPoint(ip,port));Restart(); }
            }
        }

        private UnityEngine.Rendering.Universal.UniversalAdditionalCameraData _baseData;
        private UnityEngine.Rendering.Volume _globalGrade;
        private Light _sceneSun, _mapSun;
        private bool _mapOwnsLighting, _probesPending;
        private void ShowWarmup(int completed,int total) => _loading.ShaderProgress(completed,total);
        private void MapLight(bool inMatch)
        {
            if(_globalGrade) _globalGrade.sharedProfile=inMatch && SelectedMap && SelectedMap.CombatGrade?SelectedMap.CombatGrade:_presentation.daylightGrade;
            bool authored=inMatch && _mapSun;
            if (_mapOwnsLighting && _loadedMap.IsValid() && _loadedMap.isLoaded)
            {
                // Unity takes the sky, ambient lighting and fog from the active additive scene.
                var visibleScene = inMatch ? _loadedMap : gameObject.scene;
                if (SceneManager.GetActiveScene() != visibleScene) SceneManager.SetActiveScene(visibleScene);
                if (authored && SelectedMap.Daylight) SelectedMap.Daylight.ApplyToActiveScene(_mapSun);
                if (inMatch && _probesPending)
                {
                    if (LightmapSettings.lightProbes) LightProbes.Tetrahedralize();
                    _probesPending = false;
                }
            }
            if (_sceneSun) _sceneSun.enabled = !authored;
            if (_mapSun) _mapSun.enabled = authored;
            RenderSettings.sun = authored ? _mapSun : _sceneSun;
        }

        private void ApplyDaylight()
        {
            _baseData=_camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if(_presentation.daylightGrade)
            {
                var grade=gameObject.AddComponent<UnityEngine.Rendering.Volume>();
                _globalGrade=grade;grade.isGlobal=true;grade.priority=10;grade.sharedProfile=_presentation.daylightGrade;
                if(_baseData)_baseData.renderPostProcessing=true;
            }
            if (!_presentation.daylightSky) return;
            RenderSettings.skybox=_presentation.daylightSky;
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=_presentation.skyLight;RenderSettings.ambientEquatorColor=_presentation.horizonLight;
            RenderSettings.ambientGroundColor=_presentation.groundLight;RenderSettings.fog=false;
            var sun=RenderSettings.sun;
            if (!sun)
            {
                foreach (var candidate in FindObjectsByType<Light>())
                {
                    if (candidate.type != LightType.Directional) continue;
                    sun = candidate;
                    break;
                }
            }
            if(sun){_sceneSun=sun;RenderSettings.sun=sun;sun.color=new Color(1,.964f,.895f);sun.intensity=_presentation.sunIntensity;sun.shadows=LightShadows.Soft;sun.shadowStrength=.78f;sun.shadowNormalBias=.25f;}
            var sky=_presentation.daylightSky;
            if(sky.HasProperty("_Tex") && sky.GetTexture("_Tex") is Cubemap cube)
            {RenderSettings.defaultReflectionMode=UnityEngine.Rendering.DefaultReflectionMode.Custom;RenderSettings.customReflectionTexture=cube;RenderSettings.reflectionIntensity=.64f;}
            _camera.clearFlags=CameraClearFlags.Skybox;_camera.farClipPlane=Mathf.Max(4500,_camera.farClipPlane);
        }

        public void SelectMap(MapDefinition map)
        {
            if (!InLobby || _preparing || !map || map==SelectedMap) return;
            _requestedMap=map;
            if (!map.Practice) _lastBombMap=map;
            _lobby.RefreshMap();
            if (!_changingMap) StartCoroutine(ChangeMap(map));
        }
        private IEnumerator ChangeMap(MapDefinition map)
        {
            _changingMap=true;
            bool replacing=_world!=null;
            _match=null;
            Array.Clear(_ports,0,_ports.Length);
            if(_navigation.valid)_navigation.Remove();
            if(_mapRoot){_mapRoot.SetActive(false);Destroy(_mapRoot);}
            if(_loadedMap.IsValid() && _loadedMap.isLoaded)
            {
                if(SceneManager.GetActiveScene()==_loadedMap)SceneManager.SetActiveScene(gameObject.scene);
                yield return SceneManager.UnloadSceneAsync(_loadedMap);
            }
            _loadedMap=default; _world?.Dispose(); _world=null; _catalog.SelectMap(map);
            if (replacing) yield return Resources.UnloadUnusedAssets();
            yield return LoadMap(map);
            _world=new ArenaWorld(_actors,map,transform,_presentation,_objectiveModel);_lobby.RefreshMap();
            _changingMap=false;
            if(_requestedMap && _requestedMap!=map) { yield return ChangeMap(_requestedMap); yield break; }
            if(_queuedStart) Restart();
        }

        private IEnumerator LoadMap(MapDefinition map)
        {
            if (map.SpawnCount(Side.Attack) != LocalMatch.TeamSize || map.SpawnCount(Side.Defend) != LocalMatch.TeamSize)
                throw new InvalidOperationException("地图需要攻守各五个出生位。");
            _mapSun=null;
            _mapOwnsLighting=false;
            _probesPending=false;
            var scenePath=map.PlayScenePath;
            if (!string.IsNullOrEmpty(scenePath))
            {
                if (!Application.CanStreamedLevelBeLoaded(scenePath))
                    throw new InvalidOperationException("请在资源管理器中将这张场景地图设为启动地图：" + scenePath);
                var operation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
                while (!operation.isDone)
                {
                    if (_queuedStart) _loading.Progress(.4f * operation.progress, "正在读取地图");
                    yield return null;
                }
                _loadedMap = SceneManager.GetSceneByPath(scenePath);
                _mapOwnsLighting=string.IsNullOrEmpty(map.ScenePath) || map.Daylight;
                foreach (var mapRoot in _loadedMap.GetRootGameObjects())
                {
                    foreach (var camera in mapRoot.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                    foreach (var listener in mapRoot.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
                    foreach (var geometry in mapRoot.GetComponentsInChildren<ImmutableMapMeshes>(true))
                        yield return geometry.ReleaseCpuCopies();
                }
            }
            else if (map.Geometry != null)
            {
                var geometry = _mapRoot = Instantiate(map.Geometry, transform);
                if (map.Daylight)
                {
                    _loadedMap = SceneManager.CreateScene(geometry.name);
                    geometry.transform.SetParent(null, true);
                    SceneManager.MoveGameObjectToScene(geometry, _loadedMap);
                    _mapOwnsLighting = true;
                }
                // Raw FBX/OBJ geometry often has no physics; authored prefabs keep their colliders.
                if (geometry.GetComponentInChildren<Collider>(true) == null)
                    foreach (var mesh in geometry.GetComponentsInChildren<MeshFilter>())
                        if (mesh.sharedMesh != null) mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
            }
            else throw new InvalidOperationException("启动地图缺少场景或几何素材。");
            if (_mapOwnsLighting)
            {
                SceneManager.SetActiveScene(_loadedMap);
                _mapSun = RenderSettings.sun;
                if (!_mapSun && map.Daylight)
                {
                    foreach (var root in _loadedMap.GetRootGameObjects())
                    {
                        _mapSun = System.Array.Find(root.GetComponentsInChildren<Light>(), l => l.type == LightType.Directional);
                        if (_mapSun) break;
                    }
                    if (!_mapSun)
                    {
                        _mapSun = new GameObject("Environment daylight").AddComponent<Light>();
                        _mapSun.type = LightType.Directional;
                        map.Daylight.PrepareSunForBake(_mapSun);
                    }
                }
                _probesPending = true;
                MapLight(false);
            }
            if(map.Navigation)_navigation=UnityEngine.AI.NavMesh.AddNavMeshData(map.Navigation);
        }

        private void InitializeMatch()
        {
            for (int i = 0; i < _actors.Length; i++)
            {
                var actor = new GameObject("Seat " + (i + 1)); actor.transform.SetParent(transform, false);
                _actors[i] = actor.AddComponent<ArenaPawn>();
                _actors[i].Tuning = _rules.movement ?? SeatMovement.Defaults;
                _avatars[i] = new AppearancePresenter(_catalog, _actors[i], _camera.transform,_presentation,_objectiveModel);
            }
            _profiles = new LocalProfileStore(Path.Combine(Application.persistentDataPath, "wardrobe-v1.json"));
            if (_objectiveModel != null)
            {
                _objective = AppearancePresenter.NewRoot("目标包", transform);
                AppearancePresenter.AddVisual(_objectiveModel, _objective.transform, Vector3.zero);
            }
            _keyboard = new KeyboardSeat(_actions);
            _audio = new SeatAudio(gameObject.AddComponent<AudioSource>(), _audioClips, _presentation);
            _cameraMask = _camera.cullingMask;
            _matchFov = _camera.fieldOfView;
            _characterCamera = new CharacterCamera(_camera);
            _aspect=(PlayAspect)Mathf.Clamp(PlayerPrefs.GetInt(AspectKey,0),0,2);
            _friendlyCues=PlayerPrefs.GetInt(CuesKey,1)!=0;
            _keyboard.Sensitivity=PlayerPrefs.GetFloat("AnimeCS.Mouse",.1f); _audio.Volume=PlayerPrefs.GetFloat("AnimeCS.Volume",1);
            var root = _document.rootVisualElement;
            root.Clear(); root.styleSheets.Add(_presentation.theme);
            var font = Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei UI","Microsoft YaHei","Noto Sans CJK SC","Arial Unicode MS"},18);
            if (font) root.style.unityFontDefinition=FontDefinition.FromFont(font);
            root.RegisterCallback<GeometryChangedEvent>(e=>root.EnableInClassList("compact",e.newRect.width<940));
            _lobbyHost=new VisualElement(); _hudHost=new VisualElement();
            _lobbyHost.style.flexGrow=1; _hudHost.style.flexGrow=1;
            root.Add(_lobbyHost); root.Add(_hudHost);
            _loading = new BattleLoadingScreen(MatchFormat.TeamSize, _presentation.battleLoadingStyle);
            root.Add(_loading);
            _hud = new MatchHud(_hudHost,_presentation,_catalog,_rules,LocalMatch.TeamSize);
            _hud.OrderRequested += _keyboard.Order;
            _hud.ResumeRequested += TogglePause;
            _hud.RestartRequested += Restart;
            _hud.LobbyRequested += ReturnToLobby;
            _hud.EndRoomRequested += EndOnlineRoom;
            _hud.TargetMotionRequested += ToggleTargetMotion;
            _teamIds = new TeamNameplates(_hudHost.Q("hud"),_teamTags.Length);
            Account = new AnimeCS.Accounts.AccountClient();
            _lobby = new LobbyScreen(_lobbyHost,_presentation.lobby,_presentation.lobbyTheme,_catalog,this);
            _lobby.LocalMatchRequested += Restart;
            _lobby.PartyMatchRequested += StartAccountBattle;
            _lobby.RenderReviewRequested += OpenRenderReview;
            _lobby.MapRequested += SelectMap;
            _lobby.OpeningSideRequested += SetOpeningSide;
            _lobby.AspectRequested += SetLobbyAspect;
            _lobby.FriendlyCuesRequested += SetLobbyFriendlyCues;
            _lobby.SensitivityRequested += SetLobbySensitivity;
            _lobby.VolumeRequested += SetLobbyVolume;
            _preview = new LobbyPreview(transform,_camera,_presentation,_catalog);
            ReturnToLobby();
        }

        private int _entrySeed;
        private void ShowLineup()
        {
            var map = SelectedMap;
            _loading.Begin(map.DisplayName, map.LoadingBrief, map.LoadingArt, map.Radar, map.Practice, _onlineRequested);
            if (_onlineRequested)
            {
                var party = Account?.State?.members;
                if (party != null)
                    for (int i = 0; i < Mathf.Min(party.Length, MatchFormat.TeamSize); i++)
                        ShowLoadingCard(Side.Defend, i, _catalog.Find<CharacterDefinition>(party[i].showCharacterId), party[i].id);
                return;
            }
            var opening=LocalMatch.OpeningFor(Mode,_firstSide);
            _entrySeed=opening==FirstSide.Random ? UnityEngine.Random.Range(1,int.MaxValue) : _seed;
            int attackers=LocalMatch.PreviewAttackTeam(opening,_entrySeed);
            var wardrobe=ReadWardrobe().current;
            for (int i=0;i<LocalMatch.Seats;i++)
            {
                var side=i/LocalMatch.TeamSize==attackers ? Side.Attack : Side.Defend;
                int slot=i%LocalMatch.TeamSize;
                var outfit=_catalog.OutfitForSide(side,i==0?wardrobe.OutfitFor(side):null,slot);
                bool empty=Mode==MatchMode.AimPractice && i>0 && i<LocalMatch.TeamSize;
                ShowLoadingCard(side,slot,!empty && outfit ? outfit.Character : null,empty ? "空席" : i==0 ? Account?.State?.id ?? "自己" : "BOT "+(i+1).ToString("00"));
            }
        }

        private void ShowLoadingOutfit(Side side, int slot, string outfitId, string label)
        {
            var outfit = _catalog.Find<OutfitDefinition>(outfitId);
            ShowLoadingCard(side, slot, outfit ? outfit.Character : null, label);
        }

        private void ShowLoadingCard(Side side, int slot, CharacterDefinition character, string label) =>
            _loading.SetCard(side, slot, character ? character.DisplayName : null, character ? character.BattlePortrait : null, label);

        public void Restart()
        {
            if (_preparing) return;
            CloseRenderReview();
            if (_online!=null) { RestartOnlineRoom(); return; }
            if (!_queuedStart) ShowLineup();
            _queuedStart=true;
            if (_changingMap) return;
            if (_world==null) { StartCoroutine(ChangeMap(SelectedMap)); return; }
            _queuedStart=false;
            _preparing=true;
            _entry=StartCoroutine(EnterMatch());
        }

        private IEnumerator EnterMatch()
        {
            GraphicsOptions.SetLobbyPacing(false);
            yield return null;
            yield return GraphicsOptions.WarmUp(_presentation.matchShaders,ShowWarmup);
            _loading.Progress(.62f,"正在准备角色");
            if (_onlineRequested) { yield return EnterOnline(); yield break; }
            var profile = ReadWardrobe();
            var mode=Mode;
            var opening=LocalMatch.OpeningFor(mode,_firstSide);
            int seed=_entrySeed;
            _match = new LocalMatch(_catalog, _rules, _world, seed, opening, profile,mode);
            for (int member=0;member<_teammates.Count;member++)
                _match.SetSeatDisplayId(member+1,_teammates[member].displayId);
            for (int i = 0; i < _ports.Length; i++)
            {
                _ports[i] = _match.BindSeat(i);
                _drivers[i] = i == 0 ? _keyboard : mode==MatchMode.AimPractice ? new PracticeTarget(_match,_catalog.Map,i) : new ObjectiveBot(_catalog, seed ^ (i * 7919),_rules.bot);
                var view = _ports[i].Read();
                ShowLoadingOutfit(view.Side,i % LocalMatch.TeamSize,view.Health>0 ? view.OutfitId : null,view.Health<=0 ? "空席" : i==0 ? Account?.State?.id ?? "自己" : "BOT "+(i+1).ToString("00"));
                _loading.Progress(.63f + .23f*(i+1)/_ports.Length, "正在准备角色");
                if (view.Health<=0 || !_avatars[i].NeedsBody(view)) continue;
                _avatars[i].Present(view,i==0);
                yield return null;
            }
            _world.WarmFeedback();
            _preview.Hide(); InLobby=false;
            _lobbyHost.style.display=DisplayStyle.None; _hudHost.style.display=DisplayStyle.Flex;
            _keyboard.SyncRound(_ports[0].Read(),true); _keyboard.ResetPointer();
            _hud.StartMatch(_match,profile.current,mode==MatchMode.AimPractice,canRestart:true);
            ApplyAspect();MapLight(true);
            _time=0;
            _loading.Progress(.93f,"正在绘制场景");
            // Submit the actual world, skin layouts, camera stack and HUD before the first playable tick.
            Draw();
            yield return null;
            yield return null;
            _loading.Progress(1,"资源就绪");
            RecordEntry(mode);
            _discardFrameTime=true;
            _preparing=false; _entry=null; _loading.style.display=DisplayStyle.None;
            UpdatePointer();
        }

        public void ReturnToLobby()
        {
            ReleaseRenderReview();
            StopOnline();
            if (_entry!=null) StopCoroutine(_entry);
            _entry=null; _queuedStart=_preparing=false; _loading.style.display=DisplayStyle.None;
            _match?.SetPaused(true); InLobby=true; _time=0; ApplyAspect();MapLight(false);
            GraphicsOptions.SetLobbyPacing(true);
            if (_objective) _objective.SetActive(false);
            foreach (var obj in _kitModels) obj.SetActive(false);
            foreach (var model in _drops.Values) AppearancePresenter.Retire(model);
            _drops.Clear();
            _audio.Stop(); _camera.cullingMask=_cameraMask;
            SetThirdPerson(false);
            if (_baseData) _baseData.renderPostProcessing=_presentation.daylightGrade;
            foreach (var avatar in _avatars) avatar.Hide();
            _lobbyHost.style.display=DisplayStyle.Flex; _hudHost.style.display=DisplayStyle.None;
            Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
            _lobby.Show();
            ShowParty(ReadWardrobe());
        }

        private void LateUpdate()
        {
            if (_preparing) return;
            if (InLobby)
            {
                if (_renderReview != null) _renderReview.Present();
                else _preview.Present();
                return;
            }
            if (!Ready) return;
            float alpha=Mathf.Clamp01((float)(_time*MatchRules.TickRate));
            if (_online!=null)
            {
                for (int i=0;i<_avatars.Length;i++) if (_shown[i]) _avatars[i].AlignWorldGun();
            }
            else
            {
                for (int i=0;i<_avatars.Length;i++) { _avatars[i].AlignWorldGun(); _world.SetMuzzle(i,_avatars[i].Muzzle()); }
                _world.RenderFeedback(alpha);
            }
            var self=Player.Read();
            var watched=self.Health>0 ? self : Player.WatchTeam();
            PresentTeamTags(self,watched,alpha);
        }

        private void PresentTeamTags(in SeatView self,in SeatView watched,float alpha)
        {
            bool active=watched.Seat>=0 && !watched.Blinded && !watched.InsideSmoke && !MenuOpen &&
                !self.Match.Paused && self.Match.Phase!=RoundPhase.Finished && _teamIds.HasLayout;
            bool assist=FriendlyCues;
            bool InFrame(Vector3 p) => p.z>=_camera.nearClipPlane && p.x>=0 && p.x<=1 && p.y>=0 && p.y<=1;
            int first=self.Seat/_teamTags.Length*_teamTags.Length;
            for(int slot=0;slot<_teamTags.Length;slot++)
            {
                int seat=first+slot;
                _teamTags[slot]=default;
                if(active && seat!=watched.Seat && (_online==null ? _actors[seat].Alive : _onlineAllyTags[seat]))
                {
                    var head=_avatars[seat].LabelAnchor(alpha);
                    var point=_camera.WorldToViewportPoint(head);
                    var chest=_camera.WorldToViewportPoint(_actors[seat].transform.position+Vector3.up*_actors[seat].Height*.57f);
                    if((InFrame(point)||InFrame(chest)) && _world.VisibleAlly(_camera.transform.position,seat,head-Vector3.up*.21f))
                        _teamTags[slot]=new TeamNameplates.Entry(_hudSeats[seat].DisplayId,point.z<_camera.nearClipPlane ? chest : point);
                }
                _avatars[seat].SetFriendlyCue(assist && _teamTags[slot].Visible);
            }
            _teamIds.Present(_teamTags,assist);
        }

        public void TogglePause()
        {
            if (!Ready || MatchState.Phase==RoundPhase.Finished) return;
            _keyboard.ResetPointer(); _time=0; _discardFrameTime=true;
            if (_online!=null)
            {
                _onlineMenu=!_onlineMenu;
                RenderOnline(); UpdatePointer();
            }
            else { _match.SetPaused(!_match.IsPaused); Draw(); }
        }

        private void Update()
        {
            if (_keyboard == null || _queuedStart || _preparing) return;
            _keyboard.ReadMenuKeys(out bool pause, out bool shop);
            if (_renderReview != null)
            {
                if (pause) CloseRenderReview();
                else _renderReview.Animate();
                return;
            }
            if (InLobby) { _preview.BeforeAnimation(); if (pause) _lobby.Back(); return; }
            if (_online!=null) { UpdateOnline(pause,shop); return; }
            if (!Ready) return;
            foreach(var avatar in _avatars) avatar.BeforeAnimation();
            var self = _ports[0].Read();
            ReadPlayerInput(self,pause,shop);
            if(_drivers[0]==_keyboard && self.CanStartShot && _keyboard.PredictPress)
            {
                var gun=_catalog.Find<WeaponDefinition>(self.WeaponId);
                _avatars[0].PredictTrigger(self.Match.Round,self.Match.Tick+1);_audio.Predict(self,gun && gun.Suppressed,gun?gun.Class:WeaponClass.Rifle);
            }
            if (_match.IsPaused || _match.Public.Phase == RoundPhase.Finished) return;
            const double fixedStep = 1d / MatchRules.TickRate;
            // Loading and focus changes can yield a frame several seconds long. Keep only the playable catch-up window.
            if (!_discardFrameTime) _time = Math.Min(8 * fixedStep, _time + Time.unscaledDeltaTime);
            _discardFrameTime = false;
            for (int steps = 0; _time >= fixedStep && steps < 8; steps++)
            {
                _time -= fixedStep;
                _remote?.Poll(_ports[_remoteSeat].Read());
                for (int i = 0; i < _drivers.Length; i++)
                {
                    var view = _ports[i].Read();
                    var driver=i==_remoteSeat && _remote!=null && _remote.Connected ? _remote.Input : _drivers[i];
                    _ports[i].Submit(view.Match.Tick + 1, driver.Decide(view));
                }
                _match.Step();
                ObserveRound(_match.Public);
                if(_remote!=null && (_match.Public.Tick&1)==0)_remote.Publish(_ports[_remoteSeat].Read());
            }
            Draw();
        }

        private bool MenuOpen => _online!=null ? _onlineMenu : _match!=null && _match.IsPaused;

        private void SetOpeningSide(FirstSide side) => OpeningSide = side;

        private void ToggleTargetMotion()
        {
            if (Ready && _match?.Mode == MatchMode.AimPractice) _match.MovingTargets = !_match.MovingTargets;
        }

        private void ReadPlayerInput(in SeatView self,bool pause,bool shop)
        {
            if (pause && _hud.ShopOpen && !MenuOpen) _hud.ToggleShop();
            else if (pause) TogglePause();
            else if (shop && !MenuOpen && self.CanBuy) _hud.ToggleShop();
            _keyboard.SyncRound(self);
            _keyboard.ReadShop(out int step,out bool confirm,out bool undo);
            _keyboard.Shopping=_hud.ShopOpen && self.CanBuy && !MenuOpen;
            if (_focused && _keyboard.Shopping) _hud.NavigateShop(step,confirm,undo,self.Match.Phase==RoundPhase.Buy);
            bool playing=_focused && !MenuOpen && self.Match.Phase!=RoundPhase.Finished;
            var mouse=Mouse.current;
            if (playing && self.Health > 0 && !_keyboard.Shopping)
            {
                if (Keyboard.current?.vKey.wasPressedThisFrame == true) SetThirdPerson(!ThirdPerson);
                if (ThirdPerson && mouse != null)
                    _characterCamera.Orbit(mouse.delta.ReadValue() * Sensitivity, OrbitWheelSteps(mouse.scroll.ReadValue().y));
            }
            if (playing && self.Health<=0 && !_keyboard.Shopping && mouse!=null)
            {
                float scroll=mouse.scroll.ReadValue().y;
                int direction=mouse.leftButton.wasPressedThisFrame || scroll<0 ? 1 :
                    mouse.rightButton.wasPressedThisFrame || scroll>0 ? -1 : 0;
                if (direction!=0) Player.WatchTeam(direction);
            }
            _keyboard.Sample(playing && self.Health>0,playing && !ThirdPerson && (!_keyboard.Shopping || _keyboard.ShopLook),Mathf.Max(1f,self.Magnification));
        }

        public void SetThirdPerson(bool enabled)
        {
            if (_characterCamera == null || enabled && (!Ready || InLobby || Player.Read().Health <= 0)) return;
            if (enabled == ThirdPerson) return;
            _characterCamera.SetThirdPerson(enabled, enabled ? Player.Read().Yaw : 0);
            _keyboard.Orbiting = enabled;
            _keyboard.ResetPointer();
        }

        private bool Orbiting(in SeatView watched) => ThirdPerson && watched.Seat >= 0 && watched.Seat == Player.Read().Seat;

        private void Draw()
        {
            var self = _ports[0].Read();
            var watched = self.Health > 0 ? self : _ports[0].WatchTeam();
            bool publicOnly = watched.Seat < 0;
            float alpha = Mathf.Clamp01((float)(_time * MatchRules.TickRate));
            bool mouseLook=watched.Seat==self.Seat && _drivers[self.Seat]==_keyboard && !MenuOpen && self.Match.Phase!=RoundPhase.Finished;
            bool orbit = Orbiting(watched);
            PositionCamera(watched,publicOnly ? default : _actors[watched.Seat].RenderEye(alpha),mouseLook,alpha);
            for (int i=0;i<_avatars.Length;i++)
            {
                _actors[i].InterpolateVisuals(alpha);
                _hudSeats[i]=_ports[i].Read();
                _avatars[i].Present(_hudSeats[i],watched.Seat==i && !orbit,alpha);
            }
            _world.SetLocalView(!publicOnly && watched.Seat==0 && _drivers[0]==_keyboard ? _actors[0].RenderEye(alpha) : (Vector3?)null);
            DrawWorld(_match,publicOnly,_match.Public.Tick+alpha);
            _hud.Present(self,watched,_hudSeats,movingTargets:_match.MovingTargets);
            UpdatePointer();
            _audio.Present(watched,_world);
        }

        private void PositionCamera(in SeatView watched,Vector3 eye,bool mouseLook,float alpha)
        {
            bool publicOnly=watched.Seat<0;
            bool orbit = Orbiting(watched);
            if (publicOnly) _camera.cullingMask = 0;
            float power = publicOnly || orbit ? 1f : Mathf.Max(1f,watched.Magnification);
            float worldFov=2f*Mathf.Atan(Mathf.Tan(_matchFov*Mathf.Deg2Rad*.5f)/power)*Mathf.Rad2Deg;
            if(!Mathf.Approximately(_camera.fieldOfView,worldFov))_camera.fieldOfView=worldFov;
            if (!publicOnly)
            {
                float elapsed=(watched.Match.Tick+alpha-watched.LastShotTick)*SeatMovement.StepTime;
                float kick=1-Mathf.Clamp01(elapsed/.12f);
                _keyboard.SetViewKick(mouseLook && _online==null && watched.Held!=HeldSlot.Knife ? _presentation.cameraKickDegrees*kick*kick/Mathf.Max(1f,watched.Magnification):0);
                _characterCamera.Present(eye, mouseLook ? _keyboard.ViewYaw : watched.Yaw,
                    mouseLook ? _keyboard.ViewPitch : watched.Pitch, _actors[watched.Seat].transform, _cameraMask, orbit);
            }
        }

        internal void DrawWorld(IMatchWorldView view,bool publicOnly,float frame)
        {
            DrawDroppedGuns(publicOnly ? Array.Empty<GroundPickup>() : view.GroundWeapons,frame);
            if (_objective != null)
            {
                var point = publicOnly ? null : view.ObjectiveAt(frame);
                _objective.SetActive(point.HasValue);
                if (point.HasValue) _objective.transform.position = point.Value;
            }
            DrawEquipment(publicOnly ? Array.Empty<Vector3>() : view.GroundKits);
        }

        private void UpdatePointer()
        {
            bool capture = _focused && Ready && !MenuOpen && MatchState.Phase!=RoundPhase.Finished &&
                (!_hud.ShopOpen || _keyboard.ShopLook);
            var mode = capture ? CursorLockMode.Locked : CursorLockMode.None;
            if (Cursor.lockState!=mode) Cursor.lockState=mode;
            Cursor.visible=!capture;
        }

        private void OnApplicationFocus(bool focused)
        {
            _focused = focused; _discardFrameTime=true;
            if (!focused) _lobby?.CancelPointerInteraction();
            _keyboard?.ResetPointer();
            UpdatePointer();
        }

        private void DrawEquipment(IReadOnlyList<Vector3> kits)
        {
            for (int i = 0; i < kits.Count; i++)
            {
                if (_kitModels.Count == i)
                {
                    var kit = AppearancePresenter.NewRoot("拆除套件", transform);
                    AppearancePresenter.AddVisual(_presentation.recoveryKit,kit.transform,Vector3.zero);
                    _kitModels.Add(kit);
                }
                _kitModels[i].transform.position = kits[i];
                _kitModels[i].SetActive(true);
            }
            for (int i = kits.Count; i < _kitModels.Count; i++) _kitModels[i].SetActive(false);
        }

        private void DrawDroppedGuns(IReadOnlyList<GroundPickup> items,float frame)
        {
            _presentDrops.Clear();
            for(int i=0;i<items.Count;i++)
            {
                var pickup=items[i];
                _presentDrops.Add(pickup);
                var position=pickup.At(frame);
                if (_drops.TryGetValue(pickup,out var existing)) { if(existing.transform.position!=position) existing.transform.position=position; continue; }
                var root = AppearancePresenter.NewRoot("Dropped " + pickup.Item.DisplayName, transform);
                root.transform.SetPositionAndRotation(position, pickup.Rest.rotation);
                var model = AppearancePresenter.AddVisual(pickup.Model, root.transform, Vector3.zero);
                if(pickup.Weapon!=null) AppearancePresenter.ApplySkin(model, pickup.Weapon.Definition, _catalog.Find<WeaponSkinDefinition>(pickup.Weapon.SkinId));
                root.SetActive(true); _drops.Add(pickup, root);
            }
            if (_drops.Count==_presentDrops.Count) return;
            _retired.Clear();
            foreach(var key in _drops.Keys) if(!_presentDrops.Contains(key)) _retired.Add(key);
            foreach (var key in _retired) { AppearancePresenter.Retire(_drops[key]); _drops.Remove(key); }
        }

        private void OnDestroy()
        {
            ReleaseRenderReview();
            if (_lobby != null)
            {
                _lobby.CancelPointerInteraction();
                _lobby.LocalMatchRequested -= Restart;
                _lobby.PartyMatchRequested -= StartAccountBattle;
                _lobby.RenderReviewRequested -= OpenRenderReview;
                _lobby.MapRequested -= SelectMap;
                _lobby.OpeningSideRequested -= SetOpeningSide;
                _lobby.AspectRequested -= SetLobbyAspect;
                _lobby.FriendlyCuesRequested -= SetLobbyFriendlyCues;
                _lobby.SensitivityRequested -= SetLobbySensitivity;
                _lobby.VolumeRequested -= SetLobbyVolume;
            }
            if (_hud != null)
            {
                _hud.OrderRequested -= _keyboard.Order;
                _hud.ResumeRequested -= TogglePause;
                _hud.RestartRequested -= Restart;
                _hud.LobbyRequested -= ReturnToLobby;
                _hud.EndRoomRequested -= EndOnlineRoom;
                _hud.TargetMotionRequested -= ToggleTargetMotion;
            }
            Account?.Dispose();
            if(_navigation.valid)_navigation.Remove();
            if (_runtimeCatalog) Destroy(_runtimeCatalog);
            _online?.Dispose();
            _remote?.Dispose();
            _world?.Dispose();
            GraphicsOptions?.Dispose();
            _keyboard?.Dispose();
            _audio?.Dispose();
            foreach (var avatar in _avatars) avatar?.Release();
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            if (_loadedMap.IsValid() && _loadedMap.isLoaded && SceneManager.sceneCount > 1)
                SceneManager.UnloadSceneAsync(_loadedMap);
        }
    }
}
