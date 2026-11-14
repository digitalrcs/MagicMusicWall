using MagicMusicWall.Core;

namespace MagicMusicWall.Streaming;

/// <summary>
/// Optional song-information callbacks for the remote page. GetSongAsync is a local
/// read; the remaining methods perform only the lookup explicitly requested by the user.
/// Song IDs refer to library entries, never file paths or provider URLs.
/// </summary>
public interface IRemoteSongInformation
{
    Task<RemoteSongDetails?> GetSongAsync(string id, CancellationToken cancellationToken);
    Task<MusicSearchResult> SearchRecordingsAsync(string id, string artist, string title, CancellationToken cancellationToken);
    Task<LyricsResult> GetLyricsAsync(string id, string artist, string title, CancellationToken cancellationToken);
    Task<RemoteFactSearch> SearchFactsAsync(string id, string artist, string title, CancellationToken cancellationToken);
    Task<SongFactsArticle> GetFactAsync(string id, string pageId, CancellationToken cancellationToken);
}

public sealed record RemoteSong(string Id, string Title, string Artist, string Album, double DurationSeconds);
public sealed record RemoteSongDetails(RemoteSong Song, LyricsResult? Lyrics);
public sealed record RemoteFactSearch(IReadOnlyList<FactArticle> Items, string? Message = null);
