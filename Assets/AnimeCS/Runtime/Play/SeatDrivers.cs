using System;
using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimeCS.Play
{
    internal sealed class KeyboardSeat : ISeatDriver, IDisposable
    {
        private readonly InputActionMap _map;
        private readonly InputAction _move, _look, _fire, _use, _walk, _jump, _crouch;
        private bool _switch, _drop, _reload, _clicked, _utility, _jumped;
        private bool _pausePressed, _shopPressed, _inspect, _confirm, _cancel, _skipLook;
        private int _cycle, _shopStep;
        private bool _active;
        private bool _shopping, _canLook, _blockFire;
        public bool ShopLook => Mouse.current != null && Mouse.current.rightButton.isPressed;
        public bool Shopping
        {
            get => _shopping;
            set { if (_shopping==value) return; _shopping=value; _clicked=false; _skipLook=true; _blockFire=_fire.IsPressed(); }
        }
        private Vector2 _movePulse;
        private readonly System.Collections.Generic.Queue<SeatIntent> _orders = new();
        private bool _zoom;
        private HeldSlot _slot;
        private int _round;
        private float _yaw, _pitch;
        public float Sensitivity { get; set; } = .1f;
        public float ViewYaw => _yaw;
        private float _viewKick;
        internal void SetViewKick(float degrees) => _viewKick=degrees;
        public float ViewPitch => Mathf.Clamp(_pitch+_viewKick,-82,82);
        internal bool PredictPress=>_active && !_shopping && !_blockFire && _clicked && !_reload && !_drop && !_switch && !_utility && !_use.IsPressed() && _slot==HeldSlot.Current && _cycle==0;

        public KeyboardSeat(InputActionAsset actions)
        {
            _map = actions.FindActionMap("Player", true).Clone();
            _move = _map.FindAction("Move", true); _look = _map.FindAction("Look", true);
            _fire = _map.FindAction("Attack", true); _use = _map.FindAction("Interact", true);
            _walk = _map.FindAction("Sprint", true); _jump = _map.FindAction("Jump", true);
            _crouch = _map.FindAction("Crouch", true);
            _move.performed += RememberMove;
            BindSlot("Previous", "<Keyboard>/1", HeldSlot.Primary);
            BindSlot("Next", "<Keyboard>/2", HeldSlot.Sidearm);
            BindSlot("Knife", "<Keyboard>/3", HeldSlot.Knife);
            BindSlot("Objective", "<Keyboard>/5", HeldSlot.Objective);
            BindSlot("LastWeapon", "<Keyboard>/q", HeldSlot.Current);
            Button("PauseMatch", "<Keyboard>/escape").performed += _ => _pausePressed = true;
            Button("BuyMenu", "<Keyboard>/b").performed += _ => _shopPressed = true;
            Button("DropHeld", "<Keyboard>/g").performed += _ => _drop = true;
            Button("Reload", "<Keyboard>/r").performed += _ => _reload = true;
            Button("ThrowUtility", "<Keyboard>/x").performed += _ => _utility = true;
            Button("Scope", "<Mouse>/rightButton").performed += _ => { if (_active && !Shopping) _zoom = true; };
            Button("Inspect", "<Keyboard>/f").performed += _ => _inspect = true;
            var wheel = _map.FindAction("CycleWeapon") ?? _map.AddAction("CycleWeapon", InputActionType.PassThrough, "<Mouse>/scroll/y");
            wheel.performed += e => { float value = e.ReadValue<float>(); if (Mathf.Abs(value) > .01f) _cycle = value < 0 ? 1 : -1; };
            Button("ShopPrevious", "<Keyboard>/upArrow").performed += _ => _shopStep--;
            Button("ShopNext", "<Keyboard>/downArrow").performed += _ => _shopStep++;
            Button("ShopConfirm", "<Keyboard>/enter").performed += _ => _confirm = true;
            Button("ShopUndo", "<Keyboard>/backspace").performed += _ => _cancel = true;
            _map.Enable();
        }

        private InputAction Button(string name, string defaultBinding) =>
            _map.FindAction(name) ?? _map.AddAction(name, InputActionType.Button, defaultBinding);

        public void ReadMenuKeys(out bool pause, out bool shop)
        {
            pause = _pausePressed;
            shop = _shopPressed;
            _pausePressed = _shopPressed = false;
        }

        public void ReadShop(out int step, out bool confirm, out bool cancel)
        {
            step = _shopStep; confirm = _confirm; cancel = _cancel;
            _shopStep = 0; _confirm = _cancel = false;
        }

        public void SyncRound(in SeatView view, bool force=false)
        {
            if (!force && _round == view.Match.Round) return;
            _round = view.Match.Round; _yaw = view.Yaw; _pitch = view.Pitch;
        }

        public void ResetPointer()
        {
            _skipLook = true; _orders.Clear(); _blockFire=_fire.IsPressed();
            ClearEdges();
        }

        private void BindSlot(string name, string defaultBinding, HeldSlot selection)
        {
            var action = Button(name, defaultBinding);
            // The private map owns these callbacks. Latch in event order until the next match tick.
            action.performed += _ =>
            {
                _slot = selection;
                _switch = selection == HeldSlot.Current;
            };
        }

        private void RememberMove(InputAction.CallbackContext context)
        {
            var value = context.ReadValue<Vector2>();
            if (value.sqrMagnitude > .001f) _movePulse = value;
        }

        private void ClearEdges()
        {
            _zoom = _switch = _drop = _reload = _clicked = _utility = _jumped = _inspect = false; _cycle = 0;
            _movePulse = Vector2.zero; _slot=HeldSlot.Current;
        }

        public void Sample(bool active, bool lookAllowed=true, float magnification=1f)
        {
            _active = active;
            if (!active) { ClearEdges(); return; }
            if (!_fire.IsPressed()) _blockFire=false;
            var look = lookAllowed && _canLook && !_skipLook ? _look.ReadValue<Vector2>() : Vector2.zero;
            _canLook=lookAllowed; _skipLook=false;
            look /= Mathf.Max(1f, magnification);
            _yaw += look.x * Sensitivity; _pitch = Mathf.Clamp(_pitch + look.y * Sensitivity, -82, 82);
            _clicked |= _fire.WasPressedThisFrame();
            _jumped |= _jump.WasPressedThisFrame();
        }

        public void Order(BuySlot slot, string id, bool cancel) =>
            _orders.Enqueue(new SeatIntent { Buy = slot, ItemId = id, CancelPurchase = cancel });

        public SeatIntent Decide(in SeatView view)
        {
            SyncRound(view);
            var input = _orders.Count > 0 ? _orders.Dequeue() : default;
            input.Yaw = _yaw; input.Pitch = ViewPitch;
            if (!_active || view.Health <= 0 || view.Match.Paused || view.Match.Phase == RoundPhase.Finished) { ClearEdges(); return input; }
            var local = _move.ReadValue<Vector2>();
            var world = Quaternion.Euler(0, _yaw, 0) * new Vector3(local.x, 0, local.y);
            input.Move = new Vector2(world.x, world.z);
            var pulse = Quaternion.Euler(0, _yaw, 0) * new Vector3(_movePulse.x, 0, _movePulse.y);
            input.BrakeTap = new Vector2(pulse.x, pulse.z);
            input.Jump = _jumped; input.Crouch = _crouch.IsPressed();
            input.FirePressed = !_blockFire && _clicked;
            input.Fire = !_blockFire && (_fire.IsPressed() || _clicked); input.Interact = _use.IsPressed(); input.Walk = _walk.IsPressed();
            input.Select=_slot;input.SwitchWeapon = _switch; input.DropHeld = _drop; input.Inspect = _inspect; input.Cycle = _cycle; input.Reload = _reload;
            input.Zoom = _zoom && !Shopping; input.LowerScope = Shopping;
            input.ThrowUtility = _utility;
            if (Shopping)
            {
                input.Move = input.BrakeTap = Vector2.zero; input.Fire = input.Interact = false;
                input.Crouch = input.Jump = input.ThrowUtility = false;
                input.Cycle = 0;
            }
            ClearEdges();
            return input;
        }
        public void Dispose() { _move.performed -= RememberMove; _map.Disable(); _map.Dispose(); }
    }

    /// <summary>First playable driver. Static routes and the seat's own observations are its only inputs.</summary>
    public sealed class ObjectiveBot : ISeatDriver
    {
        private readonly MapDefinition _map;
        private readonly ContentCatalog _catalog;
        private readonly System.Random _random;
        private readonly UnityEngine.AI.NavMeshPath _path=new UnityEngine.AI.NavMeshPath();
        private readonly Vector3[] _corners=new Vector3[192];
        private int _corner,_cornerCount,_replan;
        private Vector3 _lastGoal;
        private int _round, _waypoint, _target = -1, _readyTick, _buyStep;
        private float _error;
        private int _heardTick = -1, _listenUntil;
        private float _listenYaw;
        private Site _site;
        private bool _clicked;
        public ObjectiveBot(ContentCatalog catalog, int seed) { _map = catalog.Map; _catalog = catalog; _random = new System.Random(seed); }
        public SeatIntent Decide(in SeatView view)
        {
            var input = new SeatIntent { Yaw = view.Yaw, Pitch = view.Pitch };
            if (_round != view.Match.Round)
            {
                _round = view.Match.Round; _waypoint = _buyStep = 0; _target = -1;
                _cornerCount=_replan=0;
                _site = (view.Seat + _round) % 2 == 0 ? Site.A : Site.B;
            }
            if (view.Health <= 0) return input;
            if (view.Match.Phase == RoundPhase.Buy)
            {
                if (_buyStep == 0)
                {
                    WeaponDefinition best = null;
                    foreach (var entry in _catalog.Entries)
                        if (entry is WeaponDefinition gun && gun.Class != WeaponClass.Pistol && gun.CanBuy(view.Side) &&
                            gun.Price <= view.Credits && (best == null || gun.Price > best.Price)) best = gun;
                    if (best != null) { input.Buy = BuySlot.Primary; input.ItemId = best.Id; }
                }
                else if (_buyStep == 1) input.Buy = BuySlot.Armor;
                else if (_buyStep == 2) input.Buy = BuySlot.Helmet;
                if (_buyStep == 3 && view.Side == Side.Defend && view.Seat % 2 == 0) input.Buy = BuySlot.Kit;
                if (_buyStep < 4) _buyStep++;
                return input;
            }
            if (view.Match.Phase == RoundPhase.Finished) return input;
            if (view.VisibleEnemy.HasValue)
            {
                var contact = view.VisibleEnemy.Value;
                if (_target != contact.Seat)
                {
                    _target = contact.Seat;
                    _readyTick = view.Match.Tick + _random.Next(12, 25);
                    _error = (float)(0.6 + 1.4 * _random.NextDouble()) * (_random.Next(2) == 0 ? -1 : 1);
                }
                var aim = contact.Position - (view.Position + Vector3.up * SeatMovement.EyeHeight(view.Motion.Crouch));
                float yaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg + _error;
                float pitch = Mathf.Atan2(aim.y, new Vector2(aim.x, aim.z).magnitude) * Mathf.Rad2Deg;
                float yawStep = Mathf.Clamp(Mathf.DeltaAngle(view.Yaw, yaw), -4.8f, 4.8f);
                input.Yaw += yawStep;
                float pitchBudget = Mathf.Sqrt(Mathf.Max(0, 4.8f * 4.8f - yawStep * yawStep));
                input.Pitch = Mathf.MoveTowards(view.Pitch, pitch, pitchBudget);
                bool sniper = _catalog.Find<WeaponDefinition>(view.WeaponId)?.Optics != null;
                input.Zoom = sniper && view.ScopeLevel == 0 && !view.Bolting;
                if (view.Match.Tick >= _readyTick)
                {
                    bool aimed = Mathf.Abs(Mathf.DeltaAngle(input.Yaw, yaw)) < 3 && Mathf.Abs(input.Pitch - pitch) < 3;
                    input.Fire = aimed && !_clicked && (!sniper || view.ScopeStability >= 1f);
                    _clicked = input.Fire; _error = Mathf.MoveTowards(_error, 0, 0.03f);
                }
                input.Reload = view.Magazine == 0;
                return input;
            }
            input.LowerScope = view.ScopeLevel > 0 || view.Bolting;
            _target = -1; _clicked = false;
            bool settling = view.Match.Phase == RoundPhase.Settlement;
            bool installed = view.Match.Phase == RoundPhase.Planted;
            var route = _map.Route(view.Side, _site);
            Vector3 goal = _map.SitePosition(installed ? view.Match.PlantedSite : _site);
            if (settling) goal = _map.Spawn(view.Side,view.Seat % LocalMatch.TeamSize);
            else if (view.DroppedObjective.HasValue) goal = view.DroppedObjective.Value;
            else if (!_map.Navigation && !installed && _waypoint < route.Count)
            {
                goal = route[_waypoint];
                if (Flat(goal - view.Position).magnitude < 1.1f) _waypoint++;
            }
            if (installed && view.Side == Side.Defend && view.VisibleDevice.HasValue) goal = view.VisibleDevice.Value;
            float distanceToGoal=Flat(goal-view.Position).magnitude;
            if(_map.Navigation && distanceToGoal>1)goal=Waypoint(view.Position,goal,view.Match.Tick,view.Seat);
            var delta = Flat(goal - view.Position);
            if (delta.magnitude > (_map.Navigation ? .14f : .75f))
            {
                input.Move = delta.normalized;
                float yaw = Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
                input.Yaw = Mathf.MoveTowardsAngle(view.Yaw, yaw, 4.8f);
            }
            input.Pitch = Mathf.MoveTowards(view.Pitch, 0, 3);
            input.Interact = !settling && distanceToGoal < 1.5f;
            if (input.Interact && (view.HasObjective || installed && view.Side == Side.Defend))
                input.Move = Vector2.zero;
            if (!view.HasObjective && !input.Interact && !installed && view.Heard.HasValue)
            {
                var clue = view.Heard.Value;
                if (!clue.Own && clue.Tick != _heardTick)
                {
                    _heardTick = clue.Tick; _listenUntil = clue.Tick + 15; _listenYaw = clue.Bearing * 45f;
                }
                if (view.Match.Tick < _listenUntil) input.Yaw = Mathf.MoveTowardsAngle(view.Yaw, _listenYaw, 4.8f);
            }
            input.Reload = view.Magazine == 0;
            return input;
        }
        private Vector3 Waypoint(Vector3 from,Vector3 goal,int tick,int seat)
        {
            if(tick>=_replan || (goal-_lastGoal).sqrMagnitude>1)
            {
                _lastGoal=goal;_replan=tick+35+seat;_cornerCount=_corner=0;
                if(UnityEngine.AI.NavMesh.SamplePosition(from,out var start,2f,-1) &&
                   UnityEngine.AI.NavMesh.SamplePosition(goal,out var end,2f,-1) &&
                   UnityEngine.AI.NavMesh.CalculatePath(start.position,end.position,-1,_path))
                { _cornerCount=_path.GetCornersNonAlloc(_corners);_corner=1; }
            }
            while(_corner<_cornerCount && Flat(_corners[_corner]-from).sqrMagnitude<.11f)_corner++;
            return _corner<_cornerCount ? _corners[_corner] : goal;
        }
        private static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
    }
}
