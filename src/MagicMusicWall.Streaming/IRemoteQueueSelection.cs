namespace MagicMusicWall.Streaming;

public interface IRemoteQueueSelection
{
    // Resolve opaque library IDs without changing the desktop queue.
    Task<IReadOnlyList<RemoteItem>> ResolveQueueItemsAsync(RemoteCommand selection, CancellationToken cancellationToken);
}
