using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;

namespace AnimeCS.Play
{
    internal sealed class AppearancePresenter
    {
        private readonly ContentCatalog _catalog;
        private readonly ArenaPawn _actor;
        private readonly Transform _camera;
        private readonly MatchPresentation _style;
        private GameObject _third, _first, _worldGun, _viewGun;
        private CharacterRig _rig, _viewRig;
        private FirstPersonBody _localBody;
        private ReloadInsert _worldInsert, _viewInsert;

        internal struct ReloadInsert
        {
            private Transform _part,_hinge;
            private Quaternion _hingeRest;
            private Vector3 _rest;
            public Bounds? Bounds;
            public ReloadInsert(Transform mount)
            {
                _part=null; _rest=default; Bounds=null; _hinge=null; _hingeRest=Quaternion.identity;
                foreach(var child in mount.GetComponentsInChildren<Transform>(true))
                    if(child.name=="InspectHinge"){_hinge=child;_hingeRest=child.localRotation;break;}
                foreach (var mesh in mount.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mesh.name != "ReloadInsert") continue;
                    _part=mesh.transform; _rest=_part.localPosition;
                    var b=mesh.localBounds;
                    var bounds=new Bounds(mount.InverseTransformPoint(_part.TransformPoint(b.center)),Vector3.zero);
                    for (int i=0;i<8;i++)
                    {
                        var sign=new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1);
                        bounds.Encapsulate(mount.InverseTransformPoint(_part.TransformPoint(b.center+Vector3.Scale(b.extents,sign))));
                    }
                    Bounds=bounds;
                    break;
                }
            }
            public void Apply(Transform mount, Vector3 offset,float hinge=0)
            {
                if(_hinge)_hinge.localRotation=_hingeRest*Quaternion.Euler(hinge,0,0);
                if (_part) _part.localPosition=_rest+_part.parent.InverseTransformVector(mount.TransformVector(offset));
            }
        }
        private string _outfit, _gun, _skin;
        private Side _side;
        private bool _built, _watch, _showView;
        private WeaponDefinition _equipped;
        private readonly GameObject _objectiveModel;
        private UtilityDefinition _utility;
        private const string Objective = HeldItemLibrary.ObjectiveId;
        private HeldItemActions _actions;
        private GameObject CarriedModel => _gun==Objective?_objectiveModel:_utility?_utility.Model:null;
        private Vector3 _tip;
        private GameObject _viewFlash, _worldFlash;
        private bool _flashVisible;
        private int _lastPresentedRound = -1, _lastPresentedTick = -1, _shotFrame = -1;
        private int _predictedRound=-1,_predictedTick=-1;
        internal void PredictTrigger(int round,int tick){_predictedRound=round;_predictedTick=tick;}
        private Quaternion _aim;
        private Transform _mark, _stature;
        private float _stanceEyes, _feetWeight;
        private int _viewTick = -1;
        private float _stride, _bodyYaw, _lookYaw, _lookPitch, _swing;
        private int _bodyRound=-1;
        private WornItems _worn;
        private float _flinch, _obstruction;
        private WeaponActionPose _viewAction, _worldAction;
        private HitZone _hitZone;
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap"), MainTex = Shader.PropertyToID("_MainTex");

        private SkinnedMeshRenderer[] _cueRenderers=System.Array.Empty<SkinnedMeshRenderer>();
        private readonly MaterialPropertyBlock _cueBlock=new();
        private static readonly int FriendlyRim=Shader.PropertyToID("_FriendlyRim");
        private bool _cueActive;
        internal void SetFriendlyCue(bool visible)
        {
            if(_cueActive==visible)return;
            _cueActive=visible;
            foreach(var renderer in _cueRenderers)
            {
                renderer.GetPropertyBlock(_cueBlock);
                _cueBlock.SetFloat(FriendlyRim,visible?1f:0f);
                renderer.SetPropertyBlock(_cueBlock);
            }
        }

        public AppearancePresenter(ContentCatalog catalog, ArenaPawn actor, Transform camera, MatchPresentation style = null,GameObject objectiveModel=null)
        { _catalog = catalog; _actor = actor; _camera = camera; _style = style; _objectiveModel=objectiveModel; }

        internal bool NeedsBody(in SeatView view) =>
            !_built || _outfit != view.OutfitId || _side != view.Side || !_worn.Equals(view.Worn);

        public void Present(in SeatView view, bool watch, float alpha = 0)
        {
            _showView=watch && view.Health>0 && view.ScopeLevel==0;
            if (view.Health<=0) { Hide(); return; }
            bool lowering = _built && _gun != null && _watch == watch && _bodyRound == view.Match.Round &&
                WeaponActionPose.DrawProgress(view, alpha) < WeaponActionPose.SwapPoint;
            string id = lowering ? _gun : view.Held==HeldSlot.Utility?view.UtilityId : view.Held==HeldSlot.Objective?Objective:view.WeaponId;
            string skinId = lowering ? _skin : view.SkinId;
            bool bodyChanged = NeedsBody(view);
            bool cameraChanged = !_built || _watch != watch || bodyChanged;
            bool gunChanged = !_built || _gun != id || _skin != skinId;
            if (watch && gunChanged && _built && _viewRig == null)
                cameraChanged |= (_equipped?.Class == WeaponClass.Pistol) != (_catalog.Find<WeaponDefinition>(id)?.Class == WeaponClass.Pistol);
            if (!bodyChanged && !cameraChanged && !gunChanged)
            {
                if (_third != null) _third.SetActive(view.Health > 0 && (!watch || view.Pitch < -18f));
                if (_first != null) _first.SetActive(watch && (_equipped || CarriedModel));
                UpdateMotion(view, alpha);
                return;
            }
            _built = true; _outfit = view.OutfitId; _gun = id; _skin = skinId; _side = view.Side; _worn=view.Worn; _watch = watch;
            var outfit = _catalog.Find<OutfitDefinition>(_outfit);
            var gun = _catalog.Find<WeaponDefinition>(_gun);
            _actions = _style && _style.heldItems ? _style.heldItems.Find(id) : null;
            _equipped = gun; _utility=_catalog.Find<UtilityDefinition>(id);
            if (gun && gunChanged)
            {
                var bounds=ArenaWorld.ModelBounds(gun.WorldModel);
                _tip=new Vector3(bounds.center.x, Mathf.Clamp(gun.Support.y+.045f,bounds.min.y,bounds.max.y),bounds.max.z);
            }
            var skin = _catalog.Find<WeaponSkinDefinition>(_skin);
            if (bodyChanged)
            {
                Retire(_third); _worldGun = null;
                _third = NewRoot("Third person", _actor.Visuals);
                var character = outfit != null && outfit.Character != null ? outfit.Character : _catalog.Character;
                _stature=new GameObject("角色身形").transform; _stature.SetParent(_third.transform,false);
                float height=character.StandingEyes>0 ? Mathf.Clamp(SeatMovement.EyeHeight(0)/character.StandingEyes,.88f,1.14f) : 1f;
                _stature.localScale=new Vector3(Mathf.Sqrt(height),height,Mathf.Sqrt(height));
                var body=AddVisual(character.Model,_stature,Vector3.zero);
                _rig=new CharacterRig(body,character,outfit ? outfit.PoseController : null);
                var lowerWearables=new System.Collections.Generic.List<Renderer>();
                WearablePresenter.Apply(_catalog,_worn,outfit,false,body,_rig,lowerWearables);
                if(_rig.Head)_rig.Head.localScale*=character.CombatHeadScale;
                if (outfit != null) _rig.BindClothing(AddVisual(outfit.Model, _stature, Vector3.zero));
                _cueRenderers=_stature.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                _cueActive=false;
                var mark = AddVisual(character.Insignia(_side), _stature, Vector3.zero);
                bool followsSkin = mark && mark.GetComponentInChildren<SkinnedMeshRenderer>(true) && _rig.BindClothing(mark);
                _mark = mark && !followsSkin ? mark.transform : null;
                _localBody=new FirstPersonBody(_third,outfit,lowerWearables);
            }
            if ((bodyChanged || gunChanged) && !gun) { Retire(_worldGun); _worldGun = null; }
            if ((bodyChanged || gunChanged) && gun != null)
            {
                Retire(_worldGun);
                bool socket = _rig.WeaponSocket != null && _rig.Grip?.Valid != true;
                _worldGun = MountGun(socket ? _rig.WeaponSocket : _third.transform,
                    socket ? Vector3.zero : new Vector3(0.24f, 1.13f, 0.2f), gun, skin, false);
                _worldInsert=new ReloadInsert(_worldGun.transform);
                _worldFlash=MakeFlash(_worldGun,false);
            }
            if((bodyChanged || gunChanged) && CarriedModel)
            {
                _worldGun=MountCarried(_third.transform);
                _worldInsert=default; _worldFlash=null;
            }
            _actor.Visuals.gameObject.SetActive(view.Health > 0);
            _localBody?.SetLocal(watch);
            if(_worldGun)_worldGun.SetActive(!watch);
            _third.SetActive(view.Health > 0 && (!watch || view.Pitch < -18f));
            if (cameraChanged) { Retire(_first); _first = _viewGun = null; _viewRig = null; }
            UpdateMotion(view, alpha);
            if (!watch || view.Health <= 0) return;
            if (_first == null)
            {
                _first = NewRoot("View model", _camera);
                if (outfit && outfit.ViewRig)
                {
                    var sleeves = AddVisual(outfit.ViewRig, _first.transform, Vector3.down * 1.60f);
                    _viewRig = new CharacterRig(sleeves, outfit.Character, alwaysAnimate:true);
                    WearablePresenter.Apply(_catalog,_worn,outfit,true,sleeves,_viewRig);
                    foreach (var skinRenderer in sleeves.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        skinRenderer.updateWhenOffscreen = true;
                        skinRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                }
                else if (outfit) AddVisual(outfit.ViewArms(gun?.Class == WeaponClass.Pistol), _first.transform, Vector3.zero);
            }
            if ((cameraChanged || gunChanged) && gun != null)
            {
                Retire(_viewGun);
                _viewGun = MountGun(_first.transform, new Vector3(0.23f, -0.22f, 0.43f), gun, skin, true);
                _viewInsert=new ReloadInsert(_viewGun.transform);
                _viewFlash=MakeFlash(_viewGun,true);
            }
            if(!gun && (gunChanged || cameraChanged)) { Retire(_viewGun); _viewGun=CarriedModel?MountCarried(_first.transform):null; _viewInsert=default; _viewFlash=null; }
            UseViewLayer(_first);
            _first.SetActive(gun || CarriedModel);
        }

        private GameObject MountCarried(Transform parent)
        {
            var mount=NewRoot(_utility?_utility.DisplayName:"手持目标包",parent);
            AddVisual(CarriedModel,mount.transform,Vector3.zero);mount.SetActive(true);return mount;
        }

        private void UpdateMotion(in SeatView view, float alpha)
        {
            _stanceEyes=_actor.RenderEye(alpha).y-_actor.RenderFeet(alpha).y;
            _feetWeight=view.Motion.Grounded ? 1f-Mathf.InverseLerp(.12f,1.5f,view.Motion.Velocity.magnitude) : 0f;
            float scale = _style ? _style.handlingMotion : 1f;
            if (_showView) _viewAction = WeaponActionPose.Sample(view, _equipped, alpha, true, scale, _actions);
            _worldAction = WeaponActionPose.Sample(view, _equipped, alpha, false, scale, _actions);
            // Several simulation ticks can elapse between rendered frames.
            if (_lastPresentedRound == view.Match.Round && view.LastShotTick > _lastPresentedTick)
                _shotFrame = Time.frameCount;
            _lastPresentedRound = view.Match.Round;
            _lastPresentedTick = view.Match.Tick;
            _flashVisible = _equipped && !_equipped.Suppressed && _equipped.Class != WeaponClass.Knife && view.Health > 0 &&
                view.LastShotTick >= 0 && (_shotFrame == Time.frameCount || view.Match.Tick + alpha - view.LastShotTick < 2.5f);
            if (_worldFlash) _worldFlash.SetActive(_flashVisible);
            bool predicted=_watch && view.Health>0 && _predictedRound==view.Match.Round && _predictedTick==view.Match.Tick+1;
            if (_viewFlash) _viewFlash.SetActive(_flashVisible || predicted && _equipped && !_equipped.Suppressed);
            if(predicted) _viewAction.Offset+=Vector3.back*.014f;
            if (_watch && _equipped && view.Health > 0 && view.ScopeLevel==0)
            {
                float reach = _equipped.Class==WeaponClass.Pistol || _equipped.Class==WeaponClass.Knife ? .61f : .92f;
                float close = Physics.SphereCast(_camera.position,.055f,_camera.forward,out var hit,reach,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                    ? 1f-Mathf.InverseLerp(.22f,reach,hit.distance) : 0;
                _obstruction = Mathf.MoveTowards(_obstruction, close, Time.unscaledDeltaTime * 9f);
                _viewAction.Offset += new Vector3(.015f,-.082f,-.036f)*_obstruction;
                _viewAction.Angles += new Vector3(21,-10,-9)*_obstruction;
            }
            float speed = _equipped != null ? _equipped.RunSpeed : 6f;
            float age = (view.Match.Tick - view.LastHitTick) * SeatMovement.StepTime;
            float t = _style ? Mathf.Clamp01(age / Mathf.Max(.03f,_style.flinchSeconds)) : 1;
            _flinch = _style && _style.hitReaction ? Mathf.Sin(Mathf.PI*Mathf.Sqrt(t))*(1-t)*_style.flinchDegrees : 0;
            _hitZone = view.LastHitZone;
            _rig?.Animate(view, speed, alpha);
            float shotPhase = _style ? Mathf.Clamp01((view.Match.Tick-view.LastShotTick)*SeatMovement.StepTime /
                Mathf.Max(.04f,_style.shotReturnSeconds)) : 1;
            float kick = _style ? (1-shotPhase)*(1-shotPhase)*_style.shotLiftDegrees : 0;
            int tick=view.Match.Tick;
            _bodyYaw=_rig?.BodyYaw ?? view.Yaw;
            _lookYaw=_rig?.WaistYaw ?? 0;
            _lookPitch=view.Pitch;_bodyRound=view.Match.Round;
            if(_third)
            {
                _third.transform.rotation=Quaternion.Euler(0,_bodyYaw,0);
                // Place the local hips behind the eye so the thighs enter from the bottom of the frame.
                float setback=_watch?Mathf.Lerp(.40f,.64f,Mathf.InverseLerp(28,79,-view.Pitch)):0;
                _third.transform.position=_actor.RenderFeet(alpha)-Quaternion.Euler(0,view.Yaw,0)*Vector3.forward*setback;
            }
            bool knife=_equipped && _equipped.Class==WeaponClass.Knife;
            _swing=knife && _actions?.Attack(view.LastMelee) == null ? Mathf.Sin(Mathf.PI*Mathf.Clamp01((tick-view.LastShotTick)*SeatMovement.StepTime/.30f)) : 0;
            if(knife)kick=0;
            _aim = Quaternion.Euler(-Mathf.Clamp(view.Pitch+kick,-66,69), view.Yaw, 0);
            if (_first == null || !_showView || view.Match.Paused || _viewTick == view.Match.Tick) return;
            int ticks = _viewTick >= 0 ? Mathf.Clamp(view.Match.Tick - _viewTick, 0, 8) : 0;
            _viewTick = view.Match.Tick;
            float pace = Mathf.Clamp01(view.Motion.Velocity.magnitude / speed);
            if (view.Motion.Grounded) _stride += ticks * SeatMovement.StepTime * pace * 10;
            float bob = view.Motion.Grounded ? pace : 0;
            bool longGun=_equipped && _equipped.Class!=WeaponClass.Pistol && _equipped.Class!=WeaponClass.Knife;
            var framing=longGun && _style?_style.longGunViewOffset:Vector3.zero;
            _first.transform.localPosition = framing+new Vector3(Mathf.Sin(_stride)*.009f*bob, Mathf.Cos(2*_stride)*.006f*bob, -kick*.0015f);
            _first.transform.localRotation = Quaternion.Euler(Mathf.Clamp(-view.Motion.VerticalSpeed*.36f,-2,2)-_flinch*.4f-kick*.32f, 0, _flinch*.18f);
        }

        public void AlignWorldGun()
        {
            if (_third != null && _third.activeInHierarchy)
            {
                _rig.RestoreHitPose();
                var basis=_third.transform;
                _rig.Look(basis,_lookYaw,_lookPitch);
                // Humanoid retargeting can lower the ankles below the mesh origin in a crouch.
                // Fit the planted sole and eye together, using the moving leg pose each frame.
                if (_rig.Head && _stature)
                {
                    var scale=_stature.localScale;
                    float origin=_stature.position.y;
                    float eye=(_rig.FaceCenter.y-origin)/scale.y;
                    float sole=(_rig.LowestSole(origin)-origin)/scale.y*_feetWeight;
                    float floor=-_actor.GroundSkin*_feetWeight;
                    if (eye-sole>.20f)
                    {
                        scale.y=Mathf.Clamp((_stanceEyes-floor)/(eye-sole),.76f,1.18f);
                        _stature.localScale=scale;
                        _stature.localPosition=Vector3.up*(floor-sole*scale.y);
                    }
                }
                _rig.Flinch(_flinch,_hitZone);
                if (_worldGun)
                {
                    _worldInsert.Apply(_worldGun.transform,_worldAction.InsertOffset,_worldAction.Hinge);
                    if(_gun==Objective && _rig.Grip?.Valid==true)_rig.Grip.PoseDevice(basis,_worldGun.transform,_aim,false,_worldAction);
                    else if(_utility && _rig.Grip?.Valid==true)_rig.Grip.PoseKnife(basis,_worldGun.transform,null,_aim,false,0,_worldAction);
                    else if (_rig.Grip?.Valid==true)
                        _rig.Grip.Pose(basis,_worldGun.transform,_equipped,_aim,false,_swing,_worldAction,_worldInsert.Bounds);
                }
                if (_mark && _rig.Chest)
                {
                    _mark.localScale = Vector3.one*.73f;
                    _mark.position = _rig.Chest.position + basis.up*(.11f-1.28f*.73f);
                }
            }
            if (_worldGun != null && _worldGun.activeInHierarchy && _rig.Grip?.Valid != true && _rig.AimFromHand && _equipped != null)
                _worldGun.transform.rotation = _aim * _equipped.Rotation(false);
            if (_showView && _first && _first.activeInHierarchy && _viewGun)
            {
                _viewInsert.Apply(_viewGun.transform,_viewAction.InsertOffset,_viewAction.Hinge);
                if(_gun==Objective && _viewRig?.Grip?.Valid==true)_viewRig.Grip.PoseDevice(_first.transform,_viewGun.transform,_first.transform.rotation,true,_viewAction);
                else if(_utility && _viewRig?.Grip?.Valid==true)_viewRig.Grip.PoseKnife(_first.transform,_viewGun.transform,null,_first.transform.rotation,true,0,_viewAction);
                else if (_viewRig?.Grip?.Valid==true)
                    _viewRig.Grip.Pose(_first.transform,_viewGun.transform,_equipped,_first.transform.rotation,true,_swing,_viewAction,_viewInsert.Bounds);
            }
        }

        public Vector3 LabelAnchor(float alpha) => _rig?.Head && _third && _third.activeInHierarchy
            ? _rig.Head.position+Vector3.up*.26f : _actor.RenderEye(alpha)+Vector3.up*.24f;

        private Vector3 Tip(bool view) => _equipped.Muzzle(view)!=Vector3.zero ? _equipped.Muzzle(view) : _tip;
        private GameObject MakeFlash(GameObject mount,bool view)
        {
            if (!_style || !_style.muzzleMaterial || _equipped.Suppressed || _equipped.Class==WeaponClass.Knife) return null;
            var flash=NewRoot("枪口火光",mount.transform); flash.transform.localPosition=Tip(view);
            flash.transform.localScale=Vector3.one*(_equipped.Class==WeaponClass.Pistol?.075f:.106f);
            flash.AddComponent<MeshFilter>().sharedMesh=Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var renderer=flash.AddComponent<MeshRenderer>(); renderer.sharedMaterial=_style.muzzleMaterial;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows=false;
            return flash;
        }

        public Vector3? Muzzle()
        {
            var mount=_watch ? _viewGun : _worldGun;
            return _equipped && _equipped.Class!=WeaponClass.Knife && mount && mount.activeInHierarchy
                ? mount.transform.TransformPoint(Tip(_watch)) : (Vector3?)null;
        }
        public void BeforeAnimation() => _rig?.RestoreHitPose();
        public void Hide()
        {
            SetFriendlyCue(false);
            if (_first && _first.activeSelf) _first.SetActive(false);
            if (_third && _third.activeSelf) _third.SetActive(false);
        }

        internal static void UseViewLayer(GameObject root)
        {
            int layer = LayerMask.NameToLayer("First Person"); if (layer < 0 || !root) return;
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=layer;
        }

        internal static GameObject MountGun(Transform parent, Vector3 anchor, WeaponDefinition gun, WeaponSkinDefinition skin, bool firstPerson)
        {
            var mount = NewRoot("Weapon", parent);
            mount.transform.localPosition = anchor + gun.Offset(firstPerson);
            mount.transform.localRotation = gun.Rotation(firstPerson);
            ApplySkin(AddVisual(gun.PosedModel(firstPerson), mount.transform, Vector3.zero), gun, skin);
            mount.SetActive(true);
            return mount;
        }

        internal static GameObject NewRoot(string name, Transform parent)
        {
            var root = new GameObject(name);
            root.SetActive(false); root.transform.SetParent(parent, false);
            return root;
        }

        internal static GameObject AddVisual(GameObject source, Transform parent, Vector3 localPosition)
        {
            if (source == null) return null;
            // Caller keeps the parent inactive until imported presentation components have been isolated.
            var copy = Object.Instantiate(source, parent, false);
            copy.transform.localPosition += localPosition;
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var rigidbody in copy.GetComponentsInChildren<Rigidbody>(true))
            { rigidbody.isKinematic = true; rigidbody.detectCollisions = false; }
            foreach (var script in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (script != null) script.enabled = false;
            foreach (var camera in copy.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (var sourceAudio in copy.GetComponentsInChildren<AudioSource>(true)) sourceAudio.enabled = false;
            foreach (var listener in copy.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            foreach (var animator in copy.GetComponentsInChildren<Animator>(true)) animator.applyRootMotion = false;
            return copy;
        }

        internal static void ApplySkin(GameObject model, WeaponDefinition definition, WeaponSkinDefinition skin)
        {
            if (!skin && definition) skin = definition.DefaultSkin;
            if (model == null || skin == null || skin.Weapon != definition) return;
            var block = new MaterialPropertyBlock();
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                int slot = skin.MaterialSlot;
                var materials = renderer.sharedMaterials;
                if (slot >= materials.Length) continue;
                if (skin.Material != null) { materials[slot] = skin.Material; renderer.sharedMaterials = materials; }
                if (skin.Texture == null) continue;
                renderer.GetPropertyBlock(block, slot);
                block.SetTexture(BaseMap, skin.Texture); block.SetTexture(MainTex, skin.Texture);
                renderer.SetPropertyBlock(block, slot);
            }
        }
        internal static void Retire(GameObject obj)
        {
            if (obj == null) return;
            obj.SetActive(false); Object.Destroy(obj);
        }
    }
}
