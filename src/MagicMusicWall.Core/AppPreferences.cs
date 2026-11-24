using System.Text.Json;
using System.Text.Json.Serialization;

namespace MagicMusicWall.Core;

/// <summary>Preferences shared by the desktop setup screen and application startup.</summary>
public sealed class AppPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public string AiProvider { get; set; } = "LM Studio";
    public string AiBaseUrl { get; set; } = "http://localhost:1234/v1";
    public string AiModel { get; set; } = "";
    public string AiKeyProtected { get; set; } = "";
    public string AiKeyEndpoint { get; set; } = "";
    public string AiKeyProvider { get; set; } = "";
    public List<string> AiSavedPrompts { get; set; } = [];
    public bool AiExcludeQueued { get; set; } = true;

    public bool LibraryToolsExpanded { get; set; } = true;
    public int VisualizationMode { get; set; }
    public double VisualizationPower { get; set; } = 1.6;

    public int Volume { get; set; } = 65;
    public string AudioDeviceId { get; set; } = "";
    public string AudioDeviceName { get; set; } = "";
    public bool Shuffle { get; set; }
    public bool RepeatQueue { get; set; }
    public bool RepeatOne { get; set; }
    public bool RestoreSession { get; set; } = true;
    public bool ResumePlayback { get; set; }
    public bool CompactLayout { get; set; }
    public double WindowWidth { get; set; } = 1420;
    public double WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; }
    public double LibraryPanelWidth { get; set; } = 220;
    public double QueuePanelWidth { get; set; } = 310;
    public bool LibraryPanelVisible { get; set; } = true;
    public bool QueuePanelVisible { get; set; } = true;
    public bool WatchFolders { get; set; }
    public bool LevelPlaybackVolume { get; set; }
    public string GuestAccessToken { get; set; } = "";
    public bool QueueExternalFiles { get; set; } = true;
    public bool RescanOnStartup { get; set; }
    public bool CollectionGridView { get; set; }
    public string SharingAddress { get; set; } = "";
    public int SharingPort { get; set; }
    public bool RemoteEnabled { get; set; }
    public bool BrowserPlaybackEnabled { get; set; } = true;
    public string RemoteAddress { get; set; } = "";
    public int RemotePort { get; set; } = 8787;
    public string RemoteAccessToken { get; set; } = "";
    public string RemotePassphraseHash { get; set; } = "";
    // Windows DPAPI ciphertext. Never store a plaintext provider token here.
    public string TmdbTokenProtected { get; set; } = "";

    [JsonIgnore]
    public string? LoadWarning { get; private set; }

    public static AppPreferences Load(string directory)
    {
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var settings = Deserialize(File.ReadAllText(path));
            settings.Normalize();
            return settings;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (JsonException)
        {
            return new() { LoadWarning = "Settings could not be read. Defaults are in use; the original settings will be preserved before saving." };
        }
        catch (IOException)
        {
            return new() { LoadWarning = "Settings are unavailable. Defaults are in use; check access to the app data folder before saving." };
        }
        catch (UnauthorizedAccessException)
        {
            return new() { LoadWarning = "Settings could not be accessed. Defaults are in use; check access to the app data folder before saving." };
        }
    }

    public AppPreferences Copy()
    {
        var copy = (AppPreferences)MemberwiseClone();
        copy.AiSavedPrompts = [.. AiSavedPrompts];
        return copy;
    }

    public void Save(string directory)
    {
        Normalize();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");

        // Do not replace an unreadable file or discard malformed user data.
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path);
            try { Deserialize(existing); }
            catch (JsonException)
            {
                var backup = Path.Combine(directory, $"settings.corrupt.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.json");
                File.Copy(path, backup, overwrite: false);
            }
        }

        var temporary = Path.Combine(directory, $"settings.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, this, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
            else File.Move(temporary, path);
            LoadWarning = null;
        }
        finally
        {
            // A failed replacement must leave the last saved preferences intact.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static AppPreferences Deserialize(string json) =>
        JsonSerializer.Deserialize<AppPreferences>(json, JsonOptions) ?? throw new JsonException("Expected a settings object.");

    private void Normalize()
    {
        VisualizationPower = double.IsFinite(VisualizationPower) ? Math.Clamp(VisualizationPower, 0.5, 3) : 1.6;
        Volume = Math.Clamp(Volume, 0, 100);
        WindowWidth = double.IsFinite(WindowWidth) ? Math.Clamp(WindowWidth, 1120, 7680) : 1420;
        WindowHeight = double.IsFinite(WindowHeight) ? Math.Clamp(WindowHeight, 720, 4320) : 900;
        LibraryPanelWidth = double.IsFinite(LibraryPanelWidth) ? Math.Clamp(LibraryPanelWidth, 180, 420) : 220;
        QueuePanelWidth = double.IsFinite(QueuePanelWidth) ? Math.Clamp(QueuePanelWidth, 240, 600) : 310;
        GuestAccessToken ??= "";
        if (SharingPort is < 0 or > 65535) SharingPort = 0;
        if (RemotePort is < 1 or > 65535) RemotePort = 8787;
        AudioDeviceId ??= "";
        AudioDeviceName ??= "";
        SharingAddress ??= "";
        RemoteAddress ??= "";
        RemoteAccessToken ??= "";
        RemotePassphraseHash ??= "";
        TmdbTokenProtected ??= "";
        AiProvider ??= "LM Studio";
        AiBaseUrl ??= "http://localhost:1234/v1";
        AiModel ??= "";
        AiKeyProtected ??= "";
        AiKeyEndpoint ??= "";
        AiKeyProvider ??= "";
        AiSavedPrompts = (AiSavedPrompts ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Length > 2000 ? p[..2000] : p).Distinct().Take(30).ToList();
    }
}
