using System;
using AnimeCS.Match;
using UnityEngine;

namespace AnimeCS.Play
{
    [Serializable]
    public sealed class MatchAudioClips
    {
        [InspectorName("脚步（含回声片前三声）")] public AudioClip step;
        [InspectorName("灰盒枪声")] public AudioClip shot;
        [InspectorName("道具生效")] public AudioClip utility;
        [InspectorName("回声片尾音")] public AudioClip echoTail;
        [InspectorName("开始安装 / 拆除")] public AudioClip objective;
        public AudioClip devicePulse, deviceBurst;
    }

    internal sealed class SeatAudio : IDisposable
    {
        private readonly AudioSource _source;
        private readonly AudioClip[] _clips;
        private readonly AudioClip _quiet;
        private readonly bool[] _generated = new bool[7];
        private int _seat = -1, _lastEvent = -1;
        private bool _paused;
        public float Volume { get => _source.volume; set => _source.volume=value; }
        public void Stop() { _source.Stop(); _lastEvent=-1; _seat=-1; }

        public SeatAudio(AudioSource source, MatchAudioClips bank, MatchPresentation preset)
        {
            _source = source; source.playOnAwake = false; source.spatialBlend = 0;
            _clips = new[] { bank.step, preset && preset.shotSound ? preset.shotSound : bank.shot, bank.utility, bank.echoTail, bank.objective, bank.devicePulse, bank.deviceBurst };
            _quiet = preset ? preset.suppressedSound : null;
            float[] frequencies = { 105, 72, 340, 1540, 780, 1210, 68 };
            for (int i = 0; i < _clips.Length; i++)
            {
                if (_clips[i] != null) continue;
                const int rate = 22050;
                bool burst = i == (int)SoundKind.DeviceBurst;
                float low = 0;
                var samples = new float[burst ? 18000 : i == (int)SoundKind.EchoTail ? 1200 : 2400];
                var noise = new System.Random(303 + i);
                for (int k = 0; k < samples.Length; k++)
                {
                    float t = k / (float)rate;
                    float tone = Mathf.Sin(2 * Mathf.PI * frequencies[i] * t);
                    float mix = i < 2 ? 0.28f : 0.06f;
                    if (burst) { low = .91f*low + .09f*(float)(2*noise.NextDouble()-1); samples[k] = Mathf.Exp(-5.7f*t)*(low*1.9f + .4f*Mathf.Sin(2*Mathf.PI*(54f*t+3.2f*(1-Mathf.Exp(-12*t))))); continue; }
                    samples[k] = Mathf.Exp(-38 * t) * (tone * (1 - mix) + mix * (float)(2 * noise.NextDouble() - 1)) * 0.4f;
                }
                _clips[i] = AudioClip.Create("Prototype " + (SoundKind)i, samples.Length, 1, rate, false);
                _clips[i].SetData(samples, 0); _generated[i] = true;
            }
        }

        public void Present(in SeatView view)
        {
            if (view.Match.Paused != _paused)
            {
                _paused = view.Match.Paused;
                if (_paused) _source.Pause(); else _source.UnPause();
            }
            if (view.Seat != _seat) { _source.Stop(); _seat = view.Seat; _lastEvent = -1; }
            if (_paused || !view.Heard.HasValue) return;
            var clue = view.Heard.Value;
            if (clue.Tick == _lastEvent || view.Match.Tick - clue.Tick > 4) return;
            _lastEvent = clue.Tick;
            _source.panStereo = clue.Own ? 0 : Mathf.Sin((clue.Bearing * 45f - view.Yaw) * Mathf.Deg2Rad) * 0.7f;
            _source.PlayOneShot(clue.Kind == SoundKind.Gun && clue.Suppressed && _quiet ? _quiet : _clips[(int)clue.Kind], 0.5f);
        }
        public void Dispose()
        {
            for (int i = 0; i < _clips.Length; i++) if (_generated[i]) UnityEngine.Object.Destroy(_clips[i]);
        }
    }
}
