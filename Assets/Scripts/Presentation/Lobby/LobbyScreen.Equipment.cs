using System.Linq;
using AnimeCS.Content;
using AnimeCS.Profile;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed partial class LobbyScreen
    {
        static readonly WeaponClass[] WeaponMounts =
        {
            WeaponClass.Knife, WeaponClass.Pistol, WeaponClass.Smg, WeaponClass.Rifle, WeaponClass.LightSniper
        };
        static readonly string[] WeaponLabels = { "近战刀具", "手枪", "冲锋枪", "步枪", "狙击枪" };
        static readonly string[] WearIcons = { "sparkles", "shirt", "hand", "layers", "footprints" };
        const int RifleMount = 3;
        const int WearOffset = 5;

        readonly EquipmentDrag<ContentDefinition> _drag;
        Side _equipmentSide;
        bool _allEquipment, _showPresets;
        WardrobeSideGear ActiveGear => _draft.GearFor(_equipmentSide);

        void FrameEquipment()
        {
            if ((_section == "equipment" || _section == "character") && _screen.worldBound.width > 1 && _stage.worldBound.width > 1)
                _host.Preview.Center((_stage.worldBound.center.x - _screen.worldBound.xMin) / _screen.worldBound.width,
                    showcase: true, equipment: _section == "equipment");
        }

        void BeginEquipment()
        {
            _equipmentSide = CurrentCharacter ? CurrentCharacter.Faction : Side.Attack;
            _allEquipment = true;
            _showPresets = false;
            _equipment = RifleMount;
            _gun = ActiveGear.Weapon(WeaponClass.Rifle);
            _host.Preview.ShowWardrobe(_draft, _gun, false, previewWorn: ActiveGear.worn);
        }

        void SelectEquipmentSide(Side side)
        {
            var outfit = _catalog.Find<OutfitDefinition>(_draft.OutfitFor(side));
            if (!outfit || !Owned(outfit)) return;
            _equipmentSide = side;
            _draft.Select(outfit);
            _equipment = RifleMount;
            _gun = ActiveGear.Weapon(WeaponClass.Rifle);
            ((ScrollView)_content).scrollOffset = Vector2.zero;
            Preview();
        }

        void Equipment()
        {
            _slots.Clear();
            var wearRail = _sheet.Q<ScrollView>("wear-rail");
            wearRail.Clear();
            SectionLabel(_slots, "武器配置");
            SectionLabel(wearRail, "服装部位");
            for (int i = 0; i < WeaponMounts.Length; i++) EquipmentSlot(_slots, i, WeaponLabels[i]);
            for (int i = 0; i < SlotNames.Length; i++) EquipmentSlot(wearRail, WearOffset + i, SlotNames[i]);

            var centerControls = _sheet.Q("equipment-stage-controls");
            centerControls.Clear();
            var sideChoices = new VisualElement();
            sideChoices.AddToClassList("faction-switch");
            centerControls.Add(sideChoices);
            foreach (var side in new[] { Side.Attack, Side.Defend })
            {
                string role = side == Side.Attack ? "攻方" : "守方";
                Choice(sideChoices, CharacterDefinition.FactionName(side) + " · " + role,
                    side == _equipmentSide, () => SelectEquipmentSide(side),
                    side == Side.Attack ? "faction-attack" : "faction-defend");
                var button = sideChoices.Children().Last();
                button.AddToClassList(side == Side.Attack ? "faction-attack" : "faction-defend");
                button.SetEnabled(!string.IsNullOrEmpty(_draft.OutfitFor(side)));
            }
            var outfits = _catalog.Entries.OfType<OutfitDefinition>()
                .Where(o => Owned(o) && o.Character && o.Character.Faction == _equipmentSide).ToList();
            int index = outfits.FindIndex(o => o.Id == _draft.outfitId);
            if (index >= 0)
            {
                var picker = new PopupField<OutfitDefinition>(outfits, index, o => o.DisplayName, o => o.DisplayName)
                    { name = "equipment-character", tooltip = "更换当前阵营的角色服装" };
                picker.RegisterValueChangedCallback(evt => { _draft.Select(evt.newValue); Preview(); });
                centerControls.Add(picker);
            }

            EquipmentPreview();

            var toolbar = _sheet.Q("equipment-toolbar");
            toolbar.Clear();
            var allowed = _catalog.Entries.Select(item => (item, slot: ValidSlot(item))).Where(choice => choice.slot >= 0);
            var choices = (_allEquipment ? allowed : allowed.Where(choice => choice.slot == _equipment)).ToArray();
            var heading = new VisualElement();
            heading.AddToClassList("equipment-heading");
            SectionLabel(heading, _showPresets ? "装备预设" : "可装配库存");
            if (!_showPresets) heading.Add(new Label($"{choices.Length} 件") { name = "equipment-count" });
            toolbar.Add(heading);
            var filters = new VisualElement();
            filters.AddToClassList("equipment-filters");
            toolbar.Add(filters);
            Choice(filters, "全部适用", _allEquipment && !_showPresets, () => SetEquipmentFilter(true), "equip-all");
            Choice(filters, "当前槽位", !_allEquipment && !_showPresets, () => SetEquipmentFilter(false), "equip-current");
            Choice(filters, "预设", _showPresets, () =>
            {
                _showPresets = true;
                ((ScrollView)_content).scrollOffset = Vector2.zero;
                Render();
            }, "equip-presets");
            if (!_showPresets)
            {
                var hint = new Label("长按拖到左侧，或点击装备") { name = "equipment-hint" };
                filters.Add(hint);
            }

            var controls = _sheet.Q("loadout-actions");
            controls.Clear();
            Choice(controls, _first ? "看全身" : "看手部", false, () => { _first = !_first; Preview(); }, "equip-view");
            Choice(controls, "应用配置", false, Apply, "apply-loadout");
            controls.Q<Button>("apply-loadout").AddToClassList("accent");

            if (_showPresets)
            {
                for (int i = 0; i < _profile.presets.Length; i++)
                {
                    int preset = i;
                    var row = Row();
                    Choice(row, $"穿戴预设 {i + 1}", false, () =>
                    {
                        _draft = _profile.Resolve(_profile.presets[preset], _catalog);
                        _equipmentSide = CurrentCharacter ? CurrentCharacter.Faction : Side.Attack;
                        _gun = ActiveGear.Weapon(WeaponClass.Rifle);
                        Preview();
                    });
                    Choice(row, $"保存到 {i + 1}", false, () =>
                    {
                        _profile.presets[preset] = _profile.Resolve(_draft, _catalog);
                        PersistWardrobe();
                    });
                }
                return;
            }

            if (!_allEquipment && _equipment >= WearOffset && ActiveGear.worn.Get((WearSlot)(_equipment - WearOffset)) != null)
                Choice(Row(), "恢复角色原装", false, () =>
                {
                    ActiveGear.worn.Set((WearSlot)(_equipment - WearOffset), null);
                    Preview();
                }, "clear-wear");

            var bag = Row();
            bag.name = "backpack-items";
            bag.AddToClassList("equipment-grid");
            if (choices.Length == 0)
                bag.Add(new Label("此槽位暂无可用物品") { name = "equipment-empty" });
            foreach (var (item, slot) in choices)
            {
                bool equipped = IsEquipped(item, slot);
                var button = Tile(bag, item, slot == _equipment && equipped, null, Art(item));
                button.tooltip = item.DisplayName + (equipped ? "（已装备）" : "");
                _drag.Source(button, item, slot);
            }
        }

        void EquipmentPreview()
        {
            var preview = _sheet.Q("equipment-preview");
            preview.Clear();
            preview.tooltip = "拖动角色旋转";
            var gun = _catalog.Find<WeaponDefinition>(_gun);
            var skin = gun ? _catalog.Find<WeaponSkinDefinition>(_draft.SkinFor(gun.Id)) : null;
            bool wearSlot = _equipment >= WearOffset;
            var item = wearSlot ? Equipped(_equipment) : skin ? (ContentDefinition)skin : gun;
            AddMountArt(preview, item, wearSlot ? _equipment : -1);

            var details = new VisualElement { pickingMode = PickingMode.Ignore };
            details.AddToClassList("mount-copy");
            var caption = wearSlot ? SlotNames[_equipment - WearOffset] : _first ? "手部展示" : "持枪展示";
            if (!wearSlot && skin) caption += " · " + SkinTitle(skin);
            details.Add(new Label(caption) { name = "equipment-preview-caption", pickingMode = PickingMode.Ignore });
            string name = wearSlot ? MountName(item, _equipment) : gun ? gun.DisplayName : "未选择";
            var title = new Label(name) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("mount-title");
            details.Add(title);
            preview.Add(details);
        }

        void SetEquipmentFilter(bool all)
        {
            _allEquipment = all;
            _showPresets = false;
            ((ScrollView)_content).scrollOffset = Vector2.zero;
            Render();
        }

        void EquipmentSlot(VisualElement rail, int slot, string title)
        {
            var item = Equipped(slot);
            var button = new Button(() =>
            {
                _equipment = slot;
                _allEquipment = false;
                _showPresets = false;
                ((ScrollView)_content).scrollOffset = Vector2.zero;
                if (slot < WearOffset && item is WeaponDefinition weapon) _gun = weapon.Id;
                Preview();
            }) { name = "equip-slot-" + slot };
            button.AddToClassList("mount-slot");
            button.EnableInClassList("selected", _equipment == slot);
            AddMountArt(button, item, slot);
            var details = new VisualElement { pickingMode = PickingMode.Ignore };
            details.AddToClassList("mount-copy");
            var kind = new Label(title) { pickingMode = PickingMode.Ignore };
            kind.AddToClassList("slot-kind");
            details.Add(kind);
            var name = new Label(MountName(item, slot)) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("mount-title");
            details.Add(name);
            if (item is WeaponDefinition gun && _catalog.Find<WeaponSkinDefinition>(_draft.SkinFor(gun.Id)) is { } skin && skin)
            {
                var finish = new Label(SkinTitle(skin)) { pickingMode = PickingMode.Ignore };
                finish.AddToClassList("item-detail");
                details.Add(finish);
            }
            button.Add(details);
            rail.Add(button);
            _drag.Slot(button, slot);
        }

        static string MountName(ContentDefinition item, int slot) =>
            item ? item.DisplayName : slot == WearOffset ? "未佩戴" : slot < WearOffset ? "未选择" : "角色原装";

        static void AddMountArt(VisualElement parent, ContentDefinition item, int slot)
        {
            var art = new Image { image = Art(item), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            art.AddToClassList("mount-art");
            parent.Add(art);
            if (art.image || slot < WearOffset) return;
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("wear-glyph");
            icon.AddToClassList("wear-" + WearIcons[slot - WearOffset]);
            art.Add(icon);
        }

        ContentDefinition Equipped(int slot) => slot < WearOffset
            ? _catalog.Find<WeaponDefinition>(ActiveGear.Weapon(WeaponMounts[slot]))
            : _catalog.Find<WearableDefinition>(ActiveGear.worn.Get((WearSlot)(slot - WearOffset)));

        int ValidSlot(ContentDefinition item)
        {
            if (!_profile.CanEquip(item, _equipmentSide, CurrentCharacter)) return -1;
            if (item is WearableDefinition wear) return WearOffset + (int)wear.Slot;
            var gun = item is WeaponSkinDefinition skin ? skin.Weapon : item as WeaponDefinition;
            return System.Array.IndexOf(WeaponMounts, WardrobeSideGear.Category(gun.Class));
        }

        bool IsEquipped(ContentDefinition item, int slot)
        {
            if (item is WeaponSkinDefinition skin)
                return Equipped(slot) == skin.Weapon && _draft.SkinFor(skin.Weapon.Id) == skin.Id;
            if (item is WeaponDefinition gun && !string.IsNullOrEmpty(_draft.SkinFor(gun.Id))) return false;
            return Equipped(slot) == item;
        }

        void MountItem(ContentDefinition item, int slot)
        {
            if (_section != "equipment" || slot < 0 || slot != ValidSlot(item) ||
                !_profile.TryEquip(_draft, item, _equipmentSide, CurrentCharacter, out var weapon)) return;
            _equipment = slot;
            if (weapon) _gun = weapon.Id;
            Preview();
        }

        bool Owned(ContentDefinition item) => _profile.Owns(item);
    }
}
