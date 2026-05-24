using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SoundCheck.Services;

/// <summary>
/// Audio playback with optional crossfade between tracks. Internally each track
/// is a "voice" (output + reader + FFT tap); during a crossfade two voices play
/// at once while their volumes ramp in opposite directions.
/// </summary>
public class AudioPlayer : IDisposable
{
    private sealed class Voice
    {
        public required IWavePlayer Output;
        public required AudioFileReader Reader;
        public required FftCaptureProvider Fft;
        public required EqualizerSampleProvider Eq;

        public void Dispose()
        {
            try { Output.Stop(); } catch { }
            try { Output.Dispose(); } catch { }
            try { Reader.Dispose(); } catch { }
        }
    }

    private Voice? _voice;        // current track
    private Voice? _fading;       // outgoing track during a crossfade
    private System.Threading.Timer? _fadeTimer;
    private float _volumeLinear = 0.8f;
    private bool _muted;

    // Equalizer state — applied to every voice's chain and updatable live.
    private bool _eqEnabled;
    private readonly float[] _eqGains = new float[EqualizerSampleProvider.BandCount];

    public bool IsPlaying => _voice?.Output.PlaybackState == PlaybackState.Playing;
    public bool IsPaused  => _voice?.Output.PlaybackState == PlaybackState.Paused;
    public TimeSpan Position => _voice?.Reader.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _voice?.Reader.TotalTime ?? TimeSpan.Zero;

    public event Action? PlaybackEnded;

    private float TargetVolume => _muted ? 0f : MathF.Pow(_volumeLinear, 2.5f);

    /// <summary>
    /// Load and prepare <paramref name="path"/>. If <paramref name="crossfadeSeconds"/> &gt; 0
    /// and a track is currently playing, the new track fades in over the old one;
    /// otherwise the current track is replaced immediately (caller then calls Play).
    /// </summary>
    public void Load(string path, double crossfadeSeconds = 0)
    {
        bool doXf = crossfadeSeconds > 0.05 && _voice != null && IsPlaying;
        var v = CreateVoice(path);

        if (doXf)
        {
            KillFading();                 // drop any earlier outgoing voice instantly
            _fading = _voice;             // current becomes outgoing
            _voice = v;
            v.Reader.Volume = 0f;
            v.Output.Play();
            StartFade(crossfadeSeconds);
        }
        else
        {
            var old = _voice;
            _voice = v;
            v.Reader.Volume = TargetVolume;
            KillFading();
            old?.Dispose();               // its PlaybackStopped won't fire ended (old != _voice)
            // caller calls Play()
        }
    }

    private Voice CreateVoice(string path)
    {
        var reader = new AudioFileReader(path);
        // reader → equalizer → FFT tap → output, so the visualizer reflects EQ.
        var eq = new EqualizerSampleProvider(reader.ToSampleProvider());
        eq.Configure(_eqEnabled, _eqGains);
        var fft = new FftCaptureProvider(eq);
        var output = new WaveOutEvent();
        var voice = new Voice { Output = output, Reader = reader, Fft = fft, Eq = eq };
        output.PlaybackStopped += (_, _) =>
        {
            // Only the *current* voice finishing naturally should advance the queue.
            if (voice == _voice && reader.TotalTime > TimeSpan.Zero
                && reader.CurrentTime >= reader.TotalTime - TimeSpan.FromMilliseconds(250))
            {
                PlaybackEnded?.Invoke();
            }
        };
        output.Init(fft.ToWaveProvider());
        return voice;
    }

    // ─── Crossfade ramp ───────────────────────────────────────────────────
    private DateTime _fadeStart;
    private double _fadeDurMs;
    private float _fadeFromVol;

    private void StartFade(double seconds)
    {
        _fadeStart = DateTime.UtcNow;
        _fadeDurMs = seconds * 1000.0;
        _fadeFromVol = _fading?.Reader.Volume ?? TargetVolume;
        _fadeTimer?.Dispose();
        _fadeTimer = new System.Threading.Timer(_ => FadeTick(), null, 0, 30);
    }

    private void FadeTick()
    {
        double t = Math.Clamp((DateTime.UtcNow - _fadeStart).TotalMilliseconds / _fadeDurMs, 0, 1);
        try
        {
            if (_voice != null) _voice.Reader.Volume = (float)(TargetVolume * t);
            if (_fading != null) _fading.Reader.Volume = (float)(_fadeFromVol * (1 - t));
        }
        catch { }
        if (t >= 1.0)
        {
            try { if (_voice != null) _voice.Reader.Volume = TargetVolume; } catch { }
            KillFading();
            _fadeTimer?.Dispose();
            _fadeTimer = null;
        }
    }

    private void KillFading()
    {
        _fadeTimer?.Dispose();
        _fadeTimer = null;
        var f = _fading;
        _fading = null;
        f?.Dispose();
    }

    /// <summary>Returns N magnitudes (0..1) from the latest FFT capture of the current track.</summary>
    public float[] GetFftBars(int bands = 24)
        => _voice?.Fft.GetBands(bands) ?? new float[bands];

    /// <summary>Enable/disable the equalizer and set its 10 band gains (dB). Applies live.</summary>
    public void SetEqualizer(bool enabled, float[] gainsDb)
    {
        _eqEnabled = enabled;
        for (int i = 0; i < _eqGains.Length; i++)
            _eqGains[i] = i < gainsDb.Length ? gainsDb[i] : 0f;
        _voice?.Eq.Configure(enabled, _eqGains);
        _fading?.Eq.Configure(enabled, _eqGains);
    }

    public void Play() => _voice?.Output.Play();
    public void Pause() => _voice?.Output.Pause();

    public void TogglePlay()
    {
        if (_voice == null) return;
        if (IsPlaying) Pause(); else Play();
    }

    public void Stop()
    {
        KillFading();
        var v = _voice;
        _voice = null;
        v?.Dispose();
    }

    public void Seek(double fraction)
    {
        if (_voice == null) return;
        var t = TimeSpan.FromSeconds(Duration.TotalSeconds * Math.Clamp(fraction, 0, 1));
        try { _voice.Reader.CurrentTime = t; } catch { }
    }

    /// <summary>Slider position 0..1 (pow(2.5) curve applied internally).</summary>
    public float Volume
    {
        get => _volumeLinear;
        set { _volumeLinear = Math.Clamp(value, 0f, 1f); ApplyVolume(); }
    }

    public bool IsMuted => _muted;
    public void ToggleMute() { _muted = !_muted; ApplyVolume(); }

    private void ApplyVolume()
    {
        // Don't fight an in-progress crossfade — the fade timer owns volumes then.
        if (_fadeTimer != null) return;
        try { if (_voice != null) _voice.Reader.Volume = TargetVolume; } catch { }
    }

    public void Dispose()
    {
        KillFading();
        Stop();
    }
}
