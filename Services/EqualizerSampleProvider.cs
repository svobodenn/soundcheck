using NAudio.Dsp;
using NAudio.Wave;

namespace SoundCheck.Services;

/// <summary>
/// 10-band graphic equalizer (ISO octave centres) built on peaking BiQuad
/// filters, one set per channel. Gains are in dB (−12..+12). When disabled or
/// all gains are zero the audio passes through untouched. Gains can be changed
/// live while audio is playing.
/// </summary>
public class EqualizerSampleProvider : ISampleProvider
{
    public static readonly float[] Centres =
        { 31f, 62f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f };
    public const int BandCount = 10;
    private const float Q = 1.1f;

    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly BiQuadFilter[,] _filters; // [channel, band]
    private readonly float[] _gains = new float[BandCount];
    private volatile bool _enabled;
    private volatile bool _active; // enabled AND at least one non-zero gain

    public EqualizerSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;
        _filters = new BiQuadFilter[_channels, BandCount];
        for (int ch = 0; ch < _channels; ch++)
            for (int b = 0; b < BandCount; b++)
                _filters[ch, b] = BiQuadFilter.PeakingEQ(_sampleRate, Centres[b], Q, 0f);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Update enabled state and all 10 band gains (dB). Safe to call while playing.</summary>
    public void Configure(bool enabled, float[] gainsDb)
    {
        _enabled = enabled;
        bool anyNonZero = false;
        for (int b = 0; b < BandCount; b++)
        {
            float g = b < gainsDb.Length ? gainsDb[b] : 0f;
            _gains[b] = g;
            if (Math.Abs(g) > 0.01f) anyNonZero = true;
            for (int ch = 0; ch < _channels; ch++)
                _filters[ch, b].SetPeakingEq(_sampleRate, Centres[b], Q, g);
        }
        _active = enabled && anyNonZero;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (!_active) return read; // bypass — no filtering cost when flat/disabled

        for (int n = 0; n < read; n++)
        {
            int ch = n % _channels;
            float s = buffer[offset + n];
            for (int b = 0; b < BandCount; b++)
                s = _filters[ch, b].Transform(s);
            buffer[offset + n] = s;
        }
        return read;
    }
}
