using System.Text.Json.Serialization;

namespace MagicMusicWall.Core;

/// <summary>Saved MusicBrainz evidence, separate from the original media's file tags.</summary>
public sealed record MusicBrainzInfo
{
    public string RecordingId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string ReleaseId { get; init; } = "";
    public string FirstReleaseDate { get; init; } = "";
    public string[] Genres { get; init; } = [];
    public string[] Tags { get; init; } = [];
    public string[] Instruments { get; init; } = [];
    public string[] Credits { get; init; } = [];
    public string[] Sources { get; init; } = [];
    public DateTimeOffset RetrievedUtc { get; init; }
    [JsonIgnore]
    public string SourceUrl => Guid.TryParse(RecordingId, out var id) ? $"https://musicbrainz.org/recording/{id:D}" : "";
}

public sealed record MusicBrainzInfoResult
{
    public MusicBrainzInfo? Info { get; init; }
    public string? Message { get; init; }
}
