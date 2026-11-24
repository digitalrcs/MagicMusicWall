using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class ListeningSessionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mmw-session-test-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public void DuplicateCurrentOccurrenceRetainsItsPositionAfterRemovedTracksArePruned()
    {
        var session = new ListeningSession { ItemIds = ["gone", "a", "b", "a"], CurrentIndex = 3, PositionMilliseconds = 4567, WasPlaying = true };
        var restored = session.Resolve(["a", "b"]);
        Assert.Equal(new[] { "a", "b", "a" }, restored.ItemIds);
        Assert.Equal(2, restored.CurrentIndex);
        Assert.Equal(4567, restored.PositionMilliseconds);
        Assert.True(restored.WasPlaying);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    public void RemovedCurrentChoosesNextSurvivorOrLastAndDoesNotAutoplay(int current, int expected)
    {
        var restored = new ListeningSession { ItemIds = ["x", "a", "x", "b", "x"], CurrentIndex = current, PositionMilliseconds = 80, WasPlaying = true }.Resolve(["a", "b"]);
        Assert.Equal(expected, restored.CurrentIndex);
        Assert.Equal(0, restored.PositionMilliseconds);
        Assert.False(restored.WasPlaying);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void InvalidCurrentRemainsUnstarted(int index)
    {
        var restored = new ListeningSession { ItemIds = ["a", "b"], CurrentIndex = index, PositionMilliseconds = 90, WasPlaying = true }.Resolve(["a", "b"]);
        Assert.Equal(-1, restored.CurrentIndex);
        Assert.Equal(0, restored.PositionMilliseconds);
        Assert.False(restored.WasPlaying);
    }

    [Fact]
    public void MissingCatalogEntriesCannotBeResurrected()
    {
        var restored = new ListeningSession { ItemIds = ["a", "a"], CurrentIndex = 1, PositionMilliseconds = 80 }.Resolve([]);
        Assert.Empty(restored.ItemIds); Assert.Equal(-1, restored.CurrentIndex); Assert.Equal(0, restored.PositionMilliseconds);
    }

    [Fact]
    public void NegativePositionIsClamped()
    {
        var restored = new ListeningSession { ItemIds = ["a"], CurrentIndex = 0, PositionMilliseconds = -90 }.Resolve(["a"]);
        Assert.Equal(0, restored.PositionMilliseconds);
    }

    [Fact]
    public void OversizedPositionCannotOverflowTimeDisplay()
    {
        var restored = new ListeningSession { ItemIds = ["a"], CurrentIndex = 0, PositionMilliseconds = long.MaxValue }.Resolve(["a"]);
        Assert.Equal(ListeningSession.MaximumPositionMilliseconds, restored.PositionMilliseconds);
    }

    [Fact]
    public void AtomicRoundTripPreservesOrderDuplicatesAndState()
    {
        var store = new ListeningSessionStore(_directory);
        store.Save(new() { ItemIds = ["a", "b", "a"], CurrentIndex = 2, PositionMilliseconds = 1234, WasPlaying = true });
        var restored = Assert.IsType<ListeningSession>(store.Load());
        Assert.Equal(new[] { "a", "b", "a" }, restored.ItemIds); Assert.Equal(2, restored.CurrentIndex);
        Assert.Equal(1234, restored.PositionMilliseconds); Assert.True(restored.WasPlaying);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void CorruptCheckpointRecoversPreviousValidBackupAndPreservesCorruption()
    {
        var store = new ListeningSessionStore(_directory);
        store.Save(new() { ItemIds = ["a", "a"], CurrentIndex = 1 });
        store.Save(new() { ItemIds = ["b"], CurrentIndex = 0 });
        File.WriteAllText(Path.Combine(_directory, "session.json"), "broken");
        Assert.Equal(new[] { "a", "a" }, store.Load()!.ItemIds);
        Assert.Contains("backup", store.LoadWarning);
        store.Save(new() { ItemIds = ["c"], CurrentIndex = 0 });
        Assert.Equal("broken", File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "*.corrupt.*"))));
        Assert.Equal(new[] { "c" }, store.Load()!.ItemIds);
    }

    [Fact]
    public void EmptyCheckpointDoesNotRestorePreviouslyClearedQueue()
    {
        var store = new ListeningSessionStore(_directory);
        store.Save(new() { ItemIds = ["a"], CurrentIndex = 0 });
        store.Save(new());
        Assert.Empty(store.Load()!.ItemIds);
    }

    [Theory]
    [InlineData(3, 0, 5, 2)]
    [InlineData(3, 5, 0, 4)]
    [InlineData(3, 3, 0, 0)]
    [InlineData(3, 0, 2, 3)]
    [InlineData(-1, 0, 2, -1)]
    public void MovingQueueEntryTracksCurrentOccurrenceRatherThanSongId(int current, int from, int to, int expected)
        => Assert.Equal(expected, QueueEditing.MovedCurrentIndex(current, from, to));

    [Fact]
    public void RepeatOneReplaysNaturalEndButManualNextAdvances()
    {
        var queue = new List<string> { "a", "b", "a" };
        Assert.Equal(1, QueueAdvance.NextAfterTrackEnds(queue, 1, true, true, true));
        Assert.Equal(new[] { "a", "b", "a" }, queue);
        Assert.Equal(2, QueueAdvance.Next(queue, 1, false, true));
        Assert.Equal(2, QueueAdvance.NextAfterTrackEnds(queue, 2, false, false, true));
        Assert.Equal(-1, QueueAdvance.Next(queue, 2, false, false));
    }
}
