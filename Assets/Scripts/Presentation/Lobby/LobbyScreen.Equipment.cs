using System;
using System.Linq;
using AnimeCS.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed partial class LobbyScreen
    {
        bool _allEquipment;
        static readonly string[] Slots={"角色","主武器","副武器","近战","头饰","上衣","手套","下衣","鞋子","预设"};

        void FrameEquipment()
        {
            if ((_section=="equipment" || _section=="character") && _screen.worldBound.width>1 && _stage.worldBound.width>1)
                _game.CenterPreview((_stage.worldBound.center.x-_screen.worldBound.xMin)/_screen.worldBound.width);
        }

        string WeaponSlot(int slot) => slot switch {1=>_draft.primaryId,2=>_draft.sidearmId,3=>_draft.knifeId,_=>null};
        ContentDefinition Equipped(int slot) => slot==0 ? _catalog.Find<OutfitDefinition>(_draft.outfitId) :
            slot<=3 ? _catalog.Find<WeaponDefinition>(WeaponSlot(slot)) : slot<=8 ? _catalog.Find<WearableDefinition>(_draft.worn.Get((WearSlot)(slot-4))) : null;

        void Equipment()
        {
            _slots.Clear();
            var wearRail = _sheet.Q<ScrollView>("wear-rail"); wearRail.Clear();
            SectionLabel(_slots,"武器配置"); SectionLabel(wearRail,"服装部位");
            _sheet.Q<Label>("stage-name").text = CurrentCharacter ? CurrentCharacter.DisplayName : "拖动旋转";
            for (int i=0;i<Slots.Length;i++)
            {
                int slot=i; var current=Equipped(slot);
                var button=new Button(()=>
                {
                    _equipment=slot;_allEquipment=false;
                    if (slot>=1 && slot<=3 && WeaponSlot(slot)!=null) _gun=WeaponSlot(slot);
                    Preview();
                }){name="equip-slot-"+slot};
                button.AddToClassList("mount-slot"); button.EnableInClassList("selected",_equipment==slot);
                var icon=current ? current.Thumbnail : null;
                if (!icon && current is OutfitDefinition f) icon=f.Character?.Icon;
                if (icon) button.Add(new Image{image=icon,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore});
                var text=new VisualElement{pickingMode=PickingMode.Ignore};
                var kind=new Label(Slots[slot]); kind.AddToClassList("slot-kind"); text.Add(kind);
                if(slot<9) text.Add(new Label(current?current.DisplayName:slot==4?"未佩戴":"角色原装"));
                button.Add(text); (slot >= 4 && slot <= 8 ? (VisualElement)wearRail : _slots).Add(button);
            }
            var controls=_sheet.Q("loadout-actions"); controls.Clear();
            Choice(controls,_first?"看全身":"看手部",false,()=>{_first=!_first;Preview();},"equip-view");
            Choice(controls,"应用搭配",true,Apply,"apply-loadout");
            var heading=new Label("可装备库存"); heading.AddToClassList("bag-title"); _content.Add(heading);
            var filters=Row();
            Choice(filters,"全部适用",_allEquipment,()=>{_allEquipment=true;Render();},"equip-all");
            Choice(filters,"当前槽位",!_allEquipment,()=>{_allEquipment=false;Render();},"equip-current");
            if (_allEquipment) { AllEquipment(); return; }
            Header(Slots[_equipment]);
            if(_equipment==9)
            {
                for(int i=0;i<2;i++)
                {
                    int index=i; var saved=_profile.presets[i]; var row=Row();
                    Choice(row,"穿戴预设 "+(i+1),false,()=>{_draft=_profile.Resolve(saved,_catalog);_gun=_draft.primaryId;Preview();});
                    Choice(row,"保存到 "+(i+1),false,()=>{_profile.presets[index]=_profile.Resolve(_draft,_catalog);PersistWardrobe();});
                }
                return;
            }
            var bag=Row();bag.name="backpack-items";
            if (_equipment==0)
            {
                var faction=CurrentCharacter ? CurrentCharacter.Faction : Side.Attack;
                var tabs=new VisualElement();tabs.AddToClassList("choice-row");_content.Insert(_content.IndexOf(bag),tabs);
                foreach (Side side in new[]{Side.Attack,Side.Defend})
                    Choice(tabs,CharacterDefinition.FactionName(side),faction==side,()=>{
                        var selected=_catalog.Entries.OfType<OutfitDefinition>().FirstOrDefault(o=>Owned(o) && o.Character && o.Character.Faction==side && o.Id==_draft.OutfitFor(side)) ?? _catalog.Entries.OfType<OutfitDefinition>().FirstOrDefault(o=>Owned(o) && o.Character && o.Character.Faction==side); if(selected)_draft.Select(selected); Preview();
                    },side==Side.Attack?"faction-attack":"faction-defend");
                foreach(var f in _catalog.Entries.OfType<OutfitDefinition>().Where(o=>Owned(o) && o.Character && o.Character.Faction==faction))
                    Tile(bag,f,_draft.outfitId==f.Id,()=>{_draft.Select(f);Preview();},f.Character.Icon);
            }
            else if(_equipment<=3)
            {
                foreach(var w in _catalog.Entries.OfType<WeaponDefinition>().Where(o=>Owned(o)))
                {
                    bool fits=_equipment==1?w.Class!=WeaponClass.Pistol&&w.Class!=WeaponClass.Knife:_equipment==2?w.Class==WeaponClass.Pistol:w.Class==WeaponClass.Knife;
                    if (!fits) continue;
                    Tile(bag,w,WeaponSlot(_equipment)==w.Id,()=>{EquipWeapon(w,_equipment);Preview();});
                }
                var gun=_catalog.Find<WeaponDefinition>(WeaponSlot(_equipment));
                if(!gun) return;
                var skins=_catalog.Entries.OfType<WeaponSkinDefinition>().Where(s=>s.Weapon==gun && s!=gun.DefaultSkin && Owned(s)).ToArray();
                if(skins.Length==0)return;
                Header("涂装");var finishes=Row();Choice(finishes,gun.DefaultSkin?gun.DefaultSkin.DisplayName:"原厂",string.IsNullOrEmpty(_draft.SkinFor(gun.Id)),()=>Skin(gun,null));
                foreach(var skin in skins) Tile(finishes,skin,_draft.SkinFor(gun.Id)==skin.Id,()=>Skin(gun,skin.Id),gun.Thumbnail);
            }
            else
            {
                var slot=(WearSlot)(_equipment-4);
                Choice(bag,slot==WearSlot.Headwear?"取下头饰":"角色原装",string.IsNullOrEmpty(_draft.worn.Get(slot)),()=>{_draft.worn.Set(slot,null);Preview();});
                foreach(var item in _catalog.Entries.OfType<WearableDefinition>().Where(w=>w.Slot==slot&&w.Fits(CurrentCharacter)&&Owned(w)))
                    Tile(bag,item,_draft.worn.Get(slot)==item.Id,()=>{_draft.worn.Set(slot,item.Id);Preview();});
            }
        }

        void AllEquipment()
        {
            var bag=Row();bag.name="backpack-items";
            var character=CurrentCharacter;
            foreach(var item in _catalog.Entries.Where(Owned).OrderBy(i=>InventoryOrder(i.Kind)))
            {
                if (item is WeaponDefinition weapon)
                {
                    int mount=weapon.Class==WeaponClass.Knife?3:weapon.Class==WeaponClass.Pistol?2:1;
                    Tile(bag,weapon,WeaponSlot(mount)==weapon.Id,()=>{EquipWeapon(weapon,mount);Preview();});
                }
                else if (item is WeaponSkinDefinition skin && skin.Weapon)
                {
                    int mount=skin.Weapon.Class==WeaponClass.Knife?3:skin.Weapon.Class==WeaponClass.Pistol?2:1;
                    Tile(bag,skin,_draft.SkinFor(skin.Weapon.Id)==skin.Id,()=>{EquipWeapon(skin.Weapon,mount);Skin(skin.Weapon,skin.Id);},Art(skin));
                }
                else if (item is WearableDefinition garment && garment.Fits(character))
                    Tile(bag,garment,_draft.worn.Get(garment.Slot)==garment.Id,()=>{_draft.worn.Set(garment.Slot,garment.Id);Preview();},Art(garment));
                else if (item is OutfitDefinition outfit && outfit.Character)
                    Tile(bag,outfit,_draft.outfitId==outfit.Id,()=>{_draft.Select(outfit);Preview();},outfit.Character.Icon);
            }
        }

        void EquipWeapon(WeaponDefinition weapon,int mount)
        {
            if (mount==1) _draft.primaryId=weapon.Id;
            else if (mount==2) _draft.sidearmId=weapon.Id;
            else _draft.knifeId=weapon.Id;
            _gun=weapon.Id;
        }

        bool Owned(ContentDefinition item) => item && !item.Archived && _profile.owned.Contains(item.Id);
    }
}
