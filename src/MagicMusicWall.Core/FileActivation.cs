namespace MagicMusicWall.Core;

public static class FileActivation
{
    public static string[] ParsePaths(string[] arguments, string baseDirectory)
    {
        var paths = new List<string>();
        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];
            if (argument is "--data-dir" or "--import" or "--video-test") { i++; continue; }
            if (argument.StartsWith("--", StringComparison.Ordinal)) continue;
            if (string.IsNullOrWhiteSpace(argument)) continue;
            paths.Add(Path.GetFullPath(argument, baseDirectory));
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
