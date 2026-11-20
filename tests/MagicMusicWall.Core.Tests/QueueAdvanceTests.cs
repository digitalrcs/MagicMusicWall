using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public class QueueAdvanceTests
{
    private sealed class PickLast : Random
    {
        public override int Next(int minValue, int maxValue) => maxValue - 1;
    }

    [Fact]
    public void EnablingShuffleDuringPlaybackSelectsAnUnplayedSongAndPreservesHistory()
    {
        var queue = new List<string> { "A", "B", "C", "D", "E" };
        Assert.Equal(2, QueueAdvance.Next(queue, 1, true, false, new PickLast()));
        Assert.Equal(new[] { "A", "B", "E", "D", "C" }, queue);
        Assert.Equal(3, QueueAdvance.Next(queue, 2, false, false));
        Assert.Equal("D", queue[3]);
    }

    [Fact]
    public void ShuffledPassVisitsEveryRemainingEntryOnceThenStops()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            var queue = Enumerable.Range(0, 20).ToList();
            var random = new Random(seed);
            var played = new List<int>();
            int current = 4;
            while ((current = QueueAdvance.Next(queue, current, true, false, random)) >= 0) played.Add(queue[current]);
            Assert.Equal(Enumerable.Range(5, 15), played.Order());
            Assert.Equal(Enumerable.Range(0, 5), queue.Take(5));
        }
    }

    [Fact]
    public void RepeatStartsANewShuffledPassWithoutImmediateReplay()
    {
        var queue = new List<string> { "A", "B", "C" };
        Assert.Equal(0, QueueAdvance.Next(queue, 2, true, true, new PickLast()));
        Assert.Equal("B", queue[0]);
        Assert.Equal("C", queue[2]);
    }

    [Fact]
    public void EmptySingleAndUnstartedQueuesBehaveCorrectly()
    {
        Assert.Equal(-1, QueueAdvance.Next(new List<int>(), -1, true, true));
        Assert.Equal(-1, QueueAdvance.Next(new List<int> { 1 }, 0, true, false));
        Assert.Equal(0, QueueAdvance.Next(new List<int> { 1 }, 0, true, true));
        var queue = new List<int> { 1, 2, 3 };
        Assert.Equal(0, QueueAdvance.Next(queue, -1, true, false, new PickLast()));
        Assert.Equal(3, queue[0]);
    }

    [Fact]
    public void NewlyQueuedEntriesParticipateInTheRemainingPass()
    {
        var queue = new List<int> { 1, 2 };
        queue.Add(3);
        Assert.Equal(1, QueueAdvance.Next(queue, 0, true, false, new PickLast()));
        Assert.Equal(3, queue[1]);
        Assert.Equal(2, QueueAdvance.Next(queue, 1, true, false));
        Assert.Equal(2, queue[2]);
    }
}
