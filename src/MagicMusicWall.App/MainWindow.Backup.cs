using System.Windows;
using MagicMusicWall.Core;
using Microsoft.Win32;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    internal async Task BackupLibrary(Window owner)
    {
        if (!CanEdit() || _queueChanging || _closing) return;
        var picker = new SaveFileDialog { Title = "Back up library, settings and queue", Filter = "Private backup ZIP|*.zip", FileName = $"MagicMusicWall-backup-{DateTime.Now:yyyy-MM-dd}.zip" };
        if (picker.ShowDialog(owner) != true) return;
        await SaveListeningSessionAsync();
        if (!CanEdit() || _queueChanging || _closing) throw new InvalidOperationException("A library operation started. Wait for it to finish, then create the backup again.");
        _catalog.Save(); _settings.Save(App.DataDirectory);
        LibraryBackup.Create(App.DataDirectory, picker.FileName);
        MessageBox.Show(owner, "Backup saved. Keep it private: it includes remote access settings. Media files, analysis caches and AI notes are not included.", "Backup complete");
    }
    internal bool PrepareLibraryRestore(Window owner)
    {
        if (!CanEdit() || _queueChanging || _closing) return false;
        var picker = new OpenFileDialog { Title = "Restore a private Magic Music Wall backup", Filter = "Private backup ZIP|*.zip" };
        if (picker.ShowDialog(owner) != true) return false;
        var names = LibraryBackup.Inspect(picker.FileName);
        if (MessageBox.Show(owner, $"Restore {string.Join(", ", names)}?\n\nMagic Music Wall will close, save a recovery backup and restart. Your music files are unchanged. Settings protected for another Windows account may need to be entered again.", "Restore and restart", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
        if (!CanEdit() || _queueChanging || _closing) throw new InvalidOperationException("A library or player operation started. Wait for it to finish before restoring.");
        App.RestoreBackupOnExit = picker.FileName;
        return true;
    }
}
