using System.Windows;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private readonly MovieLookupService _movieLookup = new();
    private bool _metadataWriting;

    private void Metadata_Click(object sender, RoutedEventArgs e) => ShowMetadata(MediaGrid.SelectedItem as MediaItem);
    private void QueueMetadata_Click(object sender, RoutedEventArgs e) => ShowMetadata(QueueList.SelectedItem as MediaItem);

    private void ShowMetadata(MediaItem? item, Window? owner = null)
    {
        if (item is null) { Status("Select one music or video file to edit its metadata."); return; }
        if (!CanEdit() || _closing) return;
        new MediaTagsWindow(item, _musicLookup, _movieLookup,
            () => ProviderSecrets.Unprotect(_settings.TmdbTokenProtected),
            (patch, ct) => WriteMediaTags(item, patch, ct)) { Owner = owner ?? this }.ShowDialog();
    }

    private async Task<MediaTagWriteResult> WriteMediaTags(MediaItem item, MediaTagPatch patch, CancellationToken ct)
    {
        if (_closing || _scanning || _queueChanging || _metadataWriting)
            throw new InvalidOperationException("The player is busy. Try saving again shortly.");
        if (!_catalog.Document.Items.Any(i => i.Id == item.Id && i.Path == item.Path))
            throw new InvalidOperationException("This file is no longer in the library.");
        _metadataWriting = _queueChanging = true;
        try
        {
            if (_current?.Id == item.Id)
            {
                ++_playVersion;
                await Playback(() => { _player.Stop(); _player.Media = null; return true; });
                _current = null;
                NowTitle.Text = "Playback stopped for tag editing";
                NowArtist.Text = item.Title;
            }
            var result = await MediaTagService.WriteAsync(item.Path, patch, ct);
            try { _catalog.ApplyWrittenTags(item.Id, result.Tags, patch.Album is not null); }
            catch (Exception ex)
            {
                // The file commit already succeeded. Never report it as an uncommitted write.
                MessageBox.Show(this, $"The file tags were saved and the original backup is at:\n{result.BackupPath}\n\nThe library could not refresh: {ex.Message}\nRescan this file after restoring access to the library data folder.",
                    "Tags saved; library refresh needed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            RefreshView(); QueueList.Items.Refresh();
            Status($"File tags saved. Original backup: {result.BackupPath}");
            return result;
        }
        finally
        {
            _metadataWriting = _queueChanging = false;
            // An unrelated playing file may have ended while queue commands were blocked.
            if (_current is not null) Player_Ended(this, EventArgs.Empty);
        }
    }
}
