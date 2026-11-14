namespace MagicMusicWall.Streaming;

internal static class MediaContentTypes
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg", [".flac"] = "audio/flac", [".wav"] = "audio/wav",
        [".wave"] = "audio/wav", [".m4a"] = "audio/mp4", [".m4b"] = "audio/mp4",
        [".aac"] = "audio/aac", [".ogg"] = "audio/ogg", [".oga"] = "audio/ogg",
        [".opus"] = "audio/ogg", [".wma"] = "audio/x-ms-wma", [".aif"] = "audio/aiff",
        [".aiff"] = "audio/aiff", [".ape"] = "audio/x-ape", [".alac"] = "audio/mp4",
        [".mp4"] = "video/mp4", [".m4v"] = "video/mp4", [".mkv"] = "video/x-matroska",
        [".webm"] = "video/webm", [".avi"] = "video/x-msvideo", [".mov"] = "video/quicktime",
        [".wmv"] = "video/x-ms-wmv", [".mpg"] = "video/mpeg", [".mpeg"] = "video/mpeg",
        [".ts"] = "video/mp2t", [".mts"] = "video/mp2t", [".m2ts"] = "video/mp2t",
        [".vob"] = "video/mpeg", [".ogv"] = "video/ogg", [".3gp"] = "video/3gpp",
        [".3g2"] = "video/3gpp2", [".flv"] = "video/x-flv", [".m2v"] = "video/mpeg"
    };

    public static bool TryGet(string path, out string contentType) =>
        Types.TryGetValue(Path.GetExtension(path), out contentType!);
}
