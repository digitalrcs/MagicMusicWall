namespace MagicMusicWall.Core;

public sealed record AudioFeatures(double[] Mfcc, double[] Chroma, double Tempo, double BeatStrength, double Rms, double Centroid);
public sealed record AudioFeatureSummary(int AnalyzedTracks, int TotalTracks, double Tempo, double Rms);
public enum MusicMapSimilarity { Metadata, Sound, Blended }

/// <summary>
/// Compact descriptors of up to sixty seconds of normalized mono PCM. MFCC contains
/// the means then population standard deviations of DCT coefficients 1–13 (C0 omitted).
/// Chroma is a twelve-bin, sum-normalized pitch-class energy profile, C through B.
/// Tempo is an approximate 40–240 BPM periodicity estimate; zero means insufficient
/// rhythmic evidence. BeatStrength is periodicity confidence, not a musical judgment.
/// </summary>
public static class AudioFeatureExtractor
{
    public const int SampleRate = 22050;
    public const int AlgorithmVersion = 1;
    private const int Window = 2048, Hop = 512, MelCount = 40, Coefficients = 13;
    private static readonly double[] Hann = Enumerable.Range(0, Window).Select(i => .5 - .5 * Math.Cos(2 * Math.PI * i / (Window - 1))).ToArray();
    private static readonly double[][] MelWeights = CreateMelWeights();
    private static readonly double[][] Cosines = Enumerable.Range(1, Coefficients).Select(k => Enumerable.Range(0, MelCount)
        .Select(m => Math.Sqrt(2.0 / MelCount) * Math.Cos(Math.PI * k * (m + .5) / MelCount)).ToArray()).ToArray();

    public static AudioFeatures Extract(float[] samples, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        cancellationToken.ThrowIfCancellationRequested();
        if (samples.Length < SampleRate) throw new ArgumentException("At least one second of mono PCM is required.", nameof(samples));
        var length = Math.Min(samples.Length, SampleRate * 60);
        double sum = 0, squared = 0;
        for (var i = 0; i < length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var sample = samples[i];
            if (!float.IsFinite(sample) || Math.Abs(sample) > 4)
                throw new ArgumentException("Audio samples must be finite normalized PCM values.", nameof(samples));
            sum += sample; squared += (double)sample * sample;
        }
        var mean = sum / length;
        var rms = Math.Sqrt(Math.Max(0, squared / length - mean * mean));
        if (rms < .000001) throw new ArgumentException("This audio segment is silent or has no usable signal.", nameof(samples));

        var frameCount = 1 + (length - Window) / Hop;
        var real = new double[Window]; var imaginary = new double[Window];
        var power = new double[Window / 2 + 1]; var previousMagnitude = new double[power.Length];
        var mel = new double[MelCount]; var coefficientMean = new double[Coefficients]; var coefficientM2 = new double[Coefficients];
        var chroma = new double[12]; var onset = new double[frameCount];
        double spectralEnergy = 0, spectralMoment = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = frame * Hop;
            for (var i = 0; i < Window; i++) { real[i] = (samples[offset + i] - mean) * Hann[i]; imaginary[i] = 0; }
            Fft(real, imaginary);
            double flux = 0;
            for (var bin = 0; bin < power.Length; bin++)
            {
                var p = (real[bin] * real[bin] + imaginary[bin] * imaginary[bin]) / (Window * Window);
                power[bin] = p;
                var magnitude = Math.Sqrt(p);
                if (frame > 0) flux += Math.Max(0, magnitude - previousMagnitude[bin]);
                previousMagnitude[bin] = magnitude;
                var hz = (double)bin * SampleRate / Window;
                spectralEnergy += magnitude; spectralMoment += hz * magnitude;
                if (hz is < 50 or > 5000) continue;
                var midi = 69 + 12 * Math.Log2(hz / 440);
                var lower = (int)Math.Floor(midi); var fraction = midi - lower;
                chroma[(lower % 12 + 12) % 12] += p * (1 - fraction);
                chroma[((lower + 1) % 12 + 12) % 12] += p * fraction;
            }
            onset[frame] = flux;
            for (var filter = 0; filter < MelCount; filter++)
            {
                double energy = 0;
                for (var bin = 0; bin < power.Length; bin++) energy += MelWeights[filter][bin] * power[bin];
                mel[filter] = Math.Log(Math.Max(energy, 1e-12));
            }
            for (var coefficient = 0; coefficient < Coefficients; coefficient++)
            {
                double value = 0;
                for (var filter = 0; filter < MelCount; filter++) value += Cosines[coefficient][filter] * mel[filter];
                var delta = value - coefficientMean[coefficient];
                coefficientMean[coefficient] += delta / (frame + 1);
                coefficientM2[coefficient] += delta * (value - coefficientMean[coefficient]);
            }
        }
        var mfcc = coefficientMean.Concat(coefficientM2.Select(x => Math.Sqrt(Math.Max(0, x / frameCount)))).ToArray();
        var chromaSum = chroma.Sum();
        if (chromaSum > 0) for (var i = 0; i < chroma.Length; i++) chroma[i] /= chromaSum;
        var (tempo, strength) = EstimateTempo(onset, spectralEnergy, cancellationToken);
        return new(mfcc, chroma, tempo, strength, rms, spectralMoment / Math.Max(spectralEnergy, 1e-12));
    }

    private static (double Tempo, double Strength) EstimateTempo(double[] flux, double energy, CancellationToken cancellationToken)
    {
        var framesPerSecond = (double)SampleRate / Hop;
        // Short excerpts and almost-stationary tones do not justify a tempo estimate.
        if (flux.Length < framesPerSecond * 4 || flux.Sum() / Math.Max(energy, 1e-12) < .001) return (0, 0);
        var envelope = new double[flux.Length];
        var radius = (int)(framesPerSecond / 4);
        var prefix = new double[flux.Length + 1];
        for (var i = 0; i < flux.Length; i++) prefix[i + 1] = prefix[i] + flux[i];
        for (var i = 0; i < flux.Length; i++)
        {
            var left = Math.Max(0, i - radius); var right = Math.Min(flux.Length, i + radius + 1);
            envelope[i] = Math.Max(0, flux[i] - (prefix[right] - prefix[left]) / (right - left));
        }
        var average = envelope.Average();
        for (var i = 0; i < envelope.Length; i++) envelope[i] -= average;
        double bestScore = 0, bestCorrelation = 0, bestLag = 0;
        var minimumLag = (int)Math.Ceiling(framesPerSecond * 60 / 240);
        var maximumLag = Math.Min(flux.Length / 3, (int)Math.Floor(framesPerSecond * 60 / 40));
        var correlations = new double[maximumLag + 2];
        for (var lag = minimumLag - 1; lag <= maximumLag + 1; lag++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double product = 0, leftEnergy = 0, rightEnergy = 0;
            for (var i = lag; i < envelope.Length; i++)
            {
                product += envelope[i] * envelope[i - lag];
                leftEnergy += envelope[i] * envelope[i]; rightEnergy += envelope[i - lag] * envelope[i - lag];
            }
            correlations[lag] = Math.Clamp(product / Math.Max(1e-20, Math.Sqrt(leftEnergy * rightEnergy)), 0, 1);
        }
        for (var lag = minimumLag; lag <= maximumLag; lag++)
        {
            var correlation = correlations[lag];
            if (correlation < correlations[lag - 1] || correlation < correlations[lag + 1]) continue;
            // Refine a local peak between frames so a fractional beat period is not
            // systematically beaten by its integer-frame half-time harmonic.
            var curvature = correlations[lag - 1] - 2 * correlation + correlations[lag + 1];
            var offset = curvature < -1e-12
                ? Math.Clamp(.5 * (correlations[lag - 1] - correlations[lag + 1]) / curvature, -.5, .5) : 0;
            correlation = Math.Clamp(correlation - .25 * (correlations[lag - 1] - correlations[lag + 1]) * offset, 0, 1);
            var refinedLag = lag + offset;
            var bpm = framesPerSecond * 60 / refinedLag;
            var octaveDistance = Math.Log2(bpm / 120);
            // A broad prior reduces half-time ties; metrical ambiguity remains possible.
            var score = correlation * (.7 + .3 * Math.Exp(-.5 * octaveDistance * octaveDistance));
            if (score > bestScore) { bestScore = score; bestCorrelation = correlation; bestLag = refinedLag; }
        }
        return bestLag == 0 || bestCorrelation < .2 ? (0, 0) : (framesPerSecond * 60 / bestLag, bestCorrelation);
    }

    private static double[][] CreateMelWeights()
    {
        static double Mel(double hz) => 2595 * Math.Log10(1 + hz / 700);
        static double Hertz(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);
        var low = Mel(20); var high = Mel(SampleRate / 2.0);
        var boundaries = Enumerable.Range(0, MelCount + 2).Select(i => Hertz(low + (high - low) * i / (MelCount + 1))).ToArray();
        return Enumerable.Range(0, MelCount).Select(m => Enumerable.Range(0, Window / 2 + 1).Select(bin =>
        {
            var hz = (double)bin * SampleRate / Window;
            var rising = (hz - boundaries[m]) / (boundaries[m + 1] - boundaries[m]);
            var falling = (boundaries[m + 2] - hz) / (boundaries[m + 2] - boundaries[m + 1]);
            return Math.Max(0, Math.Min(rising, falling));
        }).ToArray()).ToArray();
    }

    private static void Fft(double[] real, double[] imaginary)
    {
        for (int i = 1, j = 0; i < Window; i++)
        {
            var bit = Window >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (real[i], real[j]) = (real[j], real[i]); (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]); }
        }
        for (var size = 2; size <= Window; size <<= 1)
        {
            var angle = -2 * Math.PI / size; var stepReal = Math.Cos(angle); var stepImaginary = Math.Sin(angle);
            for (var start = 0; start < Window; start += size)
            {
                double unitReal = 1, unitImaginary = 0;
                for (var k = 0; k < size / 2; k++)
                {
                    var even = start + k; var odd = even + size / 2;
                    var oddReal = real[odd] * unitReal - imaginary[odd] * unitImaginary;
                    var oddImaginary = real[odd] * unitImaginary + imaginary[odd] * unitReal;
                    real[odd] = real[even] - oddReal; imaginary[odd] = imaginary[even] - oddImaginary;
                    real[even] += oddReal; imaginary[even] += oddImaginary;
                    var nextReal = unitReal * stepReal - unitImaginary * stepImaginary;
                    unitImaginary = unitReal * stepImaginary + unitImaginary * stepReal; unitReal = nextReal;
                }
            }
        }
    }
}
