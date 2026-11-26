using System;
using System.Collections.Generic;
using AnimeCS.Content;
using UnityEngine;

namespace AnimeCS.Play
{
    [Serializable]
    public struct LobbyMember
    {
        public string outfitId, weaponId, skinId, displayId;
        public WornItems worn;
        public LobbyMember(string outfit, string weapon = null, string skin = null, WornItems worn=default,string displayId=null)
        { outfitId = outfit; weaponId = weapon; skinId = skin; this.worn=worn; this.displayId=displayId; }
    }

    internal sealed class LobbyPreview
    {
        private readonly GameObject _stage;
        private readonly Transform _members;
        private readonly Camera _camera;
        private readonly MatchPresentation _style;
        private readonly ContentCatalog _catalog;
        private readonly float _matchFov, _matchFar;
        private readonly List<Avatar> _party = new List<Avatar>(5);
        private float _rotation, _stageCenter = .59f, _itemAngle;
        private Transform _itemPivot;
        private Bounds _itemBounds;
        private bool _inspection;
        private GameObject _sleeves;
        private CharacterRig _hands;
        private Transform _held;
        private LobbyMember _sample;
        public int Count => _party.Count;

        public LobbyPreview(Transform owner, Camera camera, MatchPresentation style, ContentCatalog catalog)
        {
            _camera = camera; _style = style; _catalog = catalog;
            _matchFov = camera.fieldOfView; _matchFar = camera.farClipPlane;
            _stage = AppearancePresenter.NewRoot("队伍大厅",owner);
            _stage.transform.position = new Vector3(2000, 0, 0);
            if (style.lobbySet) AppearancePresenter.AddVisual(style.lobbySet,_stage.transform,Vector3.zero);
            _members = new GameObject("Party").transform;
            _members.SetParent(_stage.transform, false);
            Light("人物主光", new Vector3(-2.0f,3.0f,2.3f), new Color(1,.90f,.78f), 8,9);
            Light("人物补光", new Vector3(2.4f,2.0f,2.6f), new Color(.73f,.86f,1), 5,9);
            Light("背景轮廓光",new Vector3(.4f,2.6f,-2.3f),new Color(.55f,.83f,.91f),7,10);
        }

        private void Light(string name, Vector3 position, Color color, float intensity, float range)
        {
            var lamp = new GameObject(name).AddComponent<Light>();
            lamp.transform.SetParent(_stage.transform,false);
            lamp.transform.localPosition = position;
            lamp.type = LightType.Point; lamp.color=color; lamp.intensity=intensity; lamp.range=range;
        }

        public void Show(LobbyMember self, IReadOnlyList<LobbyMember> teammates)
        {
            ClearItem();
            _stage.SetActive(true);
            int count = 1 + Mathf.Min(4,teammates.Count);
            for (int i=_party.Count-1; i>=count; i--)
            { AppearancePresenter.Retire(_party[i].root); _party.RemoveAt(i); }
            for (int i=0; i<count; i++)
            {
                var member = i==0 ? self : teammates[i-1];
                if (i==_party.Count) _party.Add(Build(member));
                else if (!_party[i].member.Equals(member))
                { AppearancePresenter.Retire(_party[i].root); _party[i]=Build(member); }
                int column = (i+1)/2 * (i%2==0 ? 1 : -1);
                float center = count%2==0 ? .5f : 0f;
                var avatar = _party[i].root.transform;
                avatar.localPosition = new Vector3((column+center)*.91f,.008f,-Mathf.Abs(column)*.28f);
                avatar.localRotation = Quaternion.Euler(0, -29 + (i==0 ? _rotation : column*6), 0);
            }
            Frame();
        }

        public void SetInspection(bool first)
        {
            _inspection=first;_members.gameObject.SetActive(!first);
            if(_sleeves)_sleeves.SetActive(first);
            if(!first || _party.Count==0)return;
            var self=_party[0];var member=self.member;
            if(_sleeves && _sample.Equals(member))return;
            AppearancePresenter.Retire(_sleeves);
            _sample=member;var outfit=_catalog.Find<OutfitDefinition>(member.outfitId) ?? _catalog.Outfit;
            _sleeves=AppearancePresenter.NewRoot("衣柜第一人称",_camera.transform);
            var model=AppearancePresenter.AddVisual(outfit.ViewRig,_sleeves.transform,Vector3.down*1.60f);
            _hands=model ? new CharacterRig(model,outfit.Character) : null;
            if(model)WearablePresenter.Apply(_catalog,member.worn,outfit,true,model,_hands);
            else AppearancePresenter.AddVisual(outfit.ViewArms(self.gun?.Class==WeaponClass.Pistol),_sleeves.transform,Vector3.zero);
            _held=self.gun ? AppearancePresenter.MountGun(_sleeves.transform,new Vector3(.18f,-.19f,.39f),self.gun,_catalog.Find<WeaponSkinDefinition>(member.skinId),true).transform : null;
            _sleeves.SetActive(true);
        }
        public void SetPageFraming(float center) { _stageCenter=center; Frame(); }
        public void ClearItem()
        {
            if (_itemPivot) AppearancePresenter.Retire(_itemPivot.gameObject);
            _itemPivot=null;
        }

        public void ShowItem(ContentDefinition item)
        {
            ClearItem(); SetInspection(false);
            var skin=item as WeaponSkinDefinition;
            var gun=skin ? skin.Weapon : item as WeaponDefinition;
            var source=gun ? gun.WorldModel : (item as UtilityDefinition)?.Model;
            if (!source) return;
            _members.gameObject.SetActive(false);
            _itemPivot=AppearancePresenter.NewRoot("库存检视",_camera.transform).transform;
            _itemBounds=ArenaWorld.ModelBounds(source);
            var model=AppearancePresenter.AddVisual(source,_itemPivot,-_itemBounds.center);
            if(gun) AppearancePresenter.ApplySkin(model,gun,skin);
            _itemAngle=72f; _itemPivot.gameObject.SetActive(true); Frame();
        }

        public void Rotate(float degrees)
        {
            if(_itemPivot) { _itemAngle=Mathf.Repeat(_itemAngle+degrees,360); return; }
            _rotation = Mathf.Repeat(_rotation+degrees+180,360)-180;
            if (_party.Count>0) _party[0].root.transform.localRotation=Quaternion.Euler(0,-29+_rotation,0);
        }

        public void Hide()
        {
            ClearItem();SetInspection(false);_stage.SetActive(false); _camera.fieldOfView=_matchFov; _camera.farClipPlane=_matchFar;
        }

        public void Present()
        {
            Frame();
            if(_inspection && _hands?.Grip?.Valid==true && _held){_hands.Grip.Pose(_sleeves.transform,_held,_party[0].gun,_sleeves.transform.rotation,true);return;}
            foreach (var entry in _party)
            {
                if (entry.rig.Grip?.Valid != true || !entry.mount) continue;
                var root = entry.root.transform;
                // A relaxed, angled-away display pose shows the finish and keeps the face visible.
                var aim = root.rotation * Quaternion.Euler(18,-19,-6);
                entry.rig.Grip.Pose(root,entry.mount,entry.gun,aim,false);
            }
        }

        private void Frame()
        {
            if(_itemPivot)
            {
                _camera.fieldOfView=29f; _camera.farClipPlane=42;
                _camera.transform.SetPositionAndRotation(_stage.transform.position+new Vector3(.4f,1.45f,2.7f),Quaternion.Euler(0,178,0));
                const float itemDistance=.98f;
                float itemHalfHeight=itemDistance*Mathf.Tan(.5f*_camera.fieldOfView*Mathf.Deg2Rad);
                var size=_itemBounds.size;
                float horizontal=Mathf.Sqrt(size.x*size.x+size.z*size.z);
                float fitWidth=1.20f*itemHalfHeight*_camera.aspect/Mathf.Max(.01f,horizontal);
                float fitHeight=1.30f*itemHalfHeight/Mathf.Max(.01f,size.y+.24f*horizontal);
                _itemPivot.localScale=Vector3.one*Mathf.Min(fitWidth,fitHeight);
                _itemPivot.localPosition=new Vector3(.16f*itemHalfHeight*_camera.aspect,-.025f,itemDistance);
                _itemPivot.localRotation=Quaternion.Euler(-9,_itemAngle,-7);
                return;
            }
            if(_inspection)
            {
                _camera.fieldOfView=_matchFov;_camera.farClipPlane=42;
                _camera.transform.SetPositionAndRotation(_stage.transform.position+new Vector3(.4f,1.58f,2.7f),Quaternion.Euler(0,178,0));return;
            }
            float fov = _style.lobbyFieldOfView;
            float halfHeight = 1.12f;
            float neededWidth = (.65f + Mathf.Floor(_party.Count*.5f)*.91f) / .74f;
            halfHeight = Mathf.Max(halfHeight,neededWidth/Mathf.Max(.5f,_camera.aspect));
            float distance = halfHeight / Mathf.Tan(.5f*fov*Mathf.Deg2Rad);
            var focus = _stage.transform.position + new Vector3((_stageCenter-.5f)*2f*halfHeight*_camera.aspect,.93f,0);
            var eye = focus + new Vector3(.13f*distance,.25f,distance);
            _camera.fieldOfView=fov; _camera.farClipPlane=42;
            _camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(focus-eye,Vector3.up));
        }

        private Avatar Build(LobbyMember member)
        {
            var outfit = _catalog.Find<OutfitDefinition>(member.outfitId) ?? _catalog.Outfit;
            var character = outfit.Character ? outfit.Character : _catalog.Character;
            var gun = _catalog.Find<WeaponDefinition>(member.weaponId) ?? _style.lobbyWeapon;
            var root = AppearancePresenter.NewRoot(outfit.DisplayName,_members);
            var body=AppearancePresenter.AddVisual(character.Model,root.transform,Vector3.zero);
            var rig=new CharacterRig(body,character,outfit.PoseController);
            WearablePresenter.Apply(_catalog,member.worn,outfit,false,body,rig);
            rig.BindClothing(AppearancePresenter.AddVisual(outfit.Model,root.transform,Vector3.zero));
            Transform mount = null;
            if (gun)
            {
                bool stick = rig.Grip?.Valid != true && rig.WeaponSocket;
                mount = AppearancePresenter.MountGun(stick ? rig.WeaponSocket : root.transform,
                    stick || rig.Grip?.Valid == true ? Vector3.zero : new Vector3(.18f,1.12f,.22f),
                    gun,_catalog.Find<WeaponSkinDefinition>(member.skinId),false).transform;
            }
            root.SetActive(true);
            return new Avatar { member=member, root=root, rig=rig, mount=mount, gun=gun };
        }

        private sealed class Avatar
        {
            public LobbyMember member;
            public GameObject root;
            public CharacterRig rig;
            public Transform mount;
            public WeaponDefinition gun;
        }
    }
}
