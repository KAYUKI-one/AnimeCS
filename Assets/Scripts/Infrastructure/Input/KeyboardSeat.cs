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
        private readonly InputAction _move, _look, _fire, _use, _walk, _jump, _crouch, _scope;
        private bool _switch, _drop, _reload, _clicked, _utility, _jumped;
        private bool _pausePressed, _shopPressed, _inspect, _confirm, _cancel, _skipLook;
        private int _cycle, _shopStep;
        private bool _active;
        private bool _shopping, _canLook, _blockFire;
        public bool ShopLook => _scope.IsPressed();
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
            BindSlot("Previous", HeldSlot.Primary);
            BindSlot("Next", HeldSlot.Sidearm);
            BindSlot("Knife", HeldSlot.Knife);
            BindSlot("UtilitySlot", HeldSlot.Utility);
            BindSlot("Objective", HeldSlot.Objective);
            BindSlot("LastWeapon", HeldSlot.Current);
            Action("PauseMatch").performed += _ => _pausePressed = true;
            Action("BuyMenu").performed += _ => _shopPressed = true;
            Action("DropHeld").performed += _ => _drop = true;
            Action("Reload").performed += _ => _reload = true;
            Action("ThrowUtility").performed += _ => _utility = true;
            _scope = Action("Scope");
            _scope.performed += _ => { if (_active && !Shopping) _zoom = true; };
            Action("Inspect").performed += _ => _inspect = true;
            var wheel = Action("CycleWeapon");
            wheel.performed += e => { float value = e.ReadValue<float>(); if (Mathf.Abs(value) > .01f) _cycle = value < 0 ? 1 : -1; };
            Action("ShopPrevious").performed += _ => _shopStep--;
            Action("ShopNext").performed += _ => _shopStep++;
            Action("ShopConfirm").performed += _ => _confirm = true;
            Action("ShopUndo").performed += _ => _cancel = true;
            _map.Enable();
        }

        private InputAction Action(string name)
        {
            var action = _map.FindAction(name);
            if (action != null) return action;
            // Older supplied maps can inherit new actions from the project-wide input asset.
            var fallback = InputSystem.actions.FindActionMap("Player", true).FindAction(name, true);
            action = _map.AddAction(name, fallback.type, interactions: fallback.interactions,
                processors: fallback.processors, expectedControlLayout: fallback.expectedControlType);
            foreach (var binding in fallback.bindings) action.AddBinding(binding);
            return action;
        }

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

        private void BindSlot(string name, HeldSlot selection)
        {
            var action = Action(name);
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
}
