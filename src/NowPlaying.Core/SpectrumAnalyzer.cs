namespace NowPlaying.Core;

/// <summary>
/// The bars of the visualizer from the sound of the PC: the last samples, a Hann window, an FFT, bands spaced like
/// the ear hears (log scale, 50 Hz to 16 kHz), levels in decibels from 0 to 1, rising fast and falling gently. The
/// scale follows the loudness of the last seconds (an automatic gain, up to 24 dB): music played quietly still moves
/// the bars, silence and background noise do not. Samples come from the audio thread, levels are read by the UI:
/// every call is guarded.
/// </summary>
public sealed class SpectrumAnalyzer
{
    private const double MinFrequency = 50;
    private const double MaxFrequency = 16000;
    // The top of the scale: the loudest band of the last seconds, 3 dB above it, between these two (loud music is
    // not squeezed further; quiet music is lifted 24 dB at most, more would show the noise of a silent PC).
    private const double LoudestTopDb = -12;
    private const double QuietestTopDb = -36;
    private const double HeadroomDb = 3;
    private const double RangeDb = 48;
    // How fast the scale comes back down after a loud passage: about 1.5 dB a second at the pace of the display.
    private const double ReleaseDbPerCompute = 0.06;

    private readonly int _size;
    private readonly float[] _ring;
    private readonly double[] _window;
    private readonly double[] _re;
    private readonly double[] _im;
    private readonly float[] _smoothed;
    private readonly double[] _bandDb;
    private double _loudestDb = QuietestTopDb - HeadroomDb;
    private readonly object _lock = new();
    private int _written;
    private int _sampleRate;

    public SpectrumAnalyzer(int bars, int fftSize = 2048, int sampleRate = 48000)
    {
        if (fftSize < 64 || (fftSize & (fftSize - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(fftSize), "A power of two, 64 or more.");
        Bars = bars;
        _size = fftSize;
        _sampleRate = sampleRate;
        _ring = new float[fftSize];
        _window = new double[fftSize];
        for (int i = 0; i < fftSize; i++)
            _window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (fftSize - 1)));
        _re = new double[fftSize];
        _im = new double[fftSize];
        _smoothed = new float[bars];
        _bandDb = new double[bars];
    }

    public int Bars { get; }

    /// <summary>The sample rate of the samples given from now on (the output device may change).</summary>
    public int SampleRate
    {
        get { lock (_lock) return _sampleRate; }
        set { lock (_lock) _sampleRate = value > 0 ? value : 48000; }
    }

    /// <summary>Mono samples, -1 to 1, oldest first.</summary>
    public void AddSamples(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            foreach (float s in samples)
            {
                _ring[_written % _size] = s;
                _written = (_written + 1) % (_size * 1024);
            }
        }
    }

    /// <summary>Forgets the sound heard so far (silence, a new device).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_ring);
            Array.Clear(_smoothed);
            _loudestDb = QuietestTopDb - HeadroomDb;
        }
    }

    /// <summary>
    /// Computes the bars from the last samples into <paramref name="levels"/> (<see cref="Bars"/> values, 0 to 1),
    /// smoothed with the previous call: call it at the pace of the display.
    /// </summary>
    public void Compute(Span<float> levels)
    {
        lock (_lock)
        {
            int start = _written % _size;
            for (int i = 0; i < _size; i++)
            {
                _re[i] = _ring[(start + i) % _size] * _window[i];
                _im[i] = 0;
            }
            Fft(_re, _im);

            double binHz = (double)_sampleRate / _size;
            double top = Math.Min(MaxFrequency, _sampleRate / 2.0);
            double ratio = Math.Pow(top / MinFrequency, 1.0 / Bars);
            double loudest = double.MinValue;
            for (int b = 0; b < Bars; b++)
            {
                double lo = MinFrequency * Math.Pow(ratio, b), hi = lo * ratio;
                int first = Math.Max(1, (int)Math.Floor(lo / binHz));
                int last = Math.Max(first, Math.Min(_size / 2 - 1, (int)Math.Ceiling(hi / binHz)));
                double peak = 0;
                for (int k = first; k <= last; k++)
                    peak = Math.Max(peak, Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]));
                // Amplitude of a full-scale sine is about size / 4 with a Hann window.
                _bandDb[b] = 20 * Math.Log10(peak / (_size / 4.0) + 1e-12);
                loudest = Math.Max(loudest, _bandDb[b]);
            }

            // Up at once with a louder sound, down slowly after it.
            _loudestDb = Math.Max(loudest, Math.Max(QuietestTopDb - HeadroomDb, _loudestDb - ReleaseDbPerCompute));
            double scaleTop = Math.Clamp(_loudestDb + HeadroomDb, QuietestTopDb, LoudestTopDb);
            double scaleBottom = scaleTop - RangeDb;
            for (int b = 0; b < Bars && b < levels.Length; b++)
            {
                float level = (float)Math.Clamp((_bandDb[b] - scaleBottom) / RangeDb, 0, 1);
                float previous = _smoothed[b];
                _smoothed[b] = level > previous ? previous + (level - previous) * 0.7f : previous * 0.86f + level * 0.14f;
                levels[b] = _smoothed[b];
            }
        }
    }

    // In-place radix-2 Cooley–Tukey.
    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2 * Math.PI / len;
            double wr = Math.Cos(angle), wi = Math.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double next = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = next;
                }
            }
        }
    }
}

/// <summary>The "Animation" visualizer: calm waves that cost nothing, frozen at the bottom when paused.</summary>
public static class DecorativeBars
{
    /// <summary>Fills <paramref name="levels"/> for the time <paramref name="seconds"/> (any clock that goes on).</summary>
    public static void Fill(Span<float> levels, double seconds)
    {
        for (int i = 0; i < levels.Length; i++)
        {
            double wave = Math.Sin(seconds * 2.4 + i * 0.6) * 0.5 + Math.Sin(seconds * 1.3 + i * 1.7) * 0.3;
            levels[i] = (float)Math.Clamp(0.42 + 0.38 * wave * (1 - 0.4 * i / Math.Max(1, levels.Length)), 0.05, 1);
        }
    }
}
