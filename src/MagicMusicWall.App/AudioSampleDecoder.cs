using System.Buffers.Binary;
using System.IO;
using LibVLCSharp.Shared;

namespace MagicMusicWall.App;

/// <summary>
/// Decodes local audio files to at most 60 seconds of mono, 22050 Hz samples.
/// Uses its own silent VLC instance; it never uses the interactive player.
/// Codec/resampler buffering can shorten the excerpt; its sample count, not
/// the original media duration, determines the duration returned here.
/// Dispose only after all outstanding DecodeAsync calls have completed.
/// </summary>
internal sealed class AudioSampleDecoder : IDisposable
{
    private const int SampleRate = 22050;
    private const int MaximumSamples = 60 * SampleRate;
    private const int MaximumPcmBytes = 62 * SampleRate * sizeof(short);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly LibVLC _vlc;
    private bool _disposed;

    public AudioSampleDecoder()
    {
        // A file-only sout chain provides no display output. Dummy outputs also
        // prevent a malformed/unsupported input from opening an audio device.
        _vlc = new LibVLC("--quiet", "--no-video", "--no-video-title-show", "--no-volume-save",
            "--vout=dummy", "--aout=dummy", "--no-sout-video", "--no-sout-spu", "--no-sout-keep");
    }

    public async Task<float[]> DecodeAsync(string path, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
            throw new ArgumentException("Audio analysis requires an existing file, not a URL.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The audio file does not exist.", fullPath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Play/Stop and native teardown can block briefly; never run them
            // on the caller's UI thread or inside a native VLC event callback.
            return await Task.Run(() => DecodeFileAsync(fullPath, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<float[]> DecodeFileAsync(string path, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = linked.Token;
        var directory = Path.Combine(Path.GetTempPath(), "MagicMusicWall-AudioAnalysis-" + Guid.NewGuid().ToString("N"));
        var pcmPath = Path.Combine(directory, "samples.pcm");
        Directory.CreateDirectory(directory);
        try
        {
            using var media = new Media(_vlc, path, FromType.FromPath);
            // VLC's sout option parser uses backslash escapes inside quotes.
            var destination = pcmPath.Replace('\\', '/').Replace("'", "\\'");
            media.AddOption(":sout=#transcode{acodec=s16l,channels=1,samplerate=22050}:std{access=file,mux=raw,dst='" + destination + "'}");
            media.AddOption(":stop-time=60");
            media.AddOption(":no-video");
            media.AddOption(":no-spu");

            using var player = new MediaPlayer(_vlc);
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnEnd(object? sender, EventArgs args) => completion.TrySetResult(true);
            // Use a result instead of a faulted task, so an error racing with
            // cancellation cannot leave an unobserved task exception behind.
            void OnError(object? sender, EventArgs args) => completion.TrySetResult(false);
            player.EndReached += OnEnd;
            player.EncounteredError += OnError;
            try
            {
                token.ThrowIfCancellationRequested();
                if (!player.Play(media)) throw new InvalidDataException("VLC could not start decoding this audio file.");
                if (!await completion.Task.WaitAsync(token).ConfigureAwait(false))
                    throw new InvalidDataException("VLC could not decode this audio file.");
            }
            finally
            {
                // Stop joins VLC workers and flushes/closes the raw file before
                // it is read or removed. Callbacks above only signal completion.
                player.Stop();
                player.EndReached -= OnEnd;
                player.EncounteredError -= OnError;
            }

            token.ThrowIfCancellationRequested();
            if (!File.Exists(pcmPath)) throw new InvalidDataException("The file did not contain decodable audio.");
            await using var input = new FileStream(pcmPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length == 0 || input.Length % sizeof(short) != 0 || input.Length > MaximumPcmBytes)
                throw new InvalidDataException("VLC returned empty, malformed, or unexpectedly long audio samples.");
            var sampleCount = (int)Math.Min(input.Length / sizeof(short), MaximumSamples);
            var bytes = new byte[sampleCount * sizeof(short)];
            await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * sizeof(short), sizeof(short))) / 32768f;
            }
            return samples;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Audio analysis decoding exceeded the 120-second limit.");
        }
        finally
        {
            // These are the single file and unique empty directory owned by
            // this operation. No source-media or user-managed files are deleted.
            File.Delete(pcmPath);
            Directory.Delete(directory);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vlc.Dispose();
        _gate.Dispose();
    }
}
