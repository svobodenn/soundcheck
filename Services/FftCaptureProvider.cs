using NAudio.Dsp;
using NAudio.Wave;

namespace SoundCheck.Services;

/// <summary>
/// Pass-through ISampleProvider that captures the last block of samples and
/// runs an FFT on them so the UI can render a spectrum visualization.
/// </summary>
public class FftCaptureProvider : ISampleProvider
{
    private const int FftSize = 1024;          // power of 2
    private static readonly int FftBits = (int)Math.Log2(FftSize);

    private readonly ISampleProvider _source;
    private readonly Complex[] _buffer = new Complex[FftSize];
    private readonly float[] _mags = new float[FftSize / 2];
    private int _writePos;
    private readonly object _lock = new();

    public WaveFormat WaveFormat => _source.WaveFormat;

    public FftCaptureProvider(ISampleProvider source) { _source = source; }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        // Mix stereo down to mono and feed into the rolling FFT buffer
        int ch = WaveFormat.Channels;
        lock (_lock)
        {
            for (int i = 0; i < read; i += ch)
            {
                float s = buffer[offset + i];
                if (ch == 2) s = (s + buffer[offset + i + 1]) * 0.5f;
                // Hann window applied at FFT time, just store raw here
                _buffer[_writePos].X = s;
                _buffer[_writePos].Y = 0;
                _writePos = (_writePos + 1) % FftSize;
            }
        }
        return read;
    }

    /// <summary>Compute magnitudes and return logarithmically grouped bands (0..1).</summary>
    public float[] GetBands(int bands)
    {
        var result = new float[bands];
        Complex[] copy;
        lock (_lock)
        {
            copy = new Complex[FftSize];
            // Reorder so most recent sample is at the end (improves spectrogram stability)
            for (int i = 0; i < FftSize; i++)
            {
                int idx = (_writePos + i) % FftSize;
                // Apply Hann window: 0.5 * (1 - cos(2π·i/(N-1)))
                float w = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
                copy[i].X = _buffer[idx].X * w;
                copy[i].Y = 0;
            }
        }
        FastFourierTransform.FFT(true, FftBits, copy);
        // Magnitude of each bin (only first half — symmetric)
        for (int i = 0; i < _mags.Length; i++)
        {
            float re = copy[i].X, im = copy[i].Y;
            _mags[i] = MathF.Sqrt(re * re + im * im);
        }
        // Log-bucket into `bands` groups (bass on left, treble on right)
        int binCount = _mags.Length;
        for (int b = 0; b < bands; b++)
        {
            double lo = Math.Pow(binCount, (double)b / bands);
            double hi = Math.Pow(binCount, (double)(b + 1) / bands);
            int iLo = Math.Clamp((int)lo, 1, binCount - 1);
            int iHi = Math.Clamp((int)Math.Ceiling(hi), iLo + 1, binCount);
            float max = 0;
            for (int i = iLo; i < iHi; i++) if (_mags[i] > max) max = _mags[i];
            // Normalize: rough dB scale, clamp to 0..1
            float db = 20f * MathF.Log10(max + 1e-6f);
            // Map -60..0 dB to 0..1
            float v = Math.Clamp((db + 60f) / 60f, 0f, 1f);
            result[b] = v;
        }
        return result;
    }
}
