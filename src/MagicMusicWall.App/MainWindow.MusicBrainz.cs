using System.Windows;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private MusicBrainzWindow? _musicBrainzWindow;

    private void ShowMusicBrainz(IReadOnlyList<string>? ids, Window owner)
    {
        if (_closing || !CanEdit()) return;
        if (_musicBrainzWindow is not null) { _musicBrainzWindow.Activate(); return; }
        var wanted = ids?.ToHashSet(StringComparer.Ordinal);
        var tracks = _catalog.Document.Items.Where(x => !x.IsVideo && (wanted is null || wanted.Contains(x.Id)))
            .Select(x => new MediaItem { Id = x.Id, Path = x.Path, Title = x.Title, Artist = x.Artist, Album = x.Album,
                DurationSeconds = x.DurationSeconds, MusicBrainz = x.MusicBrainz }).ToArray();
        _musicBrainzWindow = new MusicBrainzWindow(tracks, _musicLookup, (expected, info) =>
        {
            if (_closing || _scanning || _metadataWriting) throw new InvalidOperationException("The library is busy. Try again after it finishes.");
            var current = _catalog.Document.Items.FirstOrDefault(x => x.Id == expected.Id);
            if (current is null || current.Path != expected.Path || current.Artist != expected.Artist || current.Title != expected.Title || current.Album != expected.Album)
                throw new InvalidOperationException("This song changed during lookup. Reopen MusicBrainz info to use its current details.");
            _catalog.SetMusicBrainzInfo(expected.Id, info);
            _musicMap?.NotifyLibraryChanged();
        }) { Owner = owner };
        try { _musicBrainzWindow.ShowDialog(); }
        finally { _musicBrainzWindow = null; RefreshMusicMapIfChanged(); }
    }
}
