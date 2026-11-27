using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimeCS.Content;
using AnimeCS.Match;
using AnimeCS.Play;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace AnimeCS.Tests.Performance
{
    // Opt-in standalone player probe. Uses the same map selector, match and seat input as normal play.
    public sealed class RenderProbe : MonoBehaviour, ISeatDriver
    {
        [Serializable] private sealed class Window
        {
            public string name;
            public double medianMs, p95Ms, worstMs, cpuMainMedianMs, gpuMedianMs;
            public long unityAllocatedMiB;
        }

        [Serializable] private sealed class Run
        {
            public string device, api, unity, wallRenderer, wallMaterial, sun;
            public int width, height, quality, lightmapIndex;
            public double lobbySeconds, practiceLoadSeconds, botLoadSeconds, restartMs;
            public Vector3 wallPoint, pixelA, pixelB;
            public List<Window> windows = new();
        }

        private static string _directory;
        private GraphicsStateCollection _trace;
        private bool _offscreen;
        private readonly Run _run = new();
        private readonly FrameTiming[] _timing = new FrameTiming[1];
        private LocalGame _game;
        private bool _buy, _zoom;
        private HeldSlot _select;
        private float _yaw = 48;
        private Vector2 _move;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-render-probe");
            if (i < 0 || i + 1 >= args.Length) return;
            _directory = Path.GetFullPath(args[i + 1]);
            Directory.CreateDirectory(_directory);
            var host = new GameObject("Standalone render probe");
            DontDestroyOnLoad(host);
            var probe = host.AddComponent<RenderProbe>();
            probe._offscreen = Array.IndexOf(args, "-offscreen-probe") >= 0;
            if (Array.IndexOf(args, "-trace-states") >= 0 && SystemInfo.supportsParallelPSOCreation)
            {
                if (!Debug.isDebugBuild)
                    throw new InvalidOperationException("Graphics state tracing requires a Development Build");
                probe._trace = new GraphicsStateCollection();
                if (!probe._trace.BeginTrace()) throw new InvalidOperationException("Graphics state tracing did not start");
            }
        }

        public SeatIntent Decide(in SeatView view)
        {
            var command = new SeatIntent { Yaw = _yaw, Move = _move, Buy = _buy ? BuySlot.Primary : BuySlot.None,
                ItemId = _buy ? "weapon.feather08" : null, Zoom = _zoom, Select = _select };
            _buy = _zoom = false;
            _select = HeldSlot.Current;
            return command;
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            _run.device = SystemInfo.graphicsDeviceName;
            _run.api = SystemInfo.graphicsDeviceType.ToString();
            _run.unity = Application.unityVersion;
            _run.width = Screen.width;
            _run.height = Screen.height;
            _run.quality = QualitySettings.GetQualityLevel();
            while (!_game || !_game.InLobby) { _game = FindAnyObjectByType<LocalGame>(); yield return null; }
            _run.lobbySeconds = Time.realtimeSinceStartupAsDouble;
            var original = _game.SelectedMap;
            var range = Resources.FindObjectsOfTypeAll<MapDefinition>().First(m => m.Practice);
            yield return WindowFrames("lobby", 45);
            if (_trace && Array.IndexOf(Environment.GetCommandLineArgs(), "-trace-lobby") >= 0)
            {
                SaveTrace("Lobby.graphicsstate");
                Application.Quit();
                yield break;
            }
            double start = Time.realtimeSinceStartupAsDouble;
            _game.SelectMap(range);
            _game.Restart();
            while (!_game.Ready || _game.InLobby) yield return null;
            _run.practiceLoadSeconds = Time.realtimeSinceStartupAsDouble - start;
            _game.SetSeatDriver(0, this);
            yield return WindowFrames("practice entry", 40);
            var cam = Camera.main;
            if (!Physics.Raycast(cam.transform.position, Quaternion.Euler(0,55,0) * Vector3.forward, out var hit, 100))
                throw new InvalidOperationException("Practice wall missing");
            _run.wallPoint = hit.point;
            _run.wallRenderer = hit.collider.name;
            var renderer = hit.collider.GetComponent<Renderer>();
            if (renderer) { _run.wallMaterial = renderer.sharedMaterial.name; _run.lightmapIndex = renderer.lightmapIndex; }
            _run.sun = RenderSettings.sun ? RenderSettings.sun.name : "";
            _run.pixelA = cam.WorldToScreenPoint(hit.point);
            yield return Capture("wall-A");
            _move = Vector2.left;
            int until = _game.MatchState.Tick + MatchRules.TickRate;
            while (_game.MatchState.Tick < until) yield return null;
            _move = Vector2.zero;
            yield return WindowFrames("practice steady", 100);
            _run.pixelB = cam.WorldToScreenPoint(hit.point);
            yield return Capture("wall-B");
            _buy = true;
            until = _game.MatchState.Tick + 90;
            while (_game.MatchState.Tick < until) yield return null;
            if (_game.Player.Read().WeaponId != "weapon.feather08") throw new InvalidOperationException("Probe weapon missing");
            for (int index = 0; index < 6; index++)
            {
                _zoom = true;
                yield return WindowFrames("scope " + index, 36);
                if (_game.Player.Read().ScopeLevel != (index + 1) % 3) throw new InvalidOperationException("Lens did not cycle");
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(),"-capture-character")>=0)
                yield return CharacterPortrait(range);
            _game.ReturnToLobby();
            start = Time.realtimeSinceStartupAsDouble;
            _game.SelectMap(original);
            _game.Restart();
            while (_game.InLobby || !_game.Ready) yield return null;
            _run.botLoadSeconds = Time.realtimeSinceStartupAsDouble - start;
            yield return WindowFrames("bot entry", 60);
            yield return Capture("bot-map");
            start = Time.realtimeSinceStartupAsDouble;
            _game.Restart();
            while (!_game.Ready) yield return null;
            _run.restartMs = (Time.realtimeSinceStartupAsDouble - start) * 1000;
            yield return WindowFrames("bot restart", 60);
            if (_trace && _trace.isTracing)
                SaveTrace("Desktop.graphicsstate");
            File.WriteAllText(Path.Combine(_directory, "frames.json"), JsonUtility.ToJson(_run, true));
            Debug.Log("Render probe complete");
            Application.Quit();
        }

        private void SaveTrace(string file)
        {
            _trace.EndTrace();
            if (_trace.totalGraphicsStateCount == 0 || !_trace.SaveToFile(Path.Combine(_directory, file)))
                throw new IOException("Could not save the graphics state trace");
            Debug.Log($"Render probe captured {_trace.totalGraphicsStateCount} graphics states");
            Destroy(_trace);
        }

        private IEnumerator CharacterPortrait(MapDefinition range)
        {
            _select = HeldSlot.Sidearm;
            var target = range.Spawn(Side.Defend, 1);
            var subject = new Vector2(target.x, target.z);
            // Walk through the open center lane before approaching the target behind the left cover.
            foreach (var stop in new[] { new Vector2(0, 3), subject - Vector2.up * 1.05f })
            {
                int deadline = _game.MatchState.Tick + MatchRules.TickRate * 6;
                while (true)
                {
                    var feet = _game.Player.Read().Position;
                    var delta = stop - new Vector2(feet.x, feet.z);
                    _yaw = Mathf.Atan2(target.x - feet.x, target.z - feet.z) * Mathf.Rad2Deg;
                    if (delta.sqrMagnitude < .025f) break;
                    if (_game.MatchState.Tick >= deadline) throw new InvalidOperationException("Portrait waypoint blocked");
                    _move = Vector2.ClampMagnitude(delta * 2, 1);
                    yield return null;
                }
            }
            _move = Vector2.zero;
            int settled = _game.MatchState.Tick + 12;
            while (_game.MatchState.Tick < settled)
            {
                var feet = _game.Player.Read().Position;
                _yaw = Mathf.Atan2(target.x - feet.x, target.z - feet.z) * Mathf.Rad2Deg;
                yield return null;
            }
            yield return Capture("practice-character");
        }

        private IEnumerator Capture(string file)
        {
            yield return new WaitForEndOfFrame();
            if (_offscreen) RenderCamera(file);
            else ScreenCapture.CaptureScreenshot(Path.Combine(_directory, file + ".png"));
            for (int i = 0; i < 5; i++) yield return null;
        }

        // A hidden Windows swap chain can skip presenting every frame. Render the actual scene camera explicitly.
        private void RenderCamera(string file)
        {
            var camera = Camera.main;
            if (!camera) return;
            var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var original = camera.targetTexture;
            Texture2D pixels = null;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                if (file == null) return;
                RenderTexture.active = target;
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                File.WriteAllBytes(Path.Combine(_directory, file + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = original;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (pixels) Destroy(pixels);
            }
        }

        private IEnumerator WindowFrames(string name, int count)
        {
            if (_offscreen) RenderCamera(name == "lobby" ? "lobby-camera" : null);
            var times = new double[count];
            var main = new List<double>(count);
            var gpu = new List<double>(count);
            for (int i = 0; i < count; i++)
            {
                double start = Time.realtimeSinceStartupAsDouble;
                FrameTimingManager.CaptureFrameTimings();
                yield return null;
                times[i] = (Time.realtimeSinceStartupAsDouble - start) * 1000;
                if (FrameTimingManager.GetLatestTimings(1, _timing) == 0) continue;
                if (_timing[0].cpuMainThreadFrameTime > 0) main.Add(_timing[0].cpuMainThreadFrameTime);
                if (_timing[0].gpuFrameTime > 0) gpu.Add(_timing[0].gpuFrameTime);
            }
            Array.Sort(times);
            main.Sort();
            gpu.Sort();
            _run.windows.Add(new Window { name = name, medianMs = times[count/2], p95Ms = times[(int)Math.Ceiling(count*.95)-1],
                worstMs = times[count-1], cpuMainMedianMs = main.Count > 0 ? main[main.Count/2] : 0,
                gpuMedianMs = gpu.Count > 0 ? gpu[gpu.Count/2] : 0,
                unityAllocatedMiB = Profiler.GetTotalAllocatedMemoryLong() >> 20 });
            File.WriteAllText(Path.Combine(_directory, "frames.partial.json"), JsonUtility.ToJson(_run, true));
        }
    }
}
