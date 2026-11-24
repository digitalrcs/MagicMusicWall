namespace MagicMusicWall.Core;

public static class QueueEditing
{
    public static int MovedCurrentIndex(int current, int from, int to)
    {
        if (current == from) return to;
        if (from < current && to >= current) return current - 1;
        if (from > current && to <= current) return current + 1;
        return current;
    }
}
