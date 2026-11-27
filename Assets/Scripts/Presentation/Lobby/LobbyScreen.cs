using System;
using System.Linq;
using System.Collections.Generic;
using AnimeCS.Content;
using AnimeCS.Match;
using AnimeCS.Profile;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed partial class LobbyScreen
    {
        readonly VisualElement _screen,_sheet,_content;
        readonly ILobbyHost _host;
        readonly ContentCatalog _catalog;
        WardrobeProfile _profile;
        WardrobeLoadout _draft;
        readonly Dictionary<string, Action> _pages;
        string _section,_gun;
        bool _first;
        int _equipment;
        readonly VisualElement _slots,_stage;
        ContentDefinition _item;
        Vector2 _inventoryScroll;
        static readonly string[] SlotNames={"头饰","上衣","手套","下衣","鞋子"};
        internal event Action LocalMatchRequested, PartyMatchRequested;
        internal event Action<MapDefinition> MapRequested;
        internal event Action<FirstSide> OpeningSideRequested;
        internal event Action<PlayAspect> AspectRequested;
        internal event Action<bool> FriendlyCuesRequested;
        internal event Action<float> SensitivityRequested, VolumeRequested;

        internal LobbyScreen(VisualElement host, VisualTreeAsset layout, StyleSheet theme, ContentCatalog catalog, ILobbyHost lobbyHost)
        {
            _host=lobbyHost;_catalog=catalog;layout.CloneTree(host);
            _screen=host.Q("lobby");
            if (theme) _screen.styleSheets.Add(theme);
            _sheet=_screen.Q("sheet");_content=_screen.Q("sheet-content");
            _slots=_sheet.Q("loadout-slots"); _stage=_sheet.Q("inspection-stage");
            _drag = new EquipmentDrag<ContentDefinition>(_screen, (card, item) => ItemCardContent(card, item, Art(item)));
            _drag.ItemRequested += MountItem;
            _stage.RegisterCallback<GeometryChangedEvent>(_=>FrameEquipment());
            void AttachTurntable(VisualElement stage)
            {
                stage.RegisterCallback<PointerDownEvent>(e=>{if(e.button==0 && e.target==stage)stage.CapturePointer(e.pointerId);});
                stage.RegisterCallback<PointerMoveEvent>(e=>{if(stage.HasPointerCapture(e.pointerId))_host.Preview.Rotate(-e.deltaPosition.x*.38f);});
                stage.RegisterCallback<PointerUpEvent>(e=>stage.ReleasePointer(e.pointerId));
            }
            AttachTurntable(_screen.Q("stage-view")); AttachTurntable(_screen.Q("inspection-stage"));
            _pages = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["inventory"] = Inventory, ["equipment"] = Equipment, ["character"] = Characters,
                ["mode"] = ModeOptions, ["settings"] = Settings, ["tasks"] = Tasks, ["shop"] = Shop
            };
            BindNavigation();
            var manual = _screen.Q("manual-summary");
            bool manualExpanded = true;
            _screen.Q<Button>("manual-toggle").clicked += () =>
            {
                manualExpanded = !manualExpanded;
                SetVisible(manual, manualExpanded);
                _screen.Q<Button>("manual-toggle").text = manualExpanded ? "任务手册    −" : "任务手册    +";
            };
            _screen.Q<Button>("manual-config").clicked += () => Dialog("config");
            BindAccount();
            RefreshMap();
        }

        public void RefreshMap()
        {
            var map = _host.SelectedMap;
            bool party=State?.party is {Length:>1};
            if(party) map=_catalog.Find<MapDefinition>(State.mapId);
            _screen.Q<Label>("map-title").text = party
                ? (map ? map.DisplayName : "服务器选图")+"\n小队爆破"
                : map.DisplayName+"\n"+(_host.Mode==MatchMode.AimPractice ? "自由训练" : $"{MatchFormat.TeamSize}v{MatchFormat.TeamSize} 攻守拆弹");
            _screen.Q<Image>("map-thumb").image = map ? (map.LoadingArt ? map.LoadingArt : map.Radar) : null;
        }

        public void Show()
        {
            _profile = _host.ReadWardrobe();
            Close();
            SetVisible(_screen.Q("auth"), !_signedIn);
            if (_signedIn) RefreshHeader(true);
        }

        public void Back()
        {
            if (!_signedIn) return;
            if (_drag.CancelDrag()) return;
            if (_dialogKey != null) { HideDialog(); return; }
            if (_section == "inventory" && _item) { _item = null; _host.Preview.EndInspection(); Render(); }
            else Close();
        }

        internal void CancelPointerInteraction() => _drag.CancelDrag();

        public void Close()
        {
            _drag.Clear();
            if (_section == "equipment" || _section == "inventory" || _section == "character") _host.Preview.EndInspection();
            _section = null; _draft = null; _item = null; _first = false;
            SetVisible(_sheet, false);
            SetVisible(_screen.Q("home-heading"), true);
            SetVisible(_screen.Q("topbar"), true);
            SelectTab("home");
            HideDialog();
        }

        void Open(string key)
        {
            Close();
            _section = key;
            _profile = _host.ReadWardrobe();
            _draft = _profile.Resolve(_profile.current, _catalog);
            _gun = _host.LobbyWeaponId;
            SetVisible(_sheet,true);
            SetVisible(_screen.Q("home-heading"),false);
            SetVisible(_screen.Q("topbar"),key != "settings" && key != "tasks");
            if (key == "equipment") BeginEquipment();
            if (key == "character") SelectCharacter(_catalog.Find<CharacterDefinition>(State?.showCharacterId) ?? CurrentCharacter);
            SelectTab(key);
            Render();
        }

        void Preview()
        {
            _draft=_profile.Resolve(_draft,_catalog);
            _host.Preview.ShowWardrobe(_draft,_gun,_first,previewWorn:_draft.WornFor(_equipmentSide));
            Render();
            FrameEquipment();
        }
        void Apply()
        {
            _profile.current=_profile.Resolve(_draft,_catalog);
            PersistWardrobe();
        }

        void PersistWardrobe()
        {
            bool online = State != null;
            RunAction(() => _host.Account.SaveWardrobeAsync(_profile, _gun), () =>
            {
                if (online) Notice("装备配置已保存");
                if (_section == "equipment") Preview();
            }, requireSignIn: online);
        }

        void Render()
        {
            _drag.Clear();
            _content.Clear();
            bool inspect = _section=="inventory" && _item;
            bool equipment = _section=="equipment", characters = _section=="character";
            _sheet.EnableInClassList("equipment-page", equipment);
            _sheet.EnableInClassList("character-page", characters);
            _sheet.EnableInClassList("inspect-page", inspect);
            _sheet.EnableInClassList("inventory-page", _section=="inventory" && !inspect);
            _sheet.EnableInClassList("full-page", _section=="settings" || _section=="tasks");
            SetVisible(_sheet.Q("wear-rail"), equipment);
            SetVisible(_sheet.Q("character-rail"), characters);
            SetVisible(_stage,equipment || inspect || characters);
            SetVisible(_sheet.Q("equipment-stage-controls"), equipment);
            SetVisible(_sheet.Q("equipment-preview"), equipment);
            SetVisible(_sheet.Q("stage-name"), characters);
            SetVisible(_sheet.Q("equipment-toolbar"), equipment);
            SetVisible(_sheet.Q("loadout-actions"), equipment);
            SetVisible(_sheet.Q("page-detail"), !inspect);
            SetVisible(_sheet.Q("sheet-title").parent, !(characters || equipment || (_section=="inventory" && !inspect)));
            _sheet.Q<Button>("close").text = inspect ? "‹  返回库存" : "‹  返回大厅";
            _sheet.Q<Label>("sheet-title").text = inspect ? _item.DisplayName : PageTitles[_section];
            SetVisible(_content,!inspect);
            SetVisible(_sheet.Q("inspect-caption"),inspect);
            if (!inspect) _pages[_section]();
            if (equipment || characters) FrameEquipment();
        }

        void Settings()
        {
            var settings = _host.Settings;
            GraphicsOptions(settings); Header("对局辅助");
            var friendly=new Toggle("队友三角与轮廓"){name="friendly-cues",value=settings.FriendlyCues};
            friendly.AddToClassList("setting-field");friendly.RegisterValueChangedCallback(e=>FriendlyCuesRequested?.Invoke(e.newValue));_content.Add(friendly);
            Header("操控与声音");
            Slider("鼠标灵敏度",.025f,.30f,settings.Sensitivity,v=>SensitivityRequested?.Invoke(v));
            Slider("音量",0,1,settings.Volume,v=>VolumeRequested?.Invoke(v));Header("键鼠");
            _content.Add(new Label("WASD 移动    Shift 静步    空格跳跃    Ctrl / C 下蹲"));
            _content.Add(new Label("1 主武器    2 副武器    3 刀    5 目标包    滚轮切换    Q 上一件"));
            _content.Add(new Label("左键使用    右键开镜    R 换弹    F 检视    E 交互    X 投道具    G 丢弃手持物"));
            _content.Add(new Label("B 购买    商店内按住右键转头    Esc 关闭商店 / 暂停"));
        }

        void ModeOptions()
        {
            if(State?.party is {Length:>1})
            {
                Header("小队爆破");
                var assigned=_catalog.Find<MapDefinition>(State.mapId);
                _content.Add(new Label(assigned ? "房间地图  "+assigned.DisplayName : "队长开赛时，战斗服务器将分配地图。"));
                return;
            }
            var modes=Row(); bool practice=_host.Mode==MatchMode.AimPractice;
            var range=_catalog.Entries.OfType<MapDefinition>().FirstOrDefault(m=>m.Practice && !m.Archived);
            var last=_host.LastBombMap ? _host.LastBombMap : _catalog.Entries.OfType<MapDefinition>().FirstOrDefault(m=>!m.Practice && !m.Archived);
            void RequestMap(MapDefinition map) { MapRequested?.Invoke(map); Render(); }
            Choice(modes,"Bot 爆破",!practice,()=>RequestMap(last),"mode-bots");
            if(range) Choice(modes,"练枪地图",practice,()=>RequestMap(range),"mode-practice");
            if(practice){_content.Add(new Label("靶位自动复位。按 B 可随时领取两方装备或切换移动靶。"));return;}
            Header("地图");var maps=Row();
            foreach(var map in _catalog.Entries.OfType<MapDefinition>().Where(m=>!m.Practice && !m.Archived))
                Choice(maps,map.DisplayName,map==_host.SelectedMap,()=>RequestMap(map));
            if(_host.Mode==MatchMode.Online){_content.Add(new Label("联机试玩 · 随机阵营"));return;}
            Header("开局阵营");var sides=Row();
            foreach(FirstSide side in Enum.GetValues(typeof(FirstSide)))Choice(sides,side==FirstSide.Attack?"余烬 · 先攻":side==FirstSide.Defend?"苍盾 · 先守":"随机",_host.OpeningSide==side,()=>{OpeningSideRequested?.Invoke(side);Render();});

        }

        void Inventory()
        {
            var owned = _catalog.Entries.Where(x => Owned(x) && !(x is MapDefinition || x is CharacterDefinition) &&
                !(x is OutfitDefinition fit && fit.Included && !fit.Model && fit.Character && fit.DisplayName==fit.Character.DisplayName)).ToArray();
            var title = Row();
            title.AddToClassList("inventory-title");
            var heading = new Label("库存一览");
            heading.AddToClassList("section-heading");
            title.Add(heading);
            title.Add(new Label($"共 {owned.Length} 件"));
            foreach (var group in owned.GroupBy(x=>x.Kind).OrderBy(g=>InventoryOrder(g.Key)))
            {
                Header(KindName(group.Key) + "    " + group.Count() + " 件");
                var row=Row(); row.AddToClassList("inventory-grid");
                foreach (var item in group) Tile(row,item,false,()=>Inspect(item),Art(item));
            }
            var scroll = (ScrollView)_content;
            scroll.schedule.Execute(()=>{ if (_section=="inventory" && !_item) scroll.scrollOffset=_inventoryScroll; });
        }

        void Inspect(ContentDefinition item)
        {
            _inventoryScroll=((ScrollView)_content).scrollOffset;
            _item=item;
            if (item is WeaponDefinition || item is WeaponSkinDefinition || item is UtilityDefinition)
                _host.Preview.ShowItem(item);
            else
            {
                var wear=_profile.Resolve(_profile.current,_catalog);
                if (item is CharacterDefinition character)
                {
                    var outfit=_catalog.Entries.OfType<OutfitDefinition>().FirstOrDefault(o=>o.Character==character && _profile.owned.Contains(o.Id));
                    if (outfit) wear.outfitId=outfit.Id;
                }
                else if (item is OutfitDefinition outfit) wear.outfitId=outfit.Id;
                else if (item is WearableDefinition garment)
                {
                    var fit=_catalog.Find<OutfitDefinition>(wear.outfitId);
                    if (!fit || !garment.Fits(fit.Character))
                        fit=_catalog.Entries.OfType<OutfitDefinition>().FirstOrDefault(o=>garment.Fits(o.Character) && _profile.owned.Contains(o.Id));
                    if (fit) wear.outfitId=fit.Id;
                    wear.worn.Set(garment.Slot,garment.Id);
                }
                _host.Preview.ShowWardrobe(wear,_gun,false,true);
            }
            Render();
        }
        CharacterDefinition CurrentCharacter=>_catalog.Find<OutfitDefinition>(_draft.outfitId)?.Character;
        void Skin(WeaponDefinition gun,string id)
        {
            _gun=gun.Id;_draft.skins.RemoveAll(c=>c.weaponId==gun.Id);
            if(!string.IsNullOrEmpty(id))_draft.skins.Add(new SkinChoice{weaponId=gun.Id,skinId=id});
            Preview();
        }
        void Header(string title){var label=new Label(title);label.AddToClassList("subhead");_content.Add(label);}
        VisualElement Row(){var row=new VisualElement();row.AddToClassList("choice-row");_content.Add(row);return row;}
        static void Choice(VisualElement row,string label,bool active,Action click,string name=null)
        {var button=new Button(click){text=label,name=name};button.EnableInClassList("selected",active);row.Add(button);}
        static Button Tile(VisualElement row,ContentDefinition item,bool active,Action click,Texture2D fallback=null)
        {
            var button=new Button(click) {name=item.Id};button.AddToClassList("item-tile");button.EnableInClassList("selected",active);
            ItemCardContent(button, item, fallback);
            row.Add(button);
            return button;
        }

        static void ItemCardContent(VisualElement card, ContentDefinition item, Texture2D fallback)
        {
            card.AddToClassList("item-" + item.Kind.ToString().ToLowerInvariant());
            var icon=item.Thumbnail?item.Thumbnail:fallback;
            if(icon)card.Add(new Image{image=icon,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore});
            var skin = item as WeaponSkinDefinition;
            card.Add(new Label(skin && skin.Weapon ? skin.Weapon.DisplayName : item.DisplayName) {pickingMode=PickingMode.Ignore});
            var detail=new Label(skin ? SkinTitle(skin) : ItemType(item)) {pickingMode=PickingMode.Ignore};
            detail.AddToClassList("item-detail");
            card.Add(detail);
        }
        void Slider(string title,float min,float max,float current,Action<float> change)
        {
            var field=new Slider(title,min,max){value=current,showInputField=true};field.AddToClassList("setting-field");
            field.RegisterValueChangedCallback(e=>change(e.newValue));_content.Add(field);
        }
    }
}
