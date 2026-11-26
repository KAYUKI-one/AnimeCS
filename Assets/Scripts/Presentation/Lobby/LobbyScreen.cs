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
            _screen=host.Q("lobby");_sheet=_screen.Q("sheet");_content=_screen.Q("sheet-content");
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
                ["inventory"] = Inventory, ["equipment"] = Equipment,
                ["mode"] = ModeOptions, ["settings"] = Settings
            };
            foreach(var key in _pages.Keys)_screen.Q<Button>(key).clicked+=()=>Open(key);
            _screen.Q<Button>("start").clicked+=()=>{Close();game.Restart();};_screen.Q<Button>("close").clicked+=Back;
            RefreshMap();
        }
        public void RefreshMap()=>_screen.Q<Label>("map-title").text=_game.SelectedMap.DisplayName+(_game.Mode==MatchMode.AimPractice?"   /   自由训练":"   /   本地 5v5");
        public void Show(){_profile=_game.ReadWardrobe();Close();}
        public void Back()
        {
            if (_section=="inventory" && _item) { _item=null; _game.EndInspection(); Render(); }
            else Close();
        }
        public void Close()
        {
            if(_section=="equipment"||_section=="inventory")_game.EndInspection();
            _section=null;_draft=null;_item=null;_first=false;_sheet.style.display=DisplayStyle.None;
            _screen.Q("home-heading").style.display=DisplayStyle.Flex;
            foreach(var button in _screen.Query<Button>(className:"nav").ToList())button.RemoveFromClassList("active");
        }
        void Open(string key)
        {
            Close();_section=key;_profile=_game.ReadWardrobe();
            _draft=_profile.Resolve(_profile.current,_catalog);_gun=_game.LobbyWeaponId;
            _sheet.style.display=DisplayStyle.Flex;_screen.Q("home-heading").style.display=DisplayStyle.None;
            if (key=="equipment") { _equipment=0; _game.PreviewWardrobe(_draft,_gun,false); }
            var button=_screen.Q<Button>(key);button.AddToClassList("active");
            _sheet.Q<Label>("sheet-title").text=button.text;Render();
        }
        void Preview()
        {
            _draft=_profile.Resolve(_draft,_catalog);
            _game.PreviewWardrobe(_draft,_gun,_first);Render(); FrameEquipment();
        }
        void Apply()
        {
            _profile.current=_profile.Resolve(_draft,_catalog);
            _game.SetLobbyWeapon(_gun);_game.SaveWardrobe(_profile); Preview();
        }
        void Render()
        {
            _content.Clear();
            bool inspect = _section=="inventory" && _item;
            bool equipment = _section=="equipment";
            _sheet.EnableInClassList("equipment-page", equipment);
            _sheet.EnableInClassList("inspect-page", inspect);
            _sheet.Q("inspection-stage").style.display=equipment||inspect?DisplayStyle.Flex:DisplayStyle.None;
            _sheet.Q<Button>("close").text=inspect?"返回库存":"返回大厅";
            _sheet.Q<Label>("sheet-title").text=inspect?_item.DisplayName:_screen.Q<Button>(_section).text;
            _content.style.display=inspect?DisplayStyle.None:DisplayStyle.Flex;
            _sheet.Q("inspect-caption").style.display=inspect?DisplayStyle.Flex:DisplayStyle.None;
            if (inspect) return;
            _pages[_section]();
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
            var row=Row(); row.AddToClassList("inventory-grid");
            foreach(var item in _catalog.Entries.Where(x=>x && !x.Archived && _profile.owned.Contains(x.Id)))
            {
                if (item is MapDefinition) continue;
                if (item is OutfitDefinition baseFit && baseFit.Included && !baseFit.Model && baseFit.Character &&
                    baseFit.DisplayName==baseFit.Character.DisplayName && _profile.owned.Contains(baseFit.Character.Id)) continue;
                Texture2D fallback = item switch
                {
                    CharacterDefinition character => character.Icon, OutfitDefinition outfit => outfit.Character?.Icon,
                    WeaponSkinDefinition skin => skin.Weapon?.Thumbnail, _ => null
                };
                Tile(row,item,false,()=>Inspect(item),fallback);
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
            button.Add(new Label(item.DisplayName));row.Add(button);
        }
        void Slider(string title,float min,float max,float current,Action<float> change)
        {
            var field=new Slider(title,min,max){value=current,showInputField=true};field.AddToClassList("setting-field");
            field.RegisterValueChangedCallback(e=>change(e.newValue));_content.Add(field);
        }
    }
}
