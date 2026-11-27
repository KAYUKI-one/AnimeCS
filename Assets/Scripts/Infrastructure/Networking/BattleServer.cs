using System;
using System.Globalization;
using System.Net;
using AnimeCS.Content;
using AnimeCS.Match;
using AnimeCS.Play;
using AnimeCS.Profile;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        private double _nextFinalSnapshot;
        private int _enteredSeats;
        private int _seed;
        private int _snapshotInterval;
        private BattleResultReporter _results;
        private readonly ObjectiveBot[] _bots=new ObjectiveBot[LocalMatch.Seats];
        private readonly string[] _names=new string[LocalMatch.Seats];
        private Task<string> _loadingLineup;
        private UnityEngine.AI.NavMeshDataInstance _navigation;
        [Serializable] private sealed class Lineup {public Member[] members;}
        [Serializable] private sealed class Member {public int seat;public string id,loadoutJson;public string[] owned;}

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
                if (_catalog.Map.Navigation) _navigation=UnityEngine.AI.NavMesh.AddNavMeshData(_catalog.Map.Navigation);
                for (int i = 0; i < _dropItems.Length; i++) _world.RegisterDropBounds(_dropItems[i], _dropBounds[i]);
                _seed = Environment.TickCount;
                BeginMatch();
                string secret = Environment.GetEnvironmentVariable("ANIMECS_BATTLE_SECRET");
                if (!IPAddress.IsLoopback(endpoint.Address) && string.IsNullOrEmpty(secret))
                    throw new InvalidOperationException("ANIMECS_BATTLE_SECRET is required for LAN and public listeners");
                _host = new SeatDatagramHost(endpoint, LocalMatch.Seats, secret);
                _host.SetContent(_catalog.Map.Id, _catalog.ContentVersion);
                if (!string.IsNullOrEmpty(secret))
                {
                    _host.RoomStarted = () =>
                    {
                        Array.Clear(_names,0,_names.Length);
                        _enteredSeats=0;
                        BeginMatch();
                        _loadingLineup=_results.ReadLineup(_host.Room);
                    };
                    _host.SeatJoined = (seat,id) =>
                    {
                        _names[seat]=id; _match.SetSeatDisplayId(seat,id);
                        int bit=1<<seat;
                        if ((_enteredSeats&bit)!=0 || _match.Public.Phase==RoundPhase.Finished) return;
                        _results.RecordProgress(_host.Room,0,bit);
                        _enteredSeats|=bit;
                    };
                    _results = new BattleResultReporter(secret,System.IO.Path.Combine(Application.persistentDataPath,"BattleResults"));
                }
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

        private void BeginMatch(WardrobeLoadout[] gear=null)
        {
            _match = new LocalMatch(_catalog, _rules, _world, ++_seed, FirstSide.Random, null, MatchMode.Online,gear);
            for (int i = 0; i < _ports.Length; i++)
            {
                _match.SetSeatDisplayId(i,_names[i] ?? $"BOT {i+1:00}");
                _bots[i]=new ObjectiveBot(_catalog,_seed ^ (i*7919),_rules.bot);
                _ports[i] = _match.BindSeat(i);
            }
            ReadViews();
        }

        private bool LineupReady()
        {
            if(_loadingLineup==null) return true;
            if(!_loadingLineup.IsCompleted) return false;
            var read=_loadingLineup;
            _loadingLineup=null;
            try
            {
                var lineup=JsonUtility.FromJson<Lineup>(read.GetAwaiter().GetResult());
                var gear=new WardrobeLoadout[LocalMatch.Seats];
                foreach(var member in lineup.members)
                {
                    if((uint)member.seat>=gear.Length) continue;
                    _names[member.seat]=member.id;
                    var wardrobe=string.IsNullOrEmpty(member.loadoutJson)?new WardrobeProfile():
                        JsonUtility.FromJson<WardrobeProfile>(member.loadoutJson);
                    wardrobe.owned=new List<string>(member.owned??Array.Empty<string>());
                    gear[member.seat]=wardrobe.Resolve(wardrobe.current,_catalog);
                }
                BeginMatch(gear);
            }
            catch(Exception error) {Debug.LogWarning("Unable to load room equipment: "+error.Message);}
            return true;
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
            _results?.Tick(now);
            if (!_host.HasPlayers || !LineupReady() || _results != null && _host.Room == null)
            {
                _due = now;
                return;
            }
            if (_results != null && _match.Public.Phase == RoundPhase.Finished)
            {
                if (now >= _nextFinalSnapshot)
                {
                    Publish();
                    _nextFinalSnapshot = now + .5;
                }
                _due = now;
                return;
            }
            while (_due <= now)
            {
                _due += step;
                int connectedSeats=0;
                for (int i = 0; i < _ports.Length; i++)
                {
                    ISeatDriver driver = _host.Driver(i);
                    if (driver!=null) connectedSeats|=1<<i;
                    driver ??= _bots[i];
                    _ports[i].Submit(_views[i].Match.Tick + 1, driver.Decide(_views[i]));
                }
                var before=_match.Public;
                _match.Step();
                var current=_match.Public;
                int completed=current.Score0+current.Score1;
                if (_results!=null && connectedSeats!=0 && completed>before.Score0+before.Score1)
                    _results.RecordProgress(_host.Room,completed,connectedSeats);
                ReadViews();
                if (_match.Public.Tick % _snapshotInterval == 0 || _match.Public.Phase == RoundPhase.Finished) Publish();
                if (_match.Public.Phase == RoundPhase.Finished)
                {
                    if (_results == null) BeginMatch();
                    else
                    {
                        _nextFinalSnapshot = now + .5;
                        var result = _match.Public;
                        int winner = result.Score0 == result.Score1 ? -1 : result.Score0 > result.Score1 ? 0 : 1;
                        _results.Record(_host.Room,winner,completed,_enteredSeats);
                    }
                    break;
                }
            }
        }

        private void Publish()
        {
            _host.Publish(_views);
            _host.PublishRoster(_views, _world, _match);
        }

        private void OnDestroy()
        {
            _host?.Dispose();
            _results?.Dispose();
            if (_navigation.valid) _navigation.Remove();
            _world?.Dispose();
        }
    }
}
