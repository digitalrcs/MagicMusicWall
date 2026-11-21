using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace MagicMusicWall.App;

/// <summary>Pass file-open requests to the instance owning the same library.</summary>
internal static class FileActivationPipe
{
    private const int MaximumMessageBytes = 1024 * 1024;

    public static async Task SendAsync(string name, string[] paths)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        var message = JsonSerializer.SerializeToUtf8Bytes(paths);
        if (message.Length > MaximumMessageBytes) throw new IOException("Too many files were selected.");
        await pipe.WriteAsync(BitConverter.GetBytes(message.Length), timeout.Token);
        await pipe.WriteAsync(message, timeout.Token);
        await pipe.FlushAsync(timeout.Token);
    }

    public static async Task ListenAsync(string name, Action<string[]> receive, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var header = new byte[4];
                await pipe.ReadExactlyAsync(header, timeout.Token);
                var length = BitConverter.ToInt32(header);
                if (length <= 0 || length > MaximumMessageBytes) continue;
                var message = new byte[length];
                await pipe.ReadExactlyAsync(message, timeout.Token);
                var paths = JsonSerializer.Deserialize<string[]>(message);
                if (paths is not null && paths.All(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathFullyQualified(p))) receive(paths);
            }
            catch (OperationCanceledException) { if (stop.IsCancellationRequested) return; }
            catch (IOException) { /* A disconnected sender must not stop later file opens. */ }
            catch (JsonException) { }
        }
    }
}
