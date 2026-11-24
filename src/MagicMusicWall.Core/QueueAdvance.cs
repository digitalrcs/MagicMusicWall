namespace MagicMusicWall.Core;

public static class QueueAdvance
{
    public static int NextAfterTrackEnds<T>(IList<T> queue, int current, bool shuffle, bool repeatQueue, bool repeatOne, Random? random = null)
        => repeatOne && current >= 0 && current < queue.Count ? current : Next(queue, current, shuffle, repeatQueue, random);

    // Move the randomly chosen unplayed entry into the next slot. The prefix
    // remains playback history, so Previous and the visible queue stay coherent.
    public static int Next<T>(IList<T> queue, int current, bool shuffle, bool repeat, Random? random = null)
    {
        if (queue.Count == 0) return -1;
        int next = Math.Max(0, current + 1);
        bool wrapped = next >= queue.Count;
        if (wrapped && !repeat) return -1;
        if (wrapped) next = 0;
        if (shuffle)
        {
            random ??= Random.Shared;
            // On wrap, avoid immediately replaying the last slot if possible.
            int limit = wrapped && queue.Count > 1 ? queue.Count - 1 : queue.Count;
            int chosen = random.Next(next, limit);
            if (chosen != next) (queue[next], queue[chosen]) = (queue[chosen], queue[next]);
        }
        return next;
    }
}
