using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class SetupWindow : Window
{
    private readonly MainWindow _main;
    private AppPreferences _draft;
    private bool _busy;

    internal SetupWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        _draft = main.Preferences;
        LoadAudio(_draft.AudioDeviceId);
        Volume.Value = _draft.Volume;
        Shuffle.IsChecked = _draft.Shuffle;
        RepeatQueue.IsChecked = _draft.RepeatQueue;
        QueueExternalFiles.IsChecked = _draft.QueueExternalFiles;
        RescanStartup.IsChecked = _draft.RescanOnStartup;
        RemoteEnabled.IsChecked = _draft.RemoteEnabled;
        BrowserPlaybackEnabled.IsChecked = _draft.BrowserPlaybackEnabled;
        FillAddresses(RemoteAddress, _draft.RemoteAddress);
        FillAddresses(SharingAddress, _draft.SharingAddress);
        RemotePort.Text = _draft.RemotePort.ToString();
        SharingPort.Text = _draft.SharingPort.ToString();
        DataLocation.Text = App.DataDirectory;
        AppVersionLabel.Text = $"DigitalRCS.com · Magic Music Wall {typeof(App).Assembly.GetName().Version?.ToString(3)}";
        InitializeAi();
        RefreshMetadataSettings();
        RefreshFolders(); RefreshRemote();
        Closing += (_, e) => { if (_busy) { e.Cancel = true; SaveStatus.Text = "Please wait for the current operation to finish."; } };
    }

    private static void FillAddresses(ComboBox box, string saved)
    {
        box.ItemsSource = NetworkAddresses.Choices(saved);
        box.SelectedItem = box.Items.Cast<NetworkChoice>().First(c => c.Address == saved);
    }

    private void LoadAudio(string id, bool refresh = false)
    {
        AudioOutput.ItemsSource = _main.SetupAudioChoices(refresh);
        AudioOutput.SelectedItem = AudioOutput.Items.Cast<AudioChoice>().FirstOrDefault(c => c.Id == id) ?? AudioOutput.Items[0];
    }

    private void RefreshFolders() => Folders.ItemsSource = _main.LibraryFolders;
    private void RefreshRemote()
    {
        RemoteStatus.Text = _main.RemoteStatus; RemoteLink.Text = _main.RemoteLink;
        RemoteAddressLink.Text = _main.RemoteAddressLink;
        RemotePassphraseStatus.Text = RemotePassphrase.IsConfigured(_draft.RemotePassphraseHash)
            ? "Passphrase saved. Leave both fields blank to keep it."
            : "No passphrase saved yet. QR access links still work.";
    }
    private void RefreshAudio_Click(object sender, RoutedEventArgs e) => LoadAudio((AudioOutput.SelectedItem as AudioChoice)?.Id ?? "", true);
    private void Bluetooth_Click(object sender, RoutedEventArgs e) => _main.OpenExternal("ms-settings:bluetooth");
    private void OpenData_Click(object sender, RoutedEventArgs e) { Directory.CreateDirectory(App.DataDirectory); _main.OpenExternal(App.DataDirectory); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void TmdbSettings_Click(object sender, RoutedEventArgs e) => _main.OpenExternal("https://www.themoviedb.org/settings/api");
    private void RefreshMetadataSettings() => TmdbTokenStatus.Text = string.IsNullOrEmpty(_draft.TmdbTokenProtected)
        ? "No token saved. Music lookup works without one."
        : "Movie token saved. Leave this field blank to keep it.";

    private async Task RunOperation(Func<Task> operation)
    {
        if (_busy) return;
        _busy = true;
        SettingsTabs.IsEnabled = false;
        SaveButton.IsEnabled = AddFolder.IsEnabled = ManageFolders.IsEnabled = Rescan.IsEnabled = false;
        try { await operation(); }
        catch (Exception ex) { SaveStatus.Text = ex.Message; }
        finally { _busy = false; SettingsTabs.IsEnabled = true; SaveButton.IsEnabled = AddFolder.IsEnabled = ManageFolders.IsEnabled = Rescan.IsEnabled = true; }
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e) => await RunOperation(async () => { SaveStatus.Text = "Adding library folders…"; await _main.AddSetupFolder(this); RefreshFolders(); SaveStatus.Text = "Library folder selection updated."; });
    private void ManageFolders_Click(object sender, RoutedEventArgs e) { _main.ManageSetupFolders(this); RefreshFolders(); }
    private async void Rescan_Click(object sender, RoutedEventArgs e) => await RunOperation(async () => { SaveStatus.Text = "Scanning library folders…"; await _main.RescanLibrary(); RefreshFolders(); SaveStatus.Text = "Rescan finished. See the player's status for details."; });

    private async void Save_Click(object sender, RoutedEventArgs e) => await RunOperation(SaveSettings);
    private async Task SaveSettings()
    {
        if (!int.TryParse(RemotePort.Text, out var remotePort) || remotePort is < 1 or > 65535)
            throw new InvalidOperationException("The web remote port must be between 1 and 65535.");
        if (!int.TryParse(SharingPort.Text, out var sharePort) || sharePort is < 0 or > 65535)
            throw new InvalidOperationException("The file sharing port must be between 0 and 65535.");
        var audio = (AudioChoice)AudioOutput.SelectedItem;
        string? newPassphraseHash = null;
        if (RemoveRemotePassphrase.IsChecked == true) newPassphraseHash = "";
        else if (RemotePassphraseInput.Password.Length > 0 || RemotePassphraseConfirm.Password.Length > 0)
        {
            if (RemotePassphraseInput.Password != RemotePassphraseConfirm.Password)
                throw new InvalidOperationException("The remote passphrases do not match.");
            // Capture current playback preferences after this asynchronous work.
            var passphrase = RemotePassphraseInput.Password;
            newPassphraseHash = await Task.Run(() => RemotePassphrase.CreateHash(passphrase));
        }
        // A remote can change playback while this dialog is open. Only apply playback
        // fields the user changed here; preserve live values for untouched controls.
        var updated = _main.Preferences;
        if (audio.Id != _draft.AudioDeviceId)
        {
            updated.AudioDeviceId = audio.Id;
            updated.AudioDeviceName = audio.Name.Replace(" · disconnected", "", StringComparison.Ordinal);
        }
        if ((int)Volume.Value != _draft.Volume) updated.Volume = (int)Volume.Value;
        if ((Shuffle.IsChecked == true) != _draft.Shuffle) updated.Shuffle = Shuffle.IsChecked == true;
        if ((RepeatQueue.IsChecked == true) != _draft.RepeatQueue) updated.RepeatQueue = RepeatQueue.IsChecked == true;
        updated.RemoteAccessToken = _draft.RemoteAccessToken;
        if (newPassphraseHash is not null) updated.RemotePassphraseHash = newPassphraseHash;
        updated.QueueExternalFiles = QueueExternalFiles.IsChecked == true;
        updated.RescanOnStartup = RescanStartup.IsChecked == true;
        updated.RemoteEnabled = RemoteEnabled.IsChecked == true;
        updated.BrowserPlaybackEnabled = BrowserPlaybackEnabled.IsChecked == true;
        updated.RemoteAddress = ((NetworkChoice)RemoteAddress.SelectedItem).Address;
        updated.RemotePort = remotePort;
        updated.SharingAddress = ((NetworkChoice)SharingAddress.SelectedItem).Address;
        updated.SharingPort = sharePort;
        if (RemoveTmdbToken.IsChecked == true) updated.TmdbTokenProtected = "";
        else if (TmdbToken.Password.Length > 0) updated.TmdbTokenProtected = ProviderSecrets.Protect(TmdbToken.Password);
        SaveStatus.Text = "Saving settings…";
        SaveAi(updated);
        SaveStatus.Text = await _main.SavePreferences(updated);
        _draft = _main.Preferences;
        AiKey.Clear(); RemoveAiKey.IsChecked = false;
        AiStatus.Text = string.IsNullOrEmpty(_draft.AiKeyProtected) ? "No AI key saved." : "AI key saved for this provider and endpoint.";
        RemotePassphraseInput.Clear(); RemotePassphraseConfirm.Clear(); RemoveRemotePassphrase.IsChecked = false;
        TmdbToken.Clear(); RemoveTmdbToken.IsChecked = false; RefreshMetadataSettings();
        Volume.Value = _draft.Volume;
        Shuffle.IsChecked = _draft.Shuffle;
        RepeatQueue.IsChecked = _draft.RepeatQueue;
        LoadAudio(_draft.AudioDeviceId);
        RefreshRemote();
    }

    private void CopyRemote_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_main.RemoteLink)) { SaveStatus.Text = "Enable the remote and save settings first."; return; }
        try { Clipboard.SetText(_main.RemoteLink); SaveStatus.Text = "Remote link copied. Open it on a device on your home network."; }
        catch (Exception ex) { SaveStatus.Text = $"Could not copy the link: {ex.Message}"; }
    }

    private void CopyAddress_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_main.RemoteAddressLink)) { SaveStatus.Text = "Enable the remote and save settings first."; return; }
        try { Clipboard.SetText(_main.RemoteAddressLink); SaveStatus.Text = "Web address copied. Sign in there with your saved passphrase."; }
        catch (Exception ex) { SaveStatus.Text = $"Could not copy the address: {ex.Message}"; }
    }

    private void OpenAddress_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_main.RemoteAddressLink)) { SaveStatus.Text = "Enable the remote and save settings first."; return; }
        _main.OpenExternal(_main.RemoteAddressLink);
    }

    private void OpenRemote_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_main.RemoteLink)) { SaveStatus.Text = "Enable the remote and save settings first."; return; }
        _main.OpenExternal(_main.RemoteLink);
    }

    private void ShowQr_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_main.RemoteLink)) { SaveStatus.Text = "Enable the remote and save settings first."; return; }
        new QrCodeWindow(_main.RemoteLink) { Owner = this }.ShowDialog();
    }

    private void RotateLink_Click(object sender, RoutedEventArgs e)
    {
        _draft.RemoteAccessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        SaveStatus.Text = "Save settings to replace the access link and sign out remembered browsers. Reconnect with your passphrase or the new link.";
    }

    private void CopyListening_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_main.ListeningLink)) { SaveStatus.Text = "Enable the remote and browser playback, then save settings first."; return; }
        try { Clipboard.SetText(_main.ListeningLink); SaveStatus.Text = "Listening link copied. Choose a song or video on the page to start."; }
        catch (Exception ex) { SaveStatus.Text = $"Could not copy the link: {ex.Message}"; }
    }

    private void ShowListeningQr_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_main.ListeningLink)) { SaveStatus.Text = "Enable the remote and browser playback, then save settings first."; return; }
        new QrCodeWindow(_main.ListeningLink, listening: true) { Owner = this }.ShowDialog();
    }
}
