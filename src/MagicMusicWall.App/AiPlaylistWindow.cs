using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

internal sealed class AiPlaylistWindow : Window
{
    private readonly TextBox _question = new() { Text = "Suggest relaxing songs for dinner", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80 };
    private readonly ComboBox _scope = new() { DisplayMemberPath = "Name", MinWidth = 200 };
    private readonly TextBox _limit = new() { Text = "20", Width = 65 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 10) };
    private readonly ListBox _results = new() { SelectionMode = SelectionMode.Extended };
    private readonly WrapPanel _actions = new();
    private readonly Func<AppPreferences> _settings;
    private readonly Func<string?, Task<AiSong[]>> _inventory;
    private readonly Func<IReadOnlyList<string>, bool, Task> _apply;
    private CancellationTokenSource? _cancel;
    private bool _closed;
    private sealed record Scope(string Name, string? Id);
    private sealed record Row(string Id, string Display);

    public AiPlaylistWindow(Func<AppPreferences> settings, IEnumerable<MusicPlaylist> playlists,
        string? selectedPlaylist, Func<string?, Task<AiSong[]>> inventory, Func<IReadOnlyList<string>, bool, Task> apply, Action<string, Window> notes)
    {
        _settings = settings; _inventory = inventory; _apply = apply;
        Title = "AI playlist assistant"; Width = 860; Height = 720; MinWidth = 620; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new(24) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Ask your music library", FontSize = 26 });
        top.Children.Add(new TextBlock { Text = "Try: songs from the 80's · gentle guitar and relaxing rhythms · dinner music", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 10) });
        top.Children.Add(_question);
        var options = new WrapPanel { Margin = new(0, 10, 0, 10) }; top.Children.Add(options);
        _scope.ItemsSource = new[] { new Scope("Entire music library", null), new Scope("Current queue", "$queue") }
            .Concat(playlists.Select(x => new Scope(x.Name, x.Id))).ToArray();
        _scope.SelectedItem = _scope.Items.Cast<Scope>().FirstOrDefault(x => x.Id == selectedPlaylist) ?? _scope.Items[0];
        options.Children.Add(_scope); options.Children.Add(new TextBlock { Text = "  Max songs: ", VerticalAlignment = VerticalAlignment.Center }); options.Children.Add(_limit);
        var buttons = new WrapPanel(); top.Children.Add(buttons);
        AddButton(buttons, "Recommend", () => Recommend(false));
        AddButton(buttons, "Recommend & play", () => Recommend(true));
        AddButton(buttons, "Cancel", () => { _cancel?.Cancel(); return Task.CompletedTask; });
        top.Children.Add(new TextBlock { Text = "Recommend previews only. Recommend & play replaces the queue after a successful review. Metadata and sound estimates go to the provider configured in Setup > AI; audio files are not uploaded. Large libraries require multiple requests.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        top.Children.Add(_status);
        DockPanel.SetDock(_actions, Dock.Bottom); root.Children.Add(_actions);
        AddButton(_actions, "Add selected to queue", () => Apply(false));
        AddButton(_actions, "Play selected", () => Apply(true));
        AddButton(_actions, "Select all", () => { _results.SelectAll(); return Task.CompletedTask; });
        AddButton(_actions, "Song notes / web lookup", () =>
        {
            if (_results.SelectedItems.Count != 1) throw new InvalidOperationException("Select one song to view its notes.");
            notes(((Row)_results.SelectedItem).Id, this); return Task.CompletedTask;
        });
        _actions.IsEnabled = false;
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("Display")); text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        _results.ItemTemplate = new DataTemplate { VisualTree = text };
        root.Children.Add(_results);
        Closed += (_, _) => { _closed = true; _cancel?.Cancel(); };
    }
    private void AddButton(Panel panel, string label, Func<Task> action)
    {
        var button = new Button { Content = label, Margin = new(0, 0, 8, 6) };
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (OperationCanceledException) { _status.Text = "Cancelled. Playback and queue were not changed."; }
            catch (Exception ex) { _status.Text = ex is System.Text.Json.JsonException or KeyNotFoundException
                ? "The model did not return the expected song list. Try a different model or a simpler request."
                : ex is HttpRequestException ? "Could not reach the AI provider. Check Setup > AI and your server connection." : ex.Message; }
        };
        panel.Children.Add(button);
    }
    private async Task Recommend(bool play)
    {
        if (_cancel is not null) return;
        if (!int.TryParse(_limit.Text, out var limit) || limit is < 1 or > 100) throw new InvalidOperationException("Choose 1–100 songs.");
        var question = _question.Text.Trim();
        var scope = (_scope.SelectedItem as Scope)?.Id;
        var settings = _settings();
        _status.Text = $"Preparing library for {settings.AiProvider}…";
        _results.ItemsSource = null; _actions.IsEnabled = false;
        using var cancel = new CancellationTokenSource(); _cancel = cancel;
        try
        {
            var songs = await _inventory(scope);
            cancel.Token.ThrowIfCancellationRequested();
            if (songs.Length == 0) { _status.Text = "No available music in this scope."; return; }
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            var progress = new Progress<string>(message => { if (!_closed && !cancel.IsCancellationRequested) _status.Text = message; });
            var choices = await new AiPlaylistService(http).RecommendAsync(AiCredentials.Options(settings), question, songs, limit, progress, cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            if (_closed) return;
            var lookup = songs.ToDictionary(x => x.Id);
            _results.ItemsSource = choices.Select(x => new Row(x.Id,
                $"{lookup[x.Id].Title} — {lookup[x.Id].Artist}\n{lookup[x.Id].Album} · {(lookup[x.Id].Year == 0 ? "Year unknown" : lookup[x.Id].Year)}\n{x.Reason}")).ToArray();
            _results.SelectAll(); _actions.IsEnabled = choices.Count > 0;
            _status.Text = $"Reviewed {songs.Length:N0} songs. {choices.Count} suggestions. AI judgments may be wrong; review the reasons. No matching songs is a valid result.";
            if (play && choices.Count > 0) await _apply(choices.Select(x => x.Id).ToArray(), true);
        }
        finally { _cancel = null; }
    }
    private async Task Apply(bool play)
    {
        if (_cancel is not null) return;
        var ids = _results.SelectedItems.Cast<Row>().Select(x => x.Id).ToArray();
        if (ids.Length == 0) return;
        _actions.IsEnabled = false;
        try { await _apply(ids, play); _status.Text = play ? "Play request sent to the player." : $"Added {ids.Length} songs to the queue."; }
        finally { _actions.IsEnabled = true; }
    }
}
