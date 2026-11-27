using System;
using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;

namespace AnimeCS.Play
{
    /// <summary>First playable driver. Static routes and the seat's own observations are its only inputs.</summary>
    public sealed class ObjectiveBot : ISeatDriver
    {
        private readonly MapDefinition _map;
        private readonly ContentCatalog _catalog;
        private readonly System.Random _random;
        private readonly BotTuning _tuning;
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
        public ObjectiveBot(ContentCatalog catalog, int seed, BotTuning tuning = null)
        {
            _map = catalog.Map;
            _catalog = catalog;
            _random = new System.Random(seed);
            _tuning = tuning ?? new BotTuning();
        }
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
            float turnStep = Mathf.Max(0f, _tuning.turnDegreesPerSecond) * SeatMovement.StepTime;
            if (view.VisibleEnemy.HasValue)
            {
                var contact = view.VisibleEnemy.Value;
                if (_target != contact.Seat)
                {
                    _target = contact.Seat;
                    int minTicks = MatchRules.Ticks(_tuning.minReactionSeconds);
                    int maxTicks = Mathf.Max(minTicks, MatchRules.Ticks(_tuning.maxReactionSeconds));
                    _readyTick = view.Match.Tick + _random.Next(minTicks, maxTicks + 1);
                    _error = (float)(0.6 + 1.4 * _random.NextDouble()) * (_random.Next(2) == 0 ? -1 : 1);
                }
                var aim = contact.Position - (view.Position + Vector3.up * SeatMovement.EyeHeight(view.Motion.Crouch));
                float yaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg + _error;
                float pitch = Mathf.Atan2(aim.y, new Vector2(aim.x, aim.z).magnitude) * Mathf.Rad2Deg;
                float yawStep = Mathf.Clamp(Mathf.DeltaAngle(view.Yaw, yaw), -turnStep, turnStep);
                input.Yaw += yawStep;
                float pitchBudget = Mathf.Sqrt(Mathf.Max(0, turnStep * turnStep - yawStep * yawStep));
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
                input.Yaw = Mathf.MoveTowardsAngle(view.Yaw, yaw, turnStep);
            }
            input.Pitch = Mathf.MoveTowards(view.Pitch, 0, Mathf.Max(0f,_tuning.levelPitchDegreesPerSecond)*SeatMovement.StepTime);
            input.Interact = !settling && distanceToGoal < 1.5f;
            if (input.Interact && (view.HasObjective || installed && view.Side == Side.Defend))
                input.Move = Vector2.zero;
            if (!view.HasObjective && !input.Interact && !installed && view.Heard.HasValue)
            {
                var clue = view.Heard.Value;
                if (!clue.Own && clue.Tick != _heardTick)
                {
                    _heardTick = clue.Tick; _listenUntil = clue.Tick + MatchRules.Ticks(_tuning.listenSeconds); _listenYaw = clue.Bearing * 45f;
                }
                if (view.Match.Tick < _listenUntil) input.Yaw = Mathf.MoveTowardsAngle(view.Yaw, _listenYaw, turnStep);
            }
            input.Reload = view.Magazine == 0;
            return input;
        }
        private Vector3 Waypoint(Vector3 from,Vector3 goal,int tick,int seat)
        {
            if(tick>=_replan || (goal-_lastGoal).sqrMagnitude>1)
            {
                _lastGoal=goal;_replan=tick+MatchRules.Ticks(_tuning.repathSeconds)+seat;_cornerCount=_corner=0;
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
