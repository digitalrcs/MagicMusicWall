using MagicMusicWall.App;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public class FileActivationPipeTests
{
    [Fact]
    public async Task RunningInstanceReceivesSuccessiveRequestsIncludingUnicode()
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = "MagicMusicWall-test-" + Guid.NewGuid().ToString("N");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var received = System.Threading.Channels.Channel.CreateUnbounded<string[]>();
        var listener = FileActivationPipe.ListenAsync(name, paths => received.Writer.TryWrite(paths), stop.Token);
        try
        {
            foreach (var paths in new[] { new[] { Path.GetFullPath("Björk song.wav") }, Array.Empty<string>(), new[] { Path.GetFullPath("second.flac") } })
            {
                await FileActivationPipe.SendAsync(name, paths);
                Assert.Equal(paths, await received.Reader.ReadAsync(stop.Token));
            }
        }
        finally { stop.Cancel(); await listener; }
    }
}
