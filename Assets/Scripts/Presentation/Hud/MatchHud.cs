using System;
using System.Collections.Generic;
using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed class MatchHud
    {
        private readonly ContentCatalog _catalog;
        private readonly MatchRules _rules;
        private readonly EliminationHud _kills;
        private readonly MatchPresentation _style;
        private readonly VisualElement _root, _shop, _offersRoot, _radar;
        private readonly Dictionary<string,Label> _labels = new Dictionary<string,Label>();
        private readonly List<(Button button,BuySlot slot,string id,int price)> _offers = new List<(Button,BuySlot,string,int)>();
        private readonly VisualElement[] _portraits, _friends;
        private readonly string[] _outfits;
        private readonly Dictionary<string,(VisualElement row,Image icon,Label key)> _slots=new();
        private readonly Label _me;
        private readonly List<Label> _sites=new List<Label>(2);
        private readonly VisualElement _enemy;
        private int _round = -1, _lastTick = -1;
        private bool _open, _paused, _movingTargets;
        private bool _practice, _canRestart;
        private IMatchWorldView _world;
        private bool _canManageRoom;
        private int _lastElimination, _lastWatched;
        private RoundPhase _lastPhase;
        private readonly Button _restart, _endRoom;
        private AnimeCS.Profile.WardrobeLoadout _loadout;
        private readonly Button _targetMotion;
        private readonly ScopeOverlay _scope;
        private readonly AimReticle _cross=new();
        private int _offerIndex;
        public bool ShopOpen => _open;

        internal event Action<BuySlot, string, bool> OrderRequested;
        internal event Action ResumeRequested, RestartRequested, LobbyRequested, EndRoomRequested, TargetMotionRequested;

        public MatchHud(VisualElement host, MatchPresentation style, ContentCatalog catalog, MatchRules rules, int teamSize)
        {
            _friends = new VisualElement[teamSize];
            _portraits = new VisualElement[teamSize*2];
            _outfits = new string[_portraits.Length];
            _catalog=catalog; _rules=rules; _style=style;
            style.hud.CloneTree(host);
            _root=host.Q("hud"); _shop=_root.Q("shop"); _offersRoot=_root.Q("offers"); _radar=_root.Q("radar");
            _root.Q("reticle").Add(_cross);
            _scope=new ScopeOverlay(); _root.Insert(_root.IndexOf(_root.Q("team-ids"))+1,_scope);
            _kills=new EliminationHud(_root,_catalog,teamSize);
            _targetMotion=new Button(()=>TargetMotionRequested?.Invoke()){name="target-motion"};
            _shop.Insert(_shop.IndexOf(_offersRoot),_targetMotion);
            foreach(var label in _root.Query<Label>().ToList()) if (!string.IsNullOrEmpty(label.name)) _labels[label.name]=label;
            foreach(var key in new[]{"primary","sidearm","knife","utility"}) _slots.Add(key,(_root.Q(key+"-slot"),_root.Q<Image>(key+"-icon"),_root.Q<Label>(key+"-key")));
            _root.Q<Button>("open-shop").clicked+=ToggleShop; _root.Q<Button>("close-shop").clicked+=ToggleShop;
            _restart=_root.Q<Button>("restart"); _restart.clicked+=()=>RestartRequested?.Invoke();
            _endRoom=_root.Q<Button>("end-room"); _endRoom.clicked+=()=>EndRoomRequested?.Invoke();
            _root.Q<Button>("resume").clicked+=()=>ResumeRequested?.Invoke();
            _root.Q<Button>("to-lobby").clicked+=()=>LobbyRequested?.Invoke();
            _root.Q("map-art").style.backgroundImage=catalog.Map.Radar;
            _radar.RegisterCallback<GeometryChangedEvent>(e => { if (Mathf.Abs(e.newRect.width-e.newRect.height)>.5f) _radar.style.height=e.newRect.width; });
            foreach (var site in new[]{Site.A,Site.B})
            {
                var mark=new Label(site.ToString()); mark.AddToClassList("radar-site"); _radar.Add(mark);
                _sites.Add(mark);Place(mark,catalog.Map.SitePosition(site));
            }
            for (int i=0;i<_friends.Length;i++) _friends[i]=Dot();
            _enemy=Dot();_enemy.AddToClassList("enemy");
            _me=new Label("▲");_me.AddToClassList("radar-dot");_me.AddToClassList("me");_radar.Add(_me);
            for (int i=0;i<_portraits.Length;i++)
            {
                var pic=new VisualElement();pic.AddToClassList("avatar");
                pic.EnableInClassList("foe",i>=teamSize);
                _root.Q(i<teamSize?"team":"rivals").Add(pic);_portraits[i]=pic;
            }
        }

        public void StartMatch(IMatchWorldView world, AnimeCS.Profile.WardrobeLoadout loadout, bool practice, bool canRestart, bool canManageRoom=false)
        {
            _round=-1; _lastTick=-1; _open=false; _loadout=loadout;
            _world=world; _practice=practice; _canRestart=canRestart;
            _canManageRoom=canManageRoom;
            SetRoomAction(false,null);
            _kills.Reset();
            Show(_root.Q("team"),!practice); Show(_root.Q("rivals"),!practice); Show(_root.Q(className:"score-line"),!practice);
            foreach(var site in _sites) Show(site,!practice);
            Show(_targetMotion,practice); _movingTargets=false; _targetMotion.text="靶位移动　关";
            _root.Q("map-art").style.backgroundImage=_catalog.Map.Radar;Place(_sites[0],_catalog.Map.SitePosition(Site.A));Place(_sites[1],_catalog.Map.SitePosition(Site.B));
            Array.Clear(_outfits,0,_outfits.Length);
            foreach(var portrait in _portraits) portrait.style.backgroundImage = StyleKeyword.None;
        }

        private void UpdatePortraits(IReadOnlyList<SeatView> seats,int localSeat)
        {
            for (int i=0;i<_portraits.Length;i++)
            {
                string outfit=seats[RosterSeat(i,localSeat,_friends.Length)].OutfitId;
                if(_outfits[i]==outfit) continue;
                _outfits[i]=outfit;
                var fit=_catalog.Find<OutfitDefinition>(outfit);
                _portraits[i].style.backgroundImage=fit?.Character?.Icon;
            }
        }

        public void ToggleShop() { _open=!_open; _lastTick=-1; }

        internal void SetRoomAction(bool busy,string message)
        {
            _restart.SetEnabled(!busy);
            _endRoom.SetEnabled(!busy);
            var feedback=_labels["room-feedback"];
            feedback.text=message;
            Show(feedback,!string.IsNullOrEmpty(message));
        }

        internal void ShowReconnecting(bool reconnecting)
        {
            string message=reconnecting ? "正在重新连接战斗服务器…" : string.Empty;
            _labels["action"].text=message;
            if (_paused)
            {
                var feedback=_labels["room-feedback"];
                feedback.text=message;
                Show(feedback,reconnecting);
            }
            if (reconnecting)
            {
                _open=false;
                Show(_shop,false);
                Show(_root.Q("open-shop"),false);
            }
            _lastTick=-1;
        }

        internal static int RosterSeat(int slot,int local,int teamSize)
        {
            int first=local/teamSize*teamSize;
            if(slot==0) return local;
            if(slot>=teamSize) return slot-first;
            int seat=first+slot-1;
            return seat<local ? seat : seat+1;
        }

        public void NavigateShop(int step, bool confirm, bool undo, bool canUndo)
        {
            if (!_open || _offers.Count == 0) return;
            if (step != 0)
            {
                _offerIndex = (_offerIndex + Math.Sign(step) + _offers.Count) % _offers.Count;
                if(_offersRoot.Contains(_offers[_offerIndex].button)) (_offersRoot as ScrollView)?.ScrollTo(_offers[_offerIndex].button);
                _lastTick = -1;
            }
            var offer = _offers[_offerIndex];
            if (undo && canUndo) OrderRequested?.Invoke(offer.slot, offer.id, true);
            else if (confirm && offer.button.enabledSelf) OrderRequested?.Invoke(offer.slot, offer.id, false);
        }

        public void Present(in SeatView self,in SeatView watched,IReadOnlyList<SeatView> seats,bool localMenu=false,bool movingTargets=false)
        {
            UpdatePortraits(seats,self.Seat);
            if (_movingTargets != movingTargets)
            {
                _movingTargets = movingTargets;
                _targetMotion.text = movingTargets ? "靶位移动　开" : "靶位移动　关";
            }
            var match=self.Match;
            bool menu=match.Paused || localMenu;
            int kills=_world.EliminationCount;
            if (_lastTick==match.Tick && _lastPhase==match.Phase && _paused==menu && _lastWatched==watched.Seat && _lastElimination==kills) return;
            _lastTick=match.Tick; _lastPhase=match.Phase; _paused=menu; _lastElimination=kills; _lastWatched=watched.Seat;
            bool practice=match.Phase==RoundPhase.Practice;
            bool buy=match.Phase==RoundPhase.Buy, finished=match.Phase==RoundPhase.Finished;
            if (match.Round!=_round) { _round=match.Round; _open=false; BuildShop(self.Side); }
            if (!self.CanBuy) _open=false;
            Show(_shop,self.CanBuy&&_open&&!_paused); Show(_root.Q("open-shop"),self.CanBuy&&!_open&&!_paused);
            if (self.CanBuy)
            {
                _root.Q<Button>("open-shop").text = practice ? "B  全装备" : buy ? "B  购买装备" : $"B  出生区购买  {Mathf.CeilToInt(self.BuyRemaining / (float)MatchRules.TickRate)}s";
                if (_open)
                {
                    _labels["shop-keys"].text = buy ? "点选购买    右键按住转头    Backspace 撤销" : "点选购买    右键按住转头";
                    UpdateOffers(self);
                }
            }
            Show(_root.Q("menu"),_paused||finished); Show(_root.Q("resume"),_paused&&!finished);
            Show(_restart,finished&&_canRestart);
            Show(_endRoom,_canManageRoom&&_paused&&!finished);
            bool teamZero=self.Seat<_friends.Length;
            int ourScore=teamZero?match.Score0:match.Score1, theirScore=teamZero?match.Score1:match.Score0;
            _labels["pause-title"].text=finished?(ourScore>theirScore?"我方获胜":"对方获胜"):localMenu?"对局菜单":"比赛已暂停";
            int seconds=Mathf.CeilToInt(match.RemainingTicks/(float)MatchRules.TickRate);
            _labels["clock"].text=practice?"练枪地图":$"{seconds/60:0}:{seconds%60:00}";
            _labels["our-score"].text=ourScore.ToString();_labels["their-score"].text=theirScore.ToString();
            _labels["phase"].text=practice?$"击倒 {match.PracticeKills}    爆头 {match.PracticeHeadshots}":buy?"冻结时间":match.Phase==RoundPhase.Planted?"已安装 · "+match.PlantedSite:
                match.Phase==RoundPhase.Settlement?(CharacterDefinition.FactionName(match.Winner)+"胜")+"  ·  "+ResultName(match.Reason):string.Empty;
            bool active=watched.Seat>=0;
            _labels["credits"].text=practice?"自由补给":"$ "+self.Credits;
            _labels["hp"].text=active?watched.Health+"  HP":"公共观战";
            _labels["armour"].text=(watched.Armor?"胸甲 ":"")+(watched.Helmet?"头盔 ":"")+(watched.HasKit?"拆除套件":"");
            _kills.Present(_world,match,watched.Seat,self.Seat,!_paused&&!finished&&!_open);
            _labels["objective"].text=watched.HasObjective?"5  目标包":"";
            _labels["ammo"].text=active && watched.WeaponId!=null && watched.Held!=HeldSlot.Knife?$"{watched.Magazine} / {watched.Reserve}":"";
            _labels["held-name"].text=watched.Held==HeldSlot.Objective?"目标包":watched.Held==HeldSlot.Utility?_catalog.Find<UtilityDefinition>(watched.UtilityId)?.DisplayName:active?_catalog.Find<WeaponDefinition>(watched.WeaponId)?.DisplayName:"";
            Slot("primary","1",watched.PrimaryId,watched.WeaponId);
            Slot("sidearm","2",watched.SidearmId,watched.WeaponId);
            Slot("knife","3",watched.KnifeId,watched.WeaponId);
            Slot("utility",watched.Utilities.Total>1?"4 ×"+watched.Utilities.Total:"4",watched.UtilityId, watched.Held==HeldSlot.Utility?watched.UtilityId:null);
            _labels["region"].text=active?_catalog.Map.NearestRegion(watched.Position):"";
            _labels["action"].text=watched.InteractionFraction>0?(match.Phase==RoundPhase.Planted?"拆除  ":"安装  ")+watched.InteractionFraction.ToString("P0"):
                self.Health<=0&&active?"观战  "+watched.DisplayId+"   ·   左 / 右键切换":"";
            var clue=watched.Heard;
            _labels["event"].text=clue.HasValue&&!clue.Value.Own&&match.Tick-clue.Value.Tick<45?
                (string.IsNullOrEmpty(clue.Value.Region)?"":clue.Value.Region+"  ·  ")+SoundName(clue.Value.Kind):"";
            bool aimVisible=active&&!_paused&&!finished&&!_open;
            _scope.Present(aimVisible && watched.ScopeLevel>0,watched.ScopeStability);
            var heldGun=_catalog.Find<WeaponDefinition>(watched.WeaponId);
            Show(_root.Q("reticle"),aimVisible && (!heldGun || heldGun.Optics==null));
            if(aimVisible && heldGun) _cross.SetMotion(watched.Motion,heldGun.RunSpeed);
            Show(_root.Q("obscured"),watched.Blinded||watched.InsideSmoke);
            _root.Q("obscured").EnableInClassList("flash",watched.Blinded);
            Show(_root.Q("hit-confirm"),aimVisible && _style.hitConfirm && match.Tick-watched.LastConfirmationTick<6);
            Show(_root.Q("wound"),active&&_style.hitReaction&&match.Tick-watched.LastHitTick<7);
            for (int i=0;i<_friends.Length;i++)
            {
                var ally=seats[RosterSeat(i,self.Seat,_friends.Length)];
                _portraits[i].EnableInClassList("out", ally.Health<=0);
                Place(_friends[i],ally.Position); Show(_friends[i],active&&ally.Health>0&&ally.Seat!=watched.Seat);
            }
            for(int i=_friends.Length;i<_portraits.Length;i++) _portraits[i].EnableInClassList("out",seats[RosterSeat(i,self.Seat,_friends.Length)].Health<=0);
            Show(_me,active); if(active) { Place(_me,watched.Position); _me.style.rotate=new Rotate(new Angle(watched.Yaw+_catalog.Map.RadarYaw)); }
            bool contact=active&&!watched.Blinded&&watched.VisibleEnemy.HasValue;
            Show(_enemy,contact); if(contact) Place(_enemy,watched.VisibleEnemy.Value.Position);
        }
        private void Slot(string label,string key,string item,string equipped)
        {
            var name=_catalog.Find<ContentDefinition>(item);
            var slot=_slots[label];Show(slot.row,name);if(!name)return;
            bool held=item==equipped;
            slot.icon.image=name.Glyph;slot.key.text=name.Glyph?key:key+"  "+name.DisplayName;
            slot.icon.tintColor=held?new Color(1,.88f,.58f,.98f):new Color(.95f,.96f,.92f,.84f);
            slot.row.EnableInClassList("equipped",held);
        }
        private static string ResultName(WinReason reason) => reason switch
        { WinReason.Defused => "装置已拆除", WinReason.DeviceExpired => "装置已引爆", WinReason.TimeExpired => "时间耗尽", _ => "全歼" };
        private static string SoundName(SoundKind kind) => kind switch
        { SoundKind.Step=>"脚步",SoundKind.Gun=>"枪声",SoundKind.Objective=>"装置声",SoundKind.DevicePulse=>"装置提示音",SoundKind.DeviceBurst=>"装置引爆",SoundKind.EchoTail=>"电子尾音",_=>"道具声" };
        private VisualElement Dot() { var dot=new VisualElement();dot.AddToClassList("radar-dot");_radar.Add(dot);return dot; }
        private void Place(VisualElement mark,Vector3 where)
        { var p=_catalog.Map.RadarPoint(where);mark.style.left=Length.Percent(Mathf.Clamp01(p.x)*100);mark.style.top=Length.Percent(Mathf.Clamp01(p.y)*100); }
        private static void Show(VisualElement e,bool show) => e.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
        private void BuildShop(Side side)
        {
            _offersRoot.Clear();_offers.Clear();_offerIndex=0;
            var protection=_shop.Q("protection-offers"); protection.Clear();
            bool practice=_practice;
            foreach(var slot in new[]{BuySlot.Sidearm,BuySlot.Primary,BuySlot.Knife,BuySlot.Utility,BuySlot.Armor})
            {
                if(slot==BuySlot.Knife && !practice) continue;
                if (slot==BuySlot.Armor)
                {
                    Offer(protection,"胸甲",_rules.armorPrice,slot,null);
                    Offer(protection,"头盔",_rules.helmetPrice,BuySlot.Helmet,null);
                    if(side==Side.Defend || practice) Offer(protection,"拆除套件",_rules.kitPrice,BuySlot.Kit,null);
                    continue;
                }
                string name=slot==BuySlot.Knife?"近战":slot==BuySlot.Sidearm?"手枪":slot==BuySlot.Primary?"主武器":slot==BuySlot.Utility?"道具":"防护";
                var label=new Label(name);label.AddToClassList("subhead");_offersRoot.Add(label);
                var row=new VisualElement();row.AddToClassList("offers");_offersRoot.Add(row);
                foreach(var entry in _catalog.Entries)
                {
                    if (entry.Archived) continue;
                    if (entry is WeaponDefinition gun && (practice || gun.Class!=WeaponClass.Knife && gun.Price>0 && gun.CanBuy(side)) &&
                        slot==(gun.Class==WeaponClass.Knife?BuySlot.Knife:gun.Class==WeaponClass.Pistol?BuySlot.Sidearm:BuySlot.Primary)) Offer(row,gun.DisplayName,gun.Price,slot,gun.Id,side);
                    if (entry is UtilityDefinition util && slot==BuySlot.Utility) Offer(row,util.DisplayName,util.Price,slot,util.Id);
                }
            }
        }
        private void Offer(VisualElement row,string text,int price,BuySlot slot,string id,Side side=Side.Attack)
        {
            var button=new Button(()=>OrderRequested?.Invoke(slot,id,false)){name=id ?? slot.ToString(),focusable=false};
            bool free=_practice;
            var gun=_catalog.Find<WeaponDefinition>(id);
            var gear = _loadout?.GearFor(side);
            bool favorite = gun && (gear != null ? gear.Weapon(gun.Class)==id : id==_loadout?.primaryId || id==_loadout?.sidearmId);
            if (gun && gun.Glyph)
            {
                button.AddToClassList("shop-weapon");
                button.Add(new Image{image=gun.Glyph,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore});
                var caption=new VisualElement{pickingMode=PickingMode.Ignore};caption.AddToClassList("offer-caption");
                caption.Add(new Label((favorite?"★ ":"")+text){pickingMode=PickingMode.Ignore});
                if (!free) { var cost=new Label(price.ToString()){pickingMode=PickingMode.Ignore};cost.AddToClassList("offer-price");caption.Add(cost); }
                button.Add(caption);
            }
            else button.text=free?text:$"{text}   {price}";
            button.tooltip=favorite?text+" · 装备页已选":text;
            row.Add(button);_offers.Add((button,slot,id,price));
        }
        private void UpdateOffers(in SeatView self)
        {
            for (int i = 0; i < _offers.Count; i++)
            {
                var offer = _offers[i];
                offer.button.EnableInClassList("shop-focus", i == _offerIndex);
                var draft=self.Draft;
                if(offer.slot==BuySlot.Utility && _catalog.Find<UtilityDefinition>(offer.id) is UtilityDefinition util)
                {
                    int count=self.Utilities.Count(util.Effect);
                    bool free=_practice;
                    offer.button.text=util.DisplayName+(count>0?"  ×"+count:"")+(free?"":"   "+util.Price);
                    offer.button.EnableInClassList("chosen",draft.Utilities.Count(util.Effect)>0);
                    offer.button.SetEnabled(self.Utilities.CanAdd(util.Effect) && (free || self.Credits>=util.Price));
                    continue;
                }
                string pending=offer.slot switch {BuySlot.Primary=>draft.Primary,BuySlot.Sidearm=>draft.Sidearm,BuySlot.Utility=>draft.Utility,_=>null};
                bool chosen=offer.slot switch {BuySlot.Kit=>draft.Kit,BuySlot.Armor=>draft.Armor,BuySlot.Helmet=>draft.Helmet,_=>offer.id!=null && pending==offer.id};
                var current=_catalog.Find<ContentDefinition>(pending);
                int credit=chosen?offer.price:current is WeaponDefinition gun?gun.Price:current is UtilityDefinition utility?utility.Price:0;
                offer.button.EnableInClassList("chosen",chosen);
                bool owned = offer.slot switch { BuySlot.Kit => self.HasKit, BuySlot.Armor => self.Armor, BuySlot.Helmet => self.Helmet, BuySlot.Primary => self.PrimaryId==offer.id,
                    BuySlot.Sidearm => self.SidearmId==offer.id, BuySlot.Knife => self.KnifeId==offer.id, BuySlot.Utility => self.UtilityId==offer.id, _ => false };
                if (_practice)
                {
                    offer.button.EnableInClassList("chosen",owned);
                    offer.button.SetEnabled(!owned || offer.slot==BuySlot.Primary || offer.slot==BuySlot.Sidearm || offer.slot==BuySlot.Knife);
                    continue;
                }
                offer.button.SetEnabled(!chosen && !owned && offer.price<=self.Credits+credit);
            }
        }
    }
}
