using System.IO;
using System.Windows;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private AiPlaylistWindow? _aiPlaylist;
    private void AiPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || !CanEdit()) return;
        if (_aiPlaylist is not null) { _aiPlaylist.Activate(); return; }
        _aiPlaylist = new AiPlaylistWindow(() => Preferences, _catalog.Document.Playlists,
            (PlaylistsList.SelectedItem as MusicPlaylist)?.Id, AiInventory, ApplyAiSongs, (id, owner) => ShowAiNotes(id, owner)) { Owner = this };
        _aiPlaylist.Closed += (_, _) => _aiPlaylist = null;
        _aiPlaylist.Show();
    }
    private void ShowAiNotes(string id, Window owner)
    {
        var item = _catalog.Document.Items.FirstOrDefault(x => x.Id == id && !x.IsVideo);
        if (item is null) return;
        new AiSongNotesWindow(item, () => Preferences, window => ShowMusicBrainz([id], window)) { Owner = owner }.ShowDialog();
    }
    private async Task<AiSong[]> AiInventory(string? scope)
    {
        if (_scanning || _metadataWriting) throw new InvalidOperationException("Wait for the library update to finish.");
        var items = _catalog.Document.Items.Where(x => !x.IsVideo && x.IsAvailable).ToArray();
        if (scope is not null)
        {
            var ids = scope == "$queue" ? _queue.Select(x => x.Id).ToHashSet()
                : (_catalog.Document.Playlists.FirstOrDefault(x => x.Id == scope)?.ItemIds ?? []).ToHashSet();
            items = items.Where(x => ids.Contains(x.Id)).ToArray();
        }
        // Copy the metadata before leaving the dispatcher. Never pass file paths to a provider.
        var metadata = AiPlaylistService.Inventory(items, new Dictionary<string, AudioFeatures>());
        var fileSnapshot = items.Select(x => new MediaItem { Id = x.Id, Path = x.Path }).ToArray();
        var features = await Task.Run(() => MusicMapAudioScanner.LoadFeatures(App.DataDirectory, fileSnapshot));
        return await Task.Run(() => metadata.Select(x =>
        {
            var song = features.TryGetValue(x.Id, out var sound)
                ? x with { EstimatedBpm = sound.Tempo > 0 ? Math.Round(sound.Tempo) : null, BeatConfidence = Math.Round(sound.BeatStrength, 3) } : x;
            try
            {
                var note = AiSongNotes.Load(App.DataDirectory, x.Id);
                if (note is not null && note.Title == x.Title && note.Artist == x.Artist)
                    song = song with { SavedAiNotes = "Unverified AI interpretation: " + note.Text[..Math.Min(note.Text.Length, 1200)] };
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { /* Optional notes must not prevent library use. */ }
            return song;
        }).ToArray());
    }
    private async Task ApplyAiSongs(IReadOnlyList<string> ids, bool play)
    {
        if (_closing || _queueChanging || _scanning || _metadataWriting) throw new InvalidOperationException("The player is busy. Try again shortly.");
        var lookup = _catalog.Document.Items.Where(x => !x.IsVideo && x.IsAvailable).ToDictionary(x => x.Id);
        if (ids.Any(id => !lookup.TryGetValue(id, out var song) || !File.Exists(song.Path)))
            throw new InvalidOperationException("A recommended song is no longer available. Reconnect its folder or request a new list.");
        var songs = ids.Distinct().Select(id => lookup[id]).ToList();
        if (play) await StartQueue(songs);
        else foreach (var song in songs) _queue.Add(song);
    }
}
