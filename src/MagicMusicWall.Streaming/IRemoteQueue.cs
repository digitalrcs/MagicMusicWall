namespace MagicMusicWall.Streaming;

/// <summary>Pages queue occurrences by absolute index, including duplicate media IDs.</summary>
public interface IRemoteQueue
{
    Task<RemoteQueuePage> GetQueueAsync(int offset, int limit, CancellationToken cancellationToken);
}

public sealed record RemoteQueuePage(IReadOnlyList<RemoteItem> Items, int Offset, int Total);
