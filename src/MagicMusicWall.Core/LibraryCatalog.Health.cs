namespace MagicMusicWall.Core;

public sealed record LibraryHealthReport(IReadOnlyList<MediaItem> Unavailable, IReadOnlyList<MediaItem> MissingMetadata,
    IReadOnlyList<IReadOnlyList<MediaItem>> PossibleDuplicates);

public sealed partial class LibraryCatalog
{
    /// <summary>Read-only suggestions. Matching metadata is not proof that two recordings are identical.</summary>
    public LibraryHealthReport InspectHealth()
    {
        lock (_gate)
        {
            var items = Document.Items.ToArray();
            var duplicates = items.Where(i => !i.IsVideo && !string.IsNullOrWhiteSpace(i.Title) && !string.IsNullOrWhiteSpace(i.Artist)
                && double.IsFinite(i.DurationSeconds) && i.DurationSeconds > 0)
                .GroupBy(i => (Title: Fold(i.Title.Trim()), Artist: Fold(i.Artist.Trim()), Seconds: Math.Round(i.DurationSeconds)))
                .Where(g => g.Count() > 1).Select(g => (IReadOnlyList<MediaItem>)g.ToArray()).ToArray();
            return new(items.Where(i => !File.Exists(i.Path)).ToArray(),
                items.Where(i => string.IsNullOrWhiteSpace(i.Title) || string.IsNullOrWhiteSpace(i.Artist) || string.IsNullOrWhiteSpace(i.Album)).ToArray(), duplicates);
        }
    }

    /// <summary>Relink one unavailable entry after explicit user review, preserving its identity and all saved metadata.</summary>
    public void RelinkItem(string itemId, string replacementPath)
    {
        var path = NormalizePath(replacementPath);
        if (!IsSupported(path)) throw new ArgumentException("Choose a supported media file.", nameof(replacementPath));
        if (!File.Exists(path)) throw new FileNotFoundException("The replacement file is unavailable.", path);
        lock (_gate)
        {
            var item = FindItem(itemId);
            if (File.Exists(item.Path)) throw new InvalidOperationException("Only unavailable library entries can be relinked.");
            if (item.IsVideo != IsVideo(path)) throw new ArgumentException("Choose the same media kind (music or video).", nameof(replacementPath));
            if (Document.Items.Any(i => i.Id != itemId && string.Equals(NormalizePath(i.Path), path, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("That file is already in the library. Choose a different replacement.");
            var previous = (item.Path, item.IsAvailable);
            try { item.Path = path; item.IsAvailable = true; SaveLocked(); }
            catch { (item.Path, item.IsAvailable) = previous; throw; }
        }
    }
}

