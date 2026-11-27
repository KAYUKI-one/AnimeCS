using AnimeCS.Content;
using AnimeCS.Profile;

namespace AnimeCS.Match
{
    public sealed class WeaponInstance
    {
        public WeaponDefinition Definition { get; }
        public string SkinId { get; }
        public int Magazine { get; internal set; }
        public int Reserve { get; internal set; }
        internal int NextFireTick { get; private set; }
        private const int TicksPerMinute = 60 * MatchRules.TickRate;
        private readonly int _roundsPerMinute;
        private long _scheduledShot;

        internal WeaponInstance(WeaponDefinition definition, string skin)
        {
            Definition = definition;
            SkinId = skin;
            Magazine = definition.Magazine;
            Reserve = definition.Reserve;
            _roundsPerMinute = System.Math.Clamp((int)definition.ShotsPerMinute, 1, TicksPerMinute);
        }

        internal void RecordShot(int tick)
        {
            // Keep the exact deadline in tick × RPM units. A late trigger starts a new cadence.
            if (tick != NextFireTick) _scheduledShot = (long)tick * _roundsPerMinute;
            _scheduledShot += TicksPerMinute;
            NextFireTick = (int)((_scheduledShot + _roundsPerMinute - 1) / _roundsPerMinute);
        }

        internal void BeginRound()
        {
            Reserve = Definition.Reserve;
            NextFireTick = 0;
            _scheduledShot = 0;
        }
    }

    internal sealed class SeatInventory
    {
        private readonly ContentCatalog _catalog;
        private readonly WardrobeLoadout _appearance;
        private WeaponInstance _primary, _sidearm, _pendingPrimary, _pendingSidearm;
        private readonly UtilityBelt _utilities=new();
        private bool _pendingArmor, _pendingHelmet, _pendingKit;
        private HeldSlot _previous, _held;
        public WeaponInstance Primary => _pendingPrimary ?? _primary;
        public WeaponInstance Sidearm => _pendingSidearm ?? _sidearm;
        public WeaponInstance Knife { get; private set; }
        public HeldSlot Held => _held;
        public UtilityDefinition Utility=>_utilities.Selected;
        public UtilityCounts Utilities=>_utilities.Counts();
        public UtilityDefinition TakeUtility()
        {
            var item=_utilities.Take();if(_held==HeldSlot.Utility && !Utility)_held=Fallback();
            return item;
        }
        public bool AddUtility(UtilityDefinition item)=>_utilities.Add(item,false);
        public bool Armor, Helmet, Kit, CarriesObjective;
        public int Wallet;
        private string _outfitId;
        private WornItems _worn;
        public WornItems Worn => _worn;
        public string OutfitId => _outfitId;
        public void Dress(OutfitDefinition outfit)
        {
            _outfitId = outfit.Id;
            _worn = default;
            var selected = _appearance.WornFor(outfit.Character.Faction);
            foreach (WearSlot slot in System.Enum.GetValues(typeof(WearSlot)))
            {
                var item=_catalog.Find<WearableDefinition>(selected.Get(slot));
                if (item && item.Fits(outfit.Character)) _worn.Set(slot,item.Id);
            }
        }
        public string PreferredOutfit(Side side) => _appearance.OutfitFor(side);
        public WeaponInstance Active => _held switch
        { HeldSlot.Primary => Primary, HeldSlot.Sidearm => Sidearm, HeldSlot.Knife => Knife, _ => null };
        public PurchaseDraft Draft => new PurchaseDraft
        {
            Primary = _pendingPrimary?.Definition.Id, Sidearm = _pendingSidearm?.Definition.Id,
            Utility = _utilities.LastOrder ? _utilities.LastOrder.Id : null, Utilities=_utilities.Counts(true), Armor = _pendingArmor, Helmet = _pendingHelmet, Kit = _pendingKit
        };
        public int Available(MatchRules rules) => Wallet - (_pendingPrimary?.Definition.Price ?? 0)
            - (_pendingSidearm?.Definition.Price ?? 0) - _utilities.Cost
            - (_pendingArmor ? rules.armorPrice : 0) - (_pendingHelmet ? rules.helmetPrice : 0) - (_pendingKit ? rules.kitPrice : 0);

        public SeatInventory(ContentCatalog catalog, WardrobeLoadout appearance)
        { _catalog = catalog; _appearance = appearance; }

        private bool Has(HeldSlot slot) => slot switch
        {
            HeldSlot.Primary => Primary != null, HeldSlot.Sidearm => Sidearm != null,
            HeldSlot.Knife => Knife != null, HeldSlot.Objective => CarriesObjective, HeldSlot.Utility => Utility, _ => false
        };

        public bool Select(HeldSlot next, bool previous = false)
        {
            if (previous) next = Has(_previous) ? _previous : Fallback();
            if(next==HeldSlot.Utility && next==_held && !previous)return _utilities.Step(1,true);
            if (!Has(next) || next == _held) return false;
            _previous = _held; _held = next;
            return true;
        }

        private HeldSlot Fallback() => Primary != null ? HeldSlot.Primary : Sidearm != null ? HeldSlot.Sidearm :
            Knife != null ? HeldSlot.Knife : CarriesObjective ? HeldSlot.Objective : HeldSlot.Current;

        private static readonly HeldSlot[] Wheel={HeldSlot.Primary,HeldSlot.Sidearm,HeldSlot.Knife,HeldSlot.Utility,HeldSlot.Objective};
        public bool Cycle(int direction)
        {
            if (direction == 0) return false;
            direction=System.Math.Sign(direction);
            if(_held==HeldSlot.Utility && _utilities.Step(direction,false))return true;
            int current=System.Array.IndexOf(Wheel,_held);
            for(int i=1;i<=Wheel.Length;i++)
            {
                var slot=Wheel[(current+direction*i+Wheel.Length*2)%Wheel.Length];
                if(!Has(slot))continue;
                if(slot==HeldSlot.Utility)_utilities.Enter(direction);
                return Select(slot);
            }
            return false;
        }

        public void RemoveObjective()
        {
            CarriesObjective = false;
            if (_held == HeldSlot.Objective) _held = Fallback();
        }

        public WeaponInstance Take(HeldSlot slot)
        {
            WeaponInstance value;
            if (slot == HeldSlot.Primary) { value = _primary; _primary = null; }
            else if (slot == HeldSlot.Sidearm) { value = _sidearm; _sidearm = null; }
            else return null;
            if (_held == slot) _held = Fallback();
            return value;
        }

        public void Equip(WeaponInstance gun)
        {
            var slot = gun.Definition.Class == WeaponClass.Pistol ? HeldSlot.Sidearm : HeldSlot.Primary;
            if (slot == HeldSlot.Sidearm) _sidearm = gun; else _primary = gun;
            Select(slot);
        }

        public void BeginRound(Side side, bool clearEquipment)
        {
            ClearOrder();
            if (clearEquipment)
            {
                _primary = _sidearm = null;
                Armor = Helmet = Kit = false; _utilities.Clear();
            }
            if (side != Side.Defend) Kit = false;
            var knife=_catalog.Find<WeaponDefinition>(_appearance.GearFor(side)?.knifeId ?? _appearance.knifeId);
            if (!knife || knife.Class!=WeaponClass.Knife) knife=_catalog.Knife;
            if (knife && Knife?.Definition != knife) Knife = Issue(knife);
            _sidearm ??= Issue(_catalog.StartingPistol(side));
            _sidearm.BeginRound();
            _primary?.BeginRound();
            _held = Fallback(); _previous = _held == HeldSlot.Primary ? HeldSlot.Sidearm : HeldSlot.Knife;
        }

        public bool Train(SeatIntent intent)
        {
            if (intent.CancelPurchase || intent.Buy==BuySlot.None) return false;
            if (intent.Buy==BuySlot.Armor) { Armor=true; return false; }
            if (intent.Buy==BuySlot.Helmet) { Helmet=true; return false; }
            if (intent.Buy==BuySlot.Kit) { Kit=true; return false; }
            if (intent.Buy==BuySlot.Utility)
            {
                var utility=_catalog.Find<UtilityDefinition>(intent.ItemId);
                _utilities.Add(utility,false);
                return false;
            }
            var gun=_catalog.Find<WeaponDefinition>(intent.ItemId);
            if (!gun || gun.Archived) return false;
            if (gun.Class==WeaponClass.Knife)
            {
                if (intent.Buy!=BuySlot.Knife) return false;
                Knife=Issue(gun); Select(HeldSlot.Knife); return true;
            }
            if (intent.Buy!=(gun.Class==WeaponClass.Pistol ? BuySlot.Sidearm : BuySlot.Primary)) return false;
            Equip(Issue(gun));
            return true;
        }

        public void Order(SeatIntent intent, Side side, MatchRules rules)
        {
            if (intent.Buy == BuySlot.None) return;
            int available = Available(rules);
            if (intent.Buy == BuySlot.Armor || intent.Buy == BuySlot.Helmet || intent.Buy == BuySlot.Kit)
            {
                if (intent.Buy == BuySlot.Armor)
                    _pendingArmor = !intent.CancelPurchase && !Armor && (_pendingArmor || available >= rules.armorPrice);
                else if (intent.Buy == BuySlot.Helmet)
                    _pendingHelmet = !intent.CancelPurchase && !Helmet && (_pendingHelmet || available >= rules.helmetPrice);
                else
                    _pendingKit = !intent.CancelPurchase && side == Side.Defend && !Kit && (_pendingKit || available >= rules.kitPrice);
                return;
            }
            if (intent.Buy == BuySlot.Utility)
            {
                var value = _catalog.Find<UtilityDefinition>(intent.ItemId);
                if(intent.CancelPurchase){_utilities.Cancel(value);if(!Has(_held))_held=Fallback();return;}
                if(value && value.Price<=available)_utilities.Add(value,true);
                return;
            }
            if (intent.Buy != BuySlot.Primary && intent.Buy != BuySlot.Sidearm) return;
            bool primary = intent.Buy == BuySlot.Primary;
            ref var pending = ref (primary ? ref _pendingPrimary : ref _pendingSidearm);
            if (intent.CancelPurchase)
            {
                pending = null;
                if (!Has(_held)) _held = Fallback();
                return;
            }
            var gun = _catalog.Find<WeaponDefinition>(intent.ItemId);
            if (!gun || gun.Price <= 0 || !gun.CanBuy(side) || (gun.Class == WeaponClass.Pistol) == primary ||
                gun == (primary ? Primary : Sidearm)?.Definition || gun.Price > available + (pending?.Definition.Price ?? 0)) return;
            pending = Issue(gun);
            Select(primary ? HeldSlot.Primary : HeldSlot.Sidearm);
        }

        public void Commit(MatchRules rules)
        {
            Wallet = Available(rules);
            if (_pendingPrimary != null) _primary = _pendingPrimary;
            if (_pendingSidearm != null) _sidearm = _pendingSidearm;
            _utilities.Commit();
            Armor |= _pendingArmor; Helmet |= _pendingHelmet; Kit |= _pendingKit;
            ClearOrder();
        }

        private WeaponInstance Issue(WeaponDefinition gun) => new WeaponInstance(gun, _appearance.SkinFor(gun.Id));
        private void ClearOrder()
        {
            _pendingPrimary = _pendingSidearm = null;
            _utilities.ClearOrders();
            _pendingArmor = _pendingHelmet = _pendingKit = false;
        }
    }
}
