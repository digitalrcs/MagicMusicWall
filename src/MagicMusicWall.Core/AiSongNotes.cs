using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MagicMusicWall.Core;

public sealed record AiSongNote(string SongId, string Title, string Artist, string Provider, string Model,
    DateTimeOffset CreatedUtc, string Text);

/// <summary>AI interpretations are kept apart from verified catalog metadata and original media.</summary>
public static class AiSongNotes
{
    private static string FilePath(string directory, string id) => Path.Combine(directory, "ai-song-notes",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json");
    public static AiSongNote? Load(string directory, string id)
    {
        var path = FilePath(directory, id);
        if (!File.Exists(path)) return null;
        var note = JsonSerializer.Deserialize<AiSongNote>(File.ReadAllText(path));
        return note?.SongId == id ? note : null;
    }
    public static void Save(string directory, AiSongNote note)
    {
        var path = FilePath(directory, note.SongId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(note, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
