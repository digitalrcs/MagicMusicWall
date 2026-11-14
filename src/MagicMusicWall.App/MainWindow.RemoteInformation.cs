using System.Windows.Threading;
using MagicMusicWall.Core;
using MagicMusicWall.Streaming;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private sealed partial class RemoteBridge
    {
        private Task<MediaItem?> SongSnapshot(string id, CancellationToken ct) => main.Dispatcher.InvokeAsync(() =>
        {
            if (main._closing || main._scanning) throw new InvalidOperationException("Library is busy. Try again shortly.");
            var item = main._catalog.Document.Items.FirstOrDefault(x => x.Id == id);
            return item is null ? null : new MediaItem { Id = item.Id, Path = item.Path, Title = item.Title, Artist = item.Artist, Album = item.Album, DurationSeconds = item.DurationSeconds, IsVideo = item.IsVideo };
        }, DispatcherPriority.Background, ct).Task;

        public async Task<RemoteSongDetails?> GetSongAsync(string id, CancellationToken ct)
        {
            var item = await SongSnapshot(id, ct);
            if (item is null) return null;
            var local = await Task.Run(() => LocalLyrics.Read(item), ct);
            return new(new(item.Id, item.Title, item.Artist, item.Album, item.DurationSeconds), local);
        }
        private async Task<MediaItem> RequireSong(string id, CancellationToken ct) => await SongSnapshot(id, ct) ?? throw new ArgumentException("Song no longer exists in the library.");

        public async Task<MusicSearchResult> SearchRecordingsAsync(string id, string artist, string title, CancellationToken ct)
        {
            await RequireSong(id, ct);
            return await main._musicLookup.SearchRecordingsAsync(artist, title, ct);
        }
        public async Task<LyricsResult> GetLyricsAsync(string id, string artist, string title, CancellationToken ct)
        {
            var item = await RequireSong(id, ct);
            return await main._musicLookup.GetLyricsAsync(artist, title, item.Album, item.DurationSeconds, ct);
        }
        public async Task<RemoteFactSearch> SearchFactsAsync(string id, string artist, string title, CancellationToken ct)
        {
            await RequireSong(id, ct);
            return new(await main._songFacts.SearchAsync(artist, title, ct));
        }
        public async Task<SongFactsArticle> GetFactAsync(string id, string pageId, CancellationToken ct)
        {
            await RequireSong(id, ct);
            return await main._songFacts.GetArticleAsync(pageId, ct);
        }
    }
}
