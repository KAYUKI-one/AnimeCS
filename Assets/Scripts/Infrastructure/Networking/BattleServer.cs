using System;
using System.Globalization;
using System.Net;
using AnimeCS.Content;
using AnimeCS.Match;
using AnimeCS.Play;
using UnityEngine;

namespace AnimeCS.Networking
{
    // Only the dedicated build's generated, collision-only scene carries this component.
    public sealed class BattleServer : MonoBehaviour
    {
        [SerializeField] private ContentCatalog _catalog;
        [SerializeField] private MatchRules _rules;
        [SerializeField] private Bounds _objectiveBounds;
        [SerializeField] private ContentDefinition[] _dropItems;
        [SerializeField] private Bounds[] _dropBounds;

        private readonly ISeatPort[] _ports = new ISeatPort[LocalMatch.Seats];
        private readonly SeatView[] _views = new SeatView[LocalMatch.Seats];
        private ArenaWorld _world;
        private LocalMatch _match;
        private SeatDatagramHost _host;
        private double _due;
        private int _seed;
        private int _snapshotInterval;

        private void Start()
        {
            try
            {
                string address = Environment.GetEnvironmentVariable("ANIMECS_BATTLE_ADDRESS") ?? "127.0.0.1";
                string port = Environment.GetEnvironmentVariable("ANIMECS_BATTLE_PORT") ?? "27840";
                var endpoint = new IPEndPoint(IPAddress.Parse(address), int.Parse(port,CultureInfo.InvariantCulture));
                string configuredRate = Environment.GetEnvironmentVariable("ANIMECS_BATTLE_SNAPSHOT_HZ");
                int rate = configuredRate == null ? MatchRules.TickRate / 2 : int.Parse(configuredRate, CultureInfo.InvariantCulture);
                if (rate != MatchRules.TickRate && rate != MatchRules.TickRate / 2)
                    throw new ArgumentOutOfRangeException("ANIMECS_BATTLE_SNAPSHOT_HZ", "Choose 25 or 50 snapshots per second.");
                _snapshotInterval = MatchRules.TickRate / rate;
                var actors = new ArenaPawn[LocalMatch.Seats];
                for (int i = 0; i < actors.Length; i++)
                {
                    var go = new GameObject("Seat " + (i + 1));
                    go.transform.SetParent(transform, false);
                    actors[i] = go.AddComponent<ArenaPawn>();
                    actors[i].Tuning = _rules.movement;
                }
                _world = new ArenaWorld(actors, _catalog.Map, transform, dedicated:true, objectiveBounds:_objectiveBounds);
                for (int i = 0; i < _dropItems.Length; i++) _world.RegisterDropBounds(_dropItems[i], _dropBounds[i]);
                _seed = Environment.TickCount;
                BeginMatch();
                _host = new SeatDatagramHost(endpoint, LocalMatch.Seats);
                Application.targetFrameRate = MatchRules.TickRate * 2;
                Application.runInBackground = true;
                _due = Time.realtimeSinceStartupAsDouble;
                Debug.Log("AnimeCS battle ready. UDP port " + _host.LocalEndpoint.Port);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                enabled = false;
                Application.Quit(1);
            }
        }

        private void BeginMatch()
        {
            _match = new LocalMatch(_catalog, _rules, _world, ++_seed, FirstSide.Random, null, MatchMode.Online);
            for (int i = 0; i < _ports.Length; i++)
            {
                _match.SetSeatDisplayId(i, "Seat " + (i + 1));
                _ports[i] = _match.BindSeat(i);
            }
            ReadViews();
        }

        private void ReadViews()
        {
            for (int i = 0; i < _views.Length; i++) _views[i] = _ports[i].Read();
        }

        private void Update()
        {
            if (_host == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            const double step = 1d / MatchRules.TickRate;
            _due = Math.Max(_due, now - 7 * step);
            _host.Poll(_views);
            while (_due <= now)
            {
                _due += step;
                for (int i = 0; i < _ports.Length; i++)
                {
                    var driver = _host.Driver(i);
                    if (driver != null) _ports[i].Submit(_views[i].Match.Tick + 1, driver.Decide(_views[i]));
                }
                _match.Step();
                ReadViews();
                if (_match.Public.Tick % _snapshotInterval == 0 || _match.Public.Phase == RoundPhase.Finished) _host.Publish(_views);
                if (_match.Public.Phase == RoundPhase.Finished) { BeginMatch(); break; }
            }
        }

        private void OnDestroy()
        {
            _host?.Dispose();
            _world?.Dispose();
        }
    }
}
