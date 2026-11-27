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
        readonly LocalGame _game;
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
        public LobbyScreen(VisualElement host,MatchPresentation style,ContentCatalog catalog,LocalGame game)
        {
            _game=game;_catalog=catalog;style.lobby.CloneTree(host);
            _screen=host.Q("lobby");
            if (style.lobbyTheme) _screen.styleSheets.Add(style.lobbyTheme);
            _sheet=_screen.Q("sheet");_content=_screen.Q("sheet-content");
            _slots=_sheet.Q("loadout-slots"); _stage=_sheet.Q("inspection-stage");
            _stage.RegisterCallback<GeometryChangedEvent>(_=>FrameEquipment());
            void AttachTurntable(VisualElement stage)
            {
                stage.RegisterCallback<PointerDownEvent>(e=>{if(e.button==0)stage.CapturePointer(e.pointerId);});
                stage.RegisterCallback<PointerMoveEvent>(e=>{if(stage.HasPointerCapture(e.pointerId))game.RotateLobby(-e.deltaPosition.x*.38f);});
                stage.RegisterCallback<PointerUpEvent>(e=>stage.ReleasePointer(e.pointerId));
            }
            AttachTurntable(_screen.Q("stage-view")); AttachTurntable(_screen.Q("inspection-stage"));
            _pages = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["inventory"] = Inventory, ["equipment"] = Equipment, ["character"] = Characters,
                ["mode"] = ModeOptions, ["settings"] = Settings, ["tasks"] = Tasks, ["shop"] = Shop
            };
            BindNavigation();
            BindAccount();
            RefreshMap();
        }

        public void RefreshMap()
        {
            var map = _game.SelectedMap;
            bool party=State?.party is {Length:>1};
            if(party) map=_catalog.Find<MapDefinition>(State.mapId);
            _screen.Q<Label>("map-title").text = party
                ? (map ? map.DisplayName : "服务器选图")+"\n小队爆破"
                : map.DisplayName+"\n"+(_game.Mode==MatchMode.AimPractice ? "自由训练" : $"{LocalMatch.TeamSize}v{LocalMatch.TeamSize} 攻守拆弹");
            _screen.Q<Image>("map-thumb").image = map ? (map.LoadingArt ? map.LoadingArt : map.Radar) : null;
        }

        public void Show()
        {
            _profile = _game.ReadWardrobe();
            Close();
            SetVisible(_screen.Q("auth"), !_signedIn);
            if (_signedIn) RefreshHeader(true);
        }

        public void Back()
        {
            if (!_signedIn) return;
            if (_dialogKey != null) { HideDialog(); return; }
            if (_section == "inventory" && _item) { _item = null; _game.EndInspection(); Render(); }
            else Close();
        }

        public void Close()
        {
            if (_section == "equipment" || _section == "inventory" || _section == "character") _game.EndInspection();
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
            _profile = _game.ReadWardrobe();
            _draft = _profile.Resolve(_profile.current, _catalog);
            _gun = _game.LobbyWeaponId;
            SetVisible(_sheet,true);
            SetVisible(_screen.Q("home-heading"),false);
            SetVisible(_screen.Q("topbar"),key != "settings" && key != "tasks");
            if (key == "equipment") { _allEquipment=true; _equipment=0; _game.PreviewWardrobe(_draft,_gun,false); }
            if (key == "character") SelectCharacter(_catalog.Find<CharacterDefinition>(_game.Account.State?.showCharacterId) ?? CurrentCharacter);
            SelectTab(key);
            Render();
        }

        void Preview()
        {
            _draft=_profile.Resolve(_draft,_catalog);
            _game.PreviewWardrobe(_draft,_gun,_first);Render(); FrameEquipment();
        }
        void Apply()
        {
            _profile.current=_profile.Resolve(_draft,_catalog);
            PersistWardrobe();
        }

        void PersistWardrobe()
        {
            if (_game.Account.State == null)
            {
                _game.SetLobbyWeapon(_gun);
                _game.SaveWardrobe(_profile);
                Preview();
                return;
            }
            RunAction(() => _game.Account.SaveLoadoutAsync(_game.SerializeGear(_profile)), () =>
            {
                Notice("装备配置已保存");
                if (_section == "equipment") Preview();
            });
        }

        void Render()
        {
            _content.Clear();
            bool inspect = _section=="inventory" && _item;
            bool equipment = _section=="equipment", characters = _section=="character";
            _sheet.EnableInClassList("equipment-page", equipment);
            _sheet.EnableInClassList("character-page", characters);
            _sheet.EnableInClassList("inspect-page", inspect);
            _sheet.EnableInClassList("full-page", _section=="settings" || _section=="tasks");
            SetVisible(_sheet.Q("wear-rail"), equipment);
            SetVisible(_sheet.Q("character-rail"), characters);
            SetVisible(_stage,equipment || inspect || characters);
            _sheet.Q<Button>("close").text = inspect ? "‹  返回库存" : "‹  返回大厅";
            _sheet.Q<Label>("sheet-title").text = inspect ? _item.DisplayName : PageTitles[_section];
            SetVisible(_content,!inspect);
            SetVisible(_sheet.Q("inspect-caption"),inspect);
            if (!inspect) _pages[_section]();
        }

        void Settings()
        {
            GraphicsOptions(); Header("对局辅助");
            var friendly=new Toggle("队友三角与轮廓"){name="friendly-cues",value=_game.FriendlyCues};
            friendly.AddToClassList("setting-field");friendly.RegisterValueChangedCallback(e=>_game.FriendlyCues=e.newValue);_content.Add(friendly);
            Header("操控与声音");
            Slider("鼠标灵敏度",.025f,.30f,_game.Sensitivity,v=>_game.Sensitivity=v);
            Slider("音量",0,1,_game.Volume,v=>_game.Volume=v);Header("键鼠");
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
            var modes=Row(); bool practice=_game.Mode==MatchMode.AimPractice;
            var range=_catalog.Entries.OfType<MapDefinition>().FirstOrDefault(m=>m.Practice && !m.Archived);
            var last=_game.LastBombMap ? _game.LastBombMap : _catalog.Entries.OfType<MapDefinition>().FirstOrDefault(m=>!m.Practice && !m.Archived);
            Choice(modes,"Bot 爆破",!practice,()=>{_game.SelectMap(last);Render();},"mode-bots");
            if(range) Choice(modes,"练枪地图",practice,()=>{_game.SelectMap(range);Render();},"mode-practice");
            if(practice){_content.Add(new Label("靶位自动复位。按 B 可随时领取两方装备或切换移动靶。"));return;}
            Header("地图");var maps=Row();
            foreach(var map in _catalog.Entries.OfType<MapDefinition>().Where(m=>!m.Practice && !m.Archived))
                Choice(maps,map.DisplayName,map==_game.SelectedMap,()=>{_game.SelectMap(map);Render();});
            if(_game.Mode==MatchMode.Online){_content.Add(new Label("联机试玩 · 随机阵营"));return;}
            Header("开局阵营");var sides=Row();
            foreach(FirstSide side in Enum.GetValues(typeof(FirstSide)))Choice(sides,side==FirstSide.Attack?"余烬 · 先攻":side==FirstSide.Defend?"苍盾 · 先守":"随机",_game.OpeningSide==side,()=>{_game.OpeningSide=side;Render();});

        }

        void Inventory()
        {
            var owned = _catalog.Entries.Where(x => Owned(x) && !(x is MapDefinition) &&
                !(x is OutfitDefinition fit && fit.Included && !fit.Model && fit.Character && fit.DisplayName==fit.Character.DisplayName));
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
                _game.InspectItem(item);
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
                _game.PreviewWardrobe(wear,_gun,false,true);
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
        static void Tile(VisualElement row,ContentDefinition item,bool active,Action click,Texture2D fallback=null)
        {
            var button=new Button(click) {name=item.Id};button.AddToClassList("item-tile");button.EnableInClassList("selected",active);
            var icon=item.Thumbnail?item.Thumbnail:fallback;
            if(icon)button.Add(new Image{image=icon,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore});
            button.Add(new Label(item.DisplayName));
            var detail=new Label(ItemType(item));detail.AddToClassList("item-detail");button.Add(detail);row.Add(button);
        }
        void Slider(string title,float min,float max,float current,Action<float> change)
        {
            var field=new Slider(title,min,max){value=current,showInputField=true};field.AddToClassList("setting-field");
            field.RegisterValueChangedCallback(e=>change(e.newValue));_content.Add(field);
        }
    }
}
