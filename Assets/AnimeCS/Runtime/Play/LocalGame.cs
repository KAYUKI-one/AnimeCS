using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AnimeCS.Content;
using AnimeCS.Match;
using AnimeCS.Profile;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace AnimeCS.Play
{
    public sealed class LocalGame : MonoBehaviour
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
        private readonly ArenaPawn[] _actors = new ArenaPawn[LocalMatch.Seats];
        private readonly AppearancePresenter[] _avatars = new AppearancePresenter[LocalMatch.Seats];
        private readonly ISeatPort[] _ports = new ISeatPort[LocalMatch.Seats];
        private readonly ISeatDriver[] _drivers = new ISeatDriver[LocalMatch.Seats];
        private readonly Dictionary<GroundPickup, GameObject> _drops = new();
        private readonly List<GroundPickup> _retired = new();
        private ArenaWorld _world;
        private KeyboardSeat _keyboard;
        private MatchHud _hud;
        private TeamNameplates _teamIds;
        private LocalMatch _match;
        private int _cameraMask;
        private float _matchFov;
        private Camera _viewCamera;
        private SeatDatagramHost _remote;
        private int _remoteSeat=5;
        private bool _discardFrameTime;
        public System.Net.IPEndPoint OpenSeatLink(System.Net.IPEndPoint endpoint,int seat=5)
        {
            if(seat<1 || seat>=LocalMatch.Seats)throw new ArgumentOutOfRangeException(nameof(seat));
            var socket=new SeatDatagramHost(endpoint);_remote?.Dispose();_remote=socket;_remoteSeat=seat;
            return _remote.LocalEndpoint;
        }
        private double _time;
        private Scene _loadedMap;
        private GameObject _mapRoot;
        private UnityEngine.AI.NavMeshDataInstance _navigation;
        private bool _changingMap, _queuedStart;
        private MapDefinition _requestedMap, _lastBombMap;
        public MapDefinition SelectedMap => _requestedMap ? _requestedMap : _catalog.Map;
        public MatchMode Mode => SelectedMap.Practice ? MatchMode.AimPractice : _remote!=null ? MatchMode.Online : MatchMode.BotMatch;
        internal MapDefinition LastBombMap => _lastBombMap;
        private GameObject _objective;
        private readonly List<GameObject> _kitModels = new List<GameObject>();
        private bool _focused = true;
        private LocalProfileStore _profiles;
        private SeatAudio _audio;
        private LobbyScreen _lobby;
        private LobbyPreview _preview;
        private readonly List<LobbyMember> _teammates = new List<LobbyMember>(4);
        public int LobbyPartyCount => _preview?.Count ?? 0;
        public string LobbyWeaponId => PlayerPrefs.GetString("AnimeCS.LobbyWeapon",_presentation.lobbyWeapon ? _presentation.lobbyWeapon.Id : null);
        private VisualElement _hudHost, _lobbyHost;
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
                if(_viewCamera) _viewCamera.ResetAspect();
                return;
            }
            float ratio=_aspect==PlayAspect.Stretched ? 4f/3f : 16f/9f;
            _camera.aspect=ratio;
            if(_viewCamera) _viewCamera.aspect=ratio;
        }

        public bool Ready => _match != null;
        internal InputActionAsset InputActions => _actions;
        public PublicMatchView MatchState => Ready ? _match.Public : default;
        public ISeatPort Player => _ports[0];
        public IReadOnlyList<RoundReport> PostMatchReports =>
            Ready && _match.Public.Phase == RoundPhase.Finished ? _match.Reports : Array.Empty<RoundReport>();
        public WardrobeProfile ReadWardrobe() => _profiles.Load(_catalog);
        public void SaveWardrobe(WardrobeProfile profile)
        { profile.Repair(_catalog); _profiles.Save(profile); if (InLobby) ShowParty(profile); }

        // A party provider supplies real teammates only. Local match seats are allocated independently.
        public void SetLobbyTeammates(IReadOnlyList<LobbyMember> members)
        {
            _teammates.Clear();
            if (members != null)
                for (int i=0;i<Mathf.Min(4,members.Count);i++) _teammates.Add(members[i]);
            if (InLobby) ShowParty(ReadWardrobe());
        }
        public void SetLobbyWeapon(string id)
        {
            if (!_catalog.Find<WeaponDefinition>(id)) return;
            PlayerPrefs.SetString("AnimeCS.LobbyWeapon",id);
            if (InLobby) ShowParty(ReadWardrobe());
        }
        internal void PreviewWardrobe(WardrobeLoadout wear,string gun,bool first,bool inspectItem=false)
        {
            _preview.Show(new LobbyMember(wear.outfitId,gun,wear.SkinFor(gun),wear.worn),Array.Empty<LobbyMember>());
            _preview.SetPageFraming(inspectItem ? .55f : .50f);
            _preview.SetInspection(first);
        }
        internal void InspectItem(ContentDefinition item) => _preview.ShowItem(item);
        internal void EndInspection()
        {
            if (!InLobby || _preview==null) return;
            _preview.ClearItem(); _preview.SetPageFraming(.59f); _preview.SetInspection(false); ShowParty(ReadWardrobe());
        }
        internal void CenterPreview(float viewportX) => _preview.SetPageFraming(viewportX);
        internal void RotateLobby(float angle) => _preview.Rotate(angle);
        private void ShowParty(WardrobeProfile profile)
        {
            string id = LobbyWeaponId;
            _preview.Show(new LobbyMember(profile.current.outfitId,id,profile.current.SkinFor(id),profile.current.worn),_teammates);
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
            _catalog=Instantiate(_catalog); _lastBombMap=_catalog.Map.Practice ? null : _catalog.Map;
            GraphicsOptions=new DesktopGraphics();
            ApplyDaylight();
            yield return LoadMap(_catalog.Map);
            InitializeMatch();
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
        private Quaternion _menuLightRotation;
        private void MapLight(bool inMatch)
        {
            if(_globalGrade) _globalGrade.sharedProfile=inMatch && SelectedMap && SelectedMap.CombatGrade?SelectedMap.CombatGrade:_presentation.daylightGrade;
            if(!_sceneSun)return;
            bool authored=inMatch && _mapSun;
            _sceneSun.transform.rotation=authored?_mapSun.transform.rotation:_menuLightRotation;
            _sceneSun.color=authored?_mapSun.color:new Color(1,.964f,.895f);
            _sceneSun.intensity=authored?_mapSun.intensity:_presentation.sunIntensity;
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
            if(sun){_sceneSun=sun;_menuLightRotation=sun.transform.rotation;RenderSettings.sun=sun;sun.color=new Color(1,.964f,.895f);sun.intensity=_presentation.sunIntensity;sun.shadows=LightShadows.Soft;sun.shadowStrength=.78f;sun.shadowNormalBias=.25f;}
            var sky=_presentation.daylightSky;
            if(sky.HasProperty("_Tex") && sky.GetTexture("_Tex") is Cubemap cube)
            {RenderSettings.defaultReflectionMode=UnityEngine.Rendering.DefaultReflectionMode.Custom;RenderSettings.customReflectionTexture=cube;RenderSettings.reflectionIntensity=.64f;}
            _camera.clearFlags=CameraClearFlags.Skybox;_camera.farClipPlane=Mathf.Max(4500,_camera.farClipPlane);
        }

        public void SelectMap(MapDefinition map)
        {
            if (!InLobby || !map || map==SelectedMap) return;
            _requestedMap=map;
            if (!map.Practice) _lastBombMap=map;
            _lobby.RefreshMap();
            if (!_changingMap) StartCoroutine(ChangeMap(map));
        }
        private IEnumerator ChangeMap(MapDefinition map)
        {
            _changingMap=true;
            if(_navigation.valid)_navigation.Remove();
            if(_mapRoot){_mapRoot.SetActive(false);Destroy(_mapRoot);}
            if(_loadedMap.IsValid() && _loadedMap.isLoaded)
            {
                if(SceneManager.GetActiveScene()==_loadedMap)SceneManager.SetActiveScene(gameObject.scene);
                yield return SceneManager.UnloadSceneAsync(_loadedMap);
            }
            _loadedMap=default;_world.Dispose();_catalog.SelectMap(map);
            yield return LoadMap(map);
            _world=new ArenaWorld(_actors,map,transform,_presentation,_objectiveModel);_lobby.RefreshMap();
            _changingMap=false;
            if(_requestedMap && _requestedMap!=map) { yield return ChangeMap(_requestedMap); yield break; }
            if(_queuedStart){_queuedStart=false;Restart();}
        }

        private IEnumerator LoadMap(MapDefinition map)
        {
            if (map.SpawnCount(Side.Attack) != LocalMatch.TeamSize || map.SpawnCount(Side.Defend) != LocalMatch.TeamSize)
                throw new InvalidOperationException("地图需要攻守各五个出生位。");
            _mapSun=null;
            var scenePath=map.PlayScenePath;
            if (!string.IsNullOrEmpty(scenePath))
            {
                if (!Application.CanStreamedLevelBeLoaded(scenePath))
                    throw new InvalidOperationException("请在资源管理器中将这张场景地图设为启动地图：" + scenePath);
                yield return SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
                _loadedMap = SceneManager.GetSceneByPath(scenePath);
                bool baked=string.IsNullOrEmpty(map.ScenePath);
                var sceneSun=RenderSettings.sun;
                foreach (var mapRoot in _loadedMap.GetRootGameObjects())
                {
                    if (baked && mapRoot.name=="Bake sunlight" && mapRoot.TryGetComponent<Light>(out var bakedSun)) { _mapSun=bakedSun;bakedSun.enabled=false; }
                    foreach (var camera in mapRoot.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                    foreach (var listener in mapRoot.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
                }
                if(baked)
                {
                    SceneManager.SetActiveScene(_loadedMap);
                    RenderSettings.sun=sceneSun;
                    LightProbes.Tetrahedralize();
                }
            }
            else if (map.Geometry != null)
            {
                var geometry = _mapRoot = Instantiate(map.Geometry, transform);
                // Raw FBX/OBJ geometry often has no physics; authored prefabs keep their colliders.
                if (geometry.GetComponentInChildren<Collider>(true) == null)
                    foreach (var mesh in geometry.GetComponentsInChildren<MeshFilter>())
                        if (mesh.sharedMesh != null) mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
            }
            else throw new InvalidOperationException("启动地图缺少场景或几何素材。");
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
            _world = new ArenaWorld(_actors, _catalog.Map, transform,_presentation,_objectiveModel);
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
            CreateViewCamera();
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
            _hud = new MatchHud(_hudHost,_presentation,_catalog,_rules,_keyboard.Order,TogglePause,Restart,ReturnToLobby);
            _teamIds = new TeamNameplates(_hudHost.Q("hud"));
            _lobby = new LobbyScreen(_lobbyHost,_presentation,_catalog,this);
            _preview = new LobbyPreview(transform,_camera,_presentation,_catalog);
            ReturnToLobby();
        }

        private void CreateViewCamera()
        {
            var layer = LayerMask.NameToLayer("First Person");
            if (layer < 0) return;
            var go = new GameObject("第一人称相机");
            go.transform.SetParent(_camera.transform,false); _viewCamera = go.AddComponent<Camera>();
            _viewCamera.CopyFrom(_camera); _viewCamera.nearClipPlane = .015f; _viewCamera.farClipPlane = 4f;
            _viewCamera.cullingMask = 1 << layer; _viewCamera.useOcclusionCulling = false;
            var overlay = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            overlay.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay;
            overlay.renderShadows = false; overlay.renderPostProcessing = false;
            overlay.requiresDepthOption=UnityEngine.Rendering.Universal.CameraOverrideOption.Off;
            overlay.requiresColorOption=UnityEngine.Rendering.Universal.CameraOverrideOption.Off;
            var basis = _camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (basis && basis.cameraStack != null) basis.cameraStack.Add(_viewCamera);
            _viewCamera.enabled = false;
        }

        public void Restart()
        {
            if(_changingMap){_queuedStart=true;return;}
            _preview.Hide(); InLobby=false; _lobbyHost.style.display=DisplayStyle.None; _hudHost.style.display=DisplayStyle.Flex;
            var profile = ReadWardrobe();
            var mode=Mode;
            var opening=LocalMatch.OpeningFor(mode,_firstSide);
            int seed=opening==FirstSide.Random ? UnityEngine.Random.Range(1,int.MaxValue) : _seed;
            _match = new LocalMatch(_catalog, _rules, _world, seed, opening, profile,mode);
            for (int member=0;member<_teammates.Count;member++)
                _match.SetSeatDisplayId(member+1,_teammates[member].displayId);
            for (int i = 0; i < _ports.Length; i++)
            {
                _ports[i] = _match.BindSeat(i);
                _drivers[i] = i == 0 ? _keyboard : mode==MatchMode.AimPractice ? new PracticeTarget(_match,_catalog.Map,i) : new ObjectiveBot(_catalog, seed ^ (i * 7919));
            }
            _keyboard.SyncRound(_ports[0].Read(),true); _keyboard.ResetPointer();
            _hud.StartMatch(_match,profile.current);
            ApplyAspect();MapLight(true);
            _time = 0; _discardFrameTime = true; Draw();
        }

        public void ReturnToLobby()
        {
            _match?.SetPaused(true); InLobby=true; _time=0; ApplyAspect();MapLight(false);
            if (_objective) _objective.SetActive(false);
            foreach (var obj in _kitModels) obj.SetActive(false);
            _audio.Stop(); _camera.cullingMask=_cameraMask;
            if (_viewCamera) _viewCamera.enabled=false;
            if (_baseData) _baseData.renderPostProcessing=_presentation.daylightGrade;
            foreach (var avatar in _avatars) avatar.HideView();
            _lobbyHost.style.display=DisplayStyle.Flex; _hudHost.style.display=DisplayStyle.None;
            Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
            _lobby.Show();
            ShowParty(ReadWardrobe());
        }

        private void LateUpdate()
        {
            if (InLobby) { _preview.Present(); return; }
            if (!Ready) return;
            for (int i=0;i<_avatars.Length;i++) { _avatars[i].AlignWorldGun(); _world.SetMuzzle(i,_avatars[i].Muzzle()); }
            var self=_ports[0].Read();var watched=self.Health>0?self:_ports[0].WatchTeam();
            _world.RenderFeedback(Mathf.Clamp01((float)(_time*MatchRules.TickRate)));
            _teamIds.Present(self,watched,_camera,_ports,_actors,_avatars,_world,FriendlyCues,Mathf.Clamp01((float)(_time*MatchRules.TickRate)));
        }

        public void TogglePause()
        {
            if (!Ready || _match.Public.Phase == RoundPhase.Finished) return;
            _keyboard.ResetPointer(); _match.SetPaused(!_match.IsPaused); _time = 0; _discardFrameTime=true; Draw();
        }

        private void Update()
        {
            if (_keyboard == null) return;
            _keyboard.ReadMenuKeys(out bool pause, out bool shop);
            if (InLobby) { if (pause) _lobby.Back(); return; }
            if (!Ready) return;
            foreach(var avatar in _avatars) avatar.BeforeAnimation();
            if (pause && _hud.ShopOpen && !_match.IsPaused) _hud.ToggleShop();
            else if (pause) TogglePause();
            var self = _ports[0].Read();
            _keyboard.SyncRound(self);
            if (shop && !_match.IsPaused && self.CanBuy) _hud.ToggleShop();
            _keyboard.ReadShop(out int step, out bool confirm, out bool cancel);
            _keyboard.Shopping = _hud.ShopOpen && self.CanBuy && !_match.IsPaused;
            if (_keyboard.Shopping) _hud.NavigateShop(step, confirm, cancel, self.Match.Phase == RoundPhase.Buy);
            bool playing = !_match.IsPaused && self.Match.Phase != RoundPhase.Finished;
            var mouse=Mouse.current;
            if(playing && self.Health<=0 && !_keyboard.Shopping && mouse!=null)
            {
                float wheel=mouse.scroll.ReadValue().y;
                int direction=mouse.leftButton.wasPressedThisFrame || wheel<0 ? 1 :
                    mouse.rightButton.wasPressedThisFrame || wheel>0 ? -1 : 0;
                if(direction!=0)_ports[0].WatchTeam(direction);
            }
            bool look=playing && _focused && (!_keyboard.Shopping || _keyboard.ShopLook);
            _keyboard.Sample(playing && self.Health > 0 && _focused,look,self.Magnification);
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
                if(_remote!=null && (_match.Public.Tick&1)==0)_remote.Publish(_ports[_remoteSeat].Read());
            }
            Draw();
        }

        private void Draw()
        {
            var self = _ports[0].Read();
            var watched = self.Health > 0 ? self : _ports[0].WatchTeam();
            bool publicOnly = watched.Seat < 0;
            float alpha = Mathf.Clamp01((float)(_time * MatchRules.TickRate));
            _camera.cullingMask = publicOnly ? 0 : _viewCamera ? _cameraMask & ~_viewCamera.cullingMask : _cameraMask;
            float power = publicOnly ? 1f : Mathf.Max(1f,watched.Magnification);
            float worldFov=2f*Mathf.Atan(Mathf.Tan(_matchFov*Mathf.Deg2Rad*.5f)/power)*Mathf.Rad2Deg;
            if(!Mathf.Approximately(_camera.fieldOfView,worldFov))_camera.fieldOfView=worldFov;
            if(_viewCamera)
            {
                bool drawHands=!publicOnly && watched.ScopeLevel==0;
                if(_viewCamera.enabled!=drawHands)_viewCamera.enabled=drawHands;
                if(drawHands && !Mathf.Approximately(_viewCamera.fieldOfView,_matchFov))_viewCamera.fieldOfView=_matchFov;
            }
            if (!publicOnly)
            {
                var actor = _actors[watched.Seat];
                bool mouseLook = watched.Seat == 0 && _drivers[0] == _keyboard && !self.Match.Paused && self.Match.Phase != RoundPhase.Finished;
                float elapsed=(watched.Match.Tick+alpha-watched.LastShotTick)*SeatMovement.StepTime;
                float kick=1-Mathf.Clamp01(elapsed/.12f);
                _keyboard.SetViewKick(mouseLook && watched.Held!=HeldSlot.Knife ? _presentation.cameraKickDegrees*kick*kick/Mathf.Max(1f,watched.Magnification):0);
                _camera.transform.SetPositionAndRotation(actor.RenderEye(alpha), Quaternion.Euler(
                    mouseLook ? -_keyboard.ViewPitch : -watched.Pitch, mouseLook ? _keyboard.ViewYaw : watched.Yaw, 0));
            }
            for (int i = 0; i < _avatars.Length; i++)
            {
                _actors[i].InterpolateVisuals(alpha);
                _avatars[i].Present(_ports[i].Read(), watched.Seat == i, alpha);
            }
            _world.SetLocalView(!publicOnly && watched.Seat==0 && _drivers[0]==_keyboard ? _camera.transform.position : (Vector3?)null);
            DrawDroppedGuns(_match.Public.Tick+alpha);
            if (_objective != null)
            {
                var point = _match.ObjectiveAt(_match.Public.Tick+alpha);
                _objective.SetActive(point.HasValue && !publicOnly);
                if (point.HasValue) _objective.transform.position = point.Value;
            }
            DrawEquipment(publicOnly);
            _hud.Present(self, watched, _ports);
            UpdatePointer();
            _audio.Present(watched,_world);
        }

        private void UpdatePointer()
        {
            bool capture = _focused && Ready && !InLobby && !_match.IsPaused && MatchState.Phase!=RoundPhase.Finished &&
                (!_hud.ShopOpen || _keyboard.ShopLook);
            var mode = capture ? CursorLockMode.Locked : CursorLockMode.None;
            if (Cursor.lockState!=mode) Cursor.lockState=mode;
            Cursor.visible=!capture;
        }

        private void OnApplicationFocus(bool focused)
        {
            _focused = focused; _discardFrameTime=true;
            _keyboard?.ResetPointer();
            UpdatePointer();
        }

        private void DrawEquipment(bool publicOnly)
        {
            for (int i = 0; i < _match.GroundKits.Count; i++)
            {
                if (_kitModels.Count == i)
                {
                    var kit = AppearancePresenter.NewRoot("拆除套件", transform);
                    AppearancePresenter.AddVisual(_presentation.recoveryKit,kit.transform,Vector3.zero);
                    _kitModels.Add(kit);
                }
                _kitModels[i].transform.position = _match.GroundKits[i];
                _kitModels[i].SetActive(!publicOnly);
            }
            for (int i = _match.GroundKits.Count; i < _kitModels.Count; i++) _kitModels[i].SetActive(false);
        }

        private void DrawDroppedGuns(float frame)
        {
            _retired.Clear();
            foreach (var item in _drops) _retired.Add(item.Key);
            foreach (var pickup in _match.GroundWeapons)
            {
                _retired.Remove(pickup);
                var position=pickup.At(frame);
                if (_drops.TryGetValue(pickup,out var existing)) { if(existing.transform.position!=position) existing.transform.position=position; continue; }
                var root = AppearancePresenter.NewRoot("Dropped " + pickup.Item.DisplayName, transform);
                root.transform.SetPositionAndRotation(position, pickup.Rest.rotation);
                var model = AppearancePresenter.AddVisual(pickup.Model, root.transform, Vector3.zero);
                if(pickup.Weapon!=null) AppearancePresenter.ApplySkin(model, pickup.Weapon.Definition, _catalog.Find<WeaponSkinDefinition>(pickup.Weapon.SkinId));
                root.SetActive(true); _drops.Add(pickup, root);
            }
            foreach (var key in _retired) { AppearancePresenter.Retire(_drops[key]); _drops.Remove(key); }
        }

        private void OnDestroy()
        {
            if(_navigation.valid)_navigation.Remove();
            if(_catalog && _catalog.name.EndsWith("(Clone)"))Destroy(_catalog);
            _remote?.Dispose();
            GraphicsOptions?.Dispose();
            _keyboard?.Dispose();
            _audio?.Dispose();
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            if (_loadedMap.IsValid() && _loadedMap.isLoaded && SceneManager.sceneCount > 1)
                SceneManager.UnloadSceneAsync(_loadedMap);
        }
    }
}
