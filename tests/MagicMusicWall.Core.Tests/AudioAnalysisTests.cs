using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class AudioAnalysisTests
{
    [Fact]
    public void SteadyToneHasExpectedPitchClassCentroidAndLevelWithoutInventedBeat()
    {
        var result = AudioFeatureExtractor.Extract(Tone(440, 5, .5));
        Assert.Equal(26, result.Mfcc.Length);
        Assert.All(result.Mfcc, value => Assert.True(double.IsFinite(value)));
        Assert.All(result.Mfcc.Skip(13), value => Assert.True(value >= 0));
        Assert.Equal(12, result.Chroma.Length);
        Assert.InRange(result.Chroma.Sum(), .999999, 1.000001);
        Assert.Equal(9, Array.IndexOf(result.Chroma, result.Chroma.Max())); // A
        Assert.InRange(result.Centroid, 430, 450);
        Assert.InRange(result.Rms, .3535, .3536);
        Assert.Equal(0, result.Tempo);
        Assert.Equal(0, result.BeatStrength);
    }

    [Fact]
    public void OctaveChangesBrightnessWhilePreservingPitchClass()
    {
        var low = AudioFeatureExtractor.Extract(Tone(220, 2));
        var high = AudioFeatureExtractor.Extract(Tone(880, 2));
        Assert.Equal(9, Array.IndexOf(low.Chroma, low.Chroma.Max()));
        Assert.Equal(9, Array.IndexOf(high.Chroma, high.Chroma.Max()));
        Assert.InRange(low.Centroid, 210, 230);
        Assert.InRange(high.Centroid, 870, 890);
        Assert.NotEqual(low.Mfcc[0], high.Mfcc[0]);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(90)]
    public void RepeatedPercussiveBurstsHaveAnEstimatedTempo(double bpm)
    {
        var result = AudioFeatureExtractor.Extract(Pulses(bpm, 12));
        Assert.InRange(result.Tempo, bpm - 5, bpm + 5);
        Assert.InRange(result.BeatStrength, .5, 1);
    }

    [Fact]
    public void ShortExcerptStillHasTimbreButNoTempoEstimate()
    {
        var result = AudioFeatureExtractor.Extract(Pulses(120, 2));
        Assert.Equal(0, result.Tempo);
        Assert.True(result.Rms > 0);
        Assert.All(result.Mfcc, value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public void InvalidSignalIsRejectedAndCancellationIsObserved()
    {
        Assert.Throws<ArgumentNullException>(() => AudioFeatureExtractor.Extract(null!));
        Assert.Throws<ArgumentException>(() => AudioFeatureExtractor.Extract(new float[AudioFeatureExtractor.SampleRate - 1]));
        Assert.Throws<ArgumentException>(() => AudioFeatureExtractor.Extract(new float[AudioFeatureExtractor.SampleRate]));
        Assert.Throws<ArgumentException>(() => AudioFeatureExtractor.Extract(Enumerable.Repeat(.2f, AudioFeatureExtractor.SampleRate).ToArray()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => AudioFeatureExtractor.Extract(Tone(440, 1), cancellation.Token));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(5f)]
    public void NonFiniteOrMalformedPcmIsRejected(float invalid)
    {
        var samples = Tone(440, 1); samples[9000] = invalid;
        Assert.Throws<ArgumentException>(() => AudioFeatureExtractor.Extract(samples));
    }

    private static float[] Tone(double hz, int seconds, double amplitude = .4) => Enumerable.Range(0, AudioFeatureExtractor.SampleRate * seconds)
        .Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / AudioFeatureExtractor.SampleRate))).ToArray();

    private static float[] Pulses(double bpm, int seconds)
    {
        var random = new Random(42);
        var samples = new float[AudioFeatureExtractor.SampleRate * seconds];
        for (var i = 0; i < samples.Length; i++)
        {
            var withinBeat = (i / (double)AudioFeatureExtractor.SampleRate) % (60 / bpm);
            if (withinBeat < .06) samples[i] = (float)((random.NextDouble() * 2 - 1) * .8 * Math.Exp(-withinBeat * 70));
        }
        return samples;
    }
}
