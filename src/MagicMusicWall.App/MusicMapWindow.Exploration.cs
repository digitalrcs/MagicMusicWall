using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

internal sealed partial class MusicMapWindow
{
    private readonly Func<string?> _nowPlaying;
    private readonly CheckBox _followPlaying = new() { Content = "Follow now playing", Margin = new(10,0,10,0), VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _followTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string? _lastFollowed;
    private Exploration? _savedExploration;
    private static string ExplorationPath => Path.Combine(App.DataDirectory, "constellation-view.json");
    public bool HasSavedExploration => _savedExploration is not null;
    private sealed record Exploration(MusicMapKind Kind, MusicMapSimilarity Similarity, string Search,
        string? Genre, string? SelectedId, string? ScopeId, string[]? Scope, string ScopeLabel,
        double Zoom, double Horizontal, double Vertical, bool Follow, string[] History);

    private void InitializeExploration()
    {
        try
        {
            if (File.Exists(ExplorationPath)) _savedExploration = JsonSerializer.Deserialize<Exploration>(File.ReadAllText(ExplorationPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { /* Optional view state never blocks the library. */ }
        _followPlaying.Checked += (_, _) => { _lastFollowed = null; FollowCurrent(); };
        _followTimer.Tick += (_, _) => FollowCurrent();
    }

    private void FollowCurrent()
    {
        if (_closed || _busy || _index is null || _followPlaying.IsChecked != true) return;
        var id = _nowPlaying();
        if (id is null || id == _lastFollowed) return;
        _lastFollowed = id;
        FocusTracks([id]);
    }

    private void RestoreExploration()
    {
        if (_savedExploration is not { } saved || _index is null) return;
        _updating = true;
        _kind.SelectedItem = Enum.IsDefined(saved.Kind) ? saved.Kind : MusicMapKind.Artist;
        _similarity.SelectedItem = Enum.IsDefined(saved.Similarity) ? saved.Similarity : MusicMapSimilarity.Blended;
        _search.Text = saved.Search ?? "";
        _genre.SelectedItem = _genre.Items.Cast<string>().Contains(saved.Genre) ? saved.Genre : "All genres";
        _scopeId = saved.ScopeId;
        var existingIds = _songs.Keys.ToHashSet(StringComparer.Ordinal);
        _scope = saved.Scope?.Where(existingIds.Contains).ToHashSet(StringComparer.Ordinal);
        _scopeLabel.Text = _scope is null ? "All indexed music" : saved.ScopeLabel;
        _updating = false;
        Filter();
        var selected = _filtered.FirstOrDefault(item => item.Id == saved.SelectedId);
        if (selected is not null) { _selected = selected; _page = Math.Max(0, _filtered.IndexOf(selected)) / PageSize; DrawMap(); }
        _history.Clear();
        foreach (var id in (saved.History ?? []).Reverse()) if (_index.Entities.Any(item => item.Id == id)) _history.Push(id);
        _back.IsEnabled = _history.Count > 0;
        _scale.ScaleX = _scale.ScaleY = double.IsFinite(saved.Zoom) ? Math.Clamp(saved.Zoom, .2, 1.6) : .72;
        _mapScroll.ScrollToHorizontalOffset(double.IsFinite(saved.Horizontal) ? Math.Max(0,saved.Horizontal) : 0);
        _mapScroll.ScrollToVerticalOffset(double.IsFinite(saved.Vertical) ? Math.Max(0,saved.Vertical) : 0);
        _followPlaying.IsChecked = saved.Follow;
    }

    private void SaveExploration()
    {
        if (_index is null) return;
        var temporary = ExplorationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(App.DataDirectory);
            var state = new Exploration(_kind.SelectedItem is MusicMapKind kind ? kind : MusicMapKind.Artist,
                Similarity, _search.Text, _genre.SelectedItem as string, _selected?.Id, _scopeId, _scope?.ToArray(),
                _scopeLabel.Text, _scale.ScaleX, _mapScroll.HorizontalOffset, _mapScroll.VerticalOffset,
                _followPlaying.IsChecked == true, _history.Take(100).ToArray());
            File.WriteAllText(temporary, JsonSerializer.Serialize(state));
            File.Move(temporary, ExplorationPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* A failed view-state save cannot affect music or queue data. */ }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
