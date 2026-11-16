using System.Net.Http;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

internal sealed class AiPlaylistWindow : Window
{
    private readonly TextBox _question = new() { Text = "Suggest relaxing songs for dinner", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80 };
    private readonly ListBox _scope = new() { DisplayMemberPath = "Name", MaxHeight = 115, MinWidth = 0 };
    private readonly CheckBox _first = new() { Content = "Stop when enough matches are found", IsChecked = true, Margin = new(0, 8, 0, 10) };
    private readonly ObservableCollection<Row> _rows = [];
    private readonly TextBlock _selectionSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _choose = new() { Content = "Choose songs / folders…", VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    private IReadOnlyList<string> _chosen = [];
    private readonly Func<IReadOnlyList<string>, IReadOnlyList<string>?> _pick;
    private readonly TextBox _limit = new() { Text = "20", Width = 88, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 10) };
    private readonly ListBox _results = new() { SelectionMode = SelectionMode.Extended };
    private readonly WrapPanel _actions = new();
    private readonly Func<AppPreferences> _settings;
    private readonly Func<string?, IReadOnlyList<string>, Task<AiSong[]>> _inventory;
    private readonly Func<IReadOnlyList<string>, bool, Task> _apply;
    private CancellationTokenSource? _cancel;
    private bool _closed;
    private sealed record Scope(string Name, string? Id);
    private sealed record Row(string Id, string Display);

    public AiPlaylistWindow(Func<AppPreferences> settings, IEnumerable<MusicPlaylist> playlists,
        string? selectedPlaylist, Func<string?, IReadOnlyList<string>, Task<AiSong[]>> inventory, Func<IReadOnlyList<string>, bool, Task> apply, Action<string, Window> notes, IReadOnlyList<string> selectedIds, Func<IReadOnlyList<string>, IReadOnlyList<string>?> pick)
    {
        Ui.Theme(this);
        _chosen = selectedIds; _pick = pick;
        _settings = settings; _inventory = inventory; _apply = apply;
        Title = "AI playlist assistant"; Width = 940; Height = 840; MinWidth = 760; MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new(24) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Ask your music library", FontSize = 26 });
        top.Children.Add(new TextBlock { Text = "Try: songs from the 80's · gentle guitar and relaxing rhythms · dinner music", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 10) });
        var searchSection = new Border
        {
            BorderThickness = new(1.5), CornerRadius = new(8), Padding = new(16),
            Margin = new(0, 8, 0, 16), HorizontalAlignment = HorizontalAlignment.Stretch
        };
        searchSection.BorderBrush = new SolidColorBrush(Color.FromRgb(0x85, 0x8D, 0xA3));
        searchSection.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        top.Children.Add(searchSection);
        var searchContent = new StackPanel(); searchSection.Child = searchContent;
        searchContent.Children.Add(Ui.Text("What would you like to hear?", 14));
        searchContent.Children.Add(_question);
        var options = new Grid { Margin = new(0, 16, 0, 0) };
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchContent.Children.Add(options);
        var sourceColumn = new StackPanel(); options.Children.Add(sourceColumn);
        sourceColumn.Children.Add(Ui.Text("Search in", 14));
        var scopeBorder = new Border { BorderThickness = new(1.5), CornerRadius = new(4), Padding = new(4), Child = _scope };
        scopeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x85, 0x8D, 0xA3));
        scopeBorder.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
        sourceColumn.Children.Add(scopeBorder);
        var selectionColumn = new StackPanel(); Grid.SetColumn(selectionColumn, 2); options.Children.Add(selectionColumn);
        selectionColumn.Children.Add(Ui.Text("Songs and folders", 14));
        selectionColumn.Children.Add(_choose);
        _selectionSummary.Margin = new(0, 8, 0, 0);
        _selectionSummary.FontSize = 12;
        _selectionSummary.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        selectionColumn.Children.Add(_selectionSummary);
        var limitColumn = new StackPanel(); Grid.SetColumn(limitColumn, 4); options.Children.Add(limitColumn);
        limitColumn.Children.Add(Ui.Text("Max songs", 14));
        limitColumn.Children.Add(_limit);
        limitColumn.Children.Add(new TextBlock { Text = "1–100 songs", Margin = new(0, 8, 0, 0), FontSize = 12 });
        _first.Margin = new(0, 16, 0, 0);
        searchContent.Children.Add(_first);
        System.Windows.Automation.AutomationProperties.SetName(_question, "Music request");
        System.Windows.Automation.AutomationProperties.SetName(_scope, "Search in");
        System.Windows.Automation.AutomationProperties.SetName(_limit, "Maximum songs");
        _scope.ItemsSource = new[] { new Scope("Entire music library", null), new Scope("Current queue", "$queue"), new Scope("Selected songs / folders", "$selection") }
            .Concat(playlists.Select(x => new Scope(x.Name, x.Id))).ToArray();
        _scope.SelectedItem = _scope.Items.Cast<Scope>().FirstOrDefault(x => x.Id == (_chosen.Count > 0 ? "$selection" : selectedPlaylist)) ?? _scope.Items[0];
        _choose.Click += (_, _) =>
        {
            var chosen = _pick(_chosen);
            if (chosen is null) return;
            _chosen = chosen;
            _scope.SelectedItem = _scope.Items.Cast<Scope>().First(x => x.Id == "$selection");
            _selectionSummary.Text = $"{_chosen.Count:N0} songs selected from your indexed library.";
        };
        _selectionSummary.Text = $"{_chosen.Count:N0} songs selected from your indexed library.";
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
        _results.ItemsSource = _rows;
        root.Children.Add(_results);
        Closed += (_, _) => { _closed = true; _cancel?.Cancel(); };
    }
    private void AddButton(Panel panel, string label, Func<Task> action)
    {
        var button = new Button { Content = label, Margin = new(0, 0, 8, 6) };
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (OperationCanceledException) { _status.Text = "Stopped. Completed matches remain below; playback and queue were not changed."; }
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
        var first = _first.IsChecked == true;
        var chosen = _chosen.ToArray();
        _status.Text = $"Preparing library for {settings.AiProvider}…";
        _rows.Clear(); _actions.IsEnabled = false;
        using var cancel = new CancellationTokenSource(); _cancel = cancel;
        _scope.IsEnabled = _choose.IsEnabled = _first.IsEnabled = _question.IsEnabled = _limit.IsEnabled = false;
        try
        {
            var songs = await _inventory(scope, chosen);
            cancel.Token.ThrowIfCancellationRequested();
            if (songs.Length == 0) { _status.Text = "No available music in this scope."; return; }
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            var lookup = songs.ToDictionary(x => x.Id);
            var reviewed = 0;
            var progress = new Progress<string>(message => { if (!_closed && ReferenceEquals(_cancel, cancel) && !cancel.IsCancellationRequested) _status.Text = message; });
            var choices = await new AiPlaylistService(http).RecommendAsync(AiCredentials.Options(settings), question, songs, limit, progress, cancel.Token, first,
                (matches, completed) =>
                {
                    if (_closed || !ReferenceEquals(_cancel, cancel) || cancel.IsCancellationRequested) return;
                    reviewed = completed;
                    var ids = matches.Select(x => x.Id).ToHashSet();
                    foreach (var row in _rows.Where(x => !ids.Contains(x.Id)).ToArray()) _rows.Remove(row);
                    foreach (var match in matches)
                    {
                        if (_rows.Any(x => x.Id == match.Id)) continue;
                        var song = lookup[match.Id];
                        _rows.Add(new(match.Id, $"{song.Title} — {song.Artist}\n{song.Album} · {(song.Year == 0 ? "Year unknown" : song.Year)}\n{match.Reason}"));
                    }
                    for (var index = 0; index < matches.Count; index++)
                    {
                        var current = _rows.ToList().FindIndex(row => row.Id == matches[index].Id);
                        if (current != index) _rows.Move(current, index);
                    }
                    _results.SelectAll();
                    _status.Text = $"{_rows.Count} matches · reviewed {completed:N0} of {songs.Length:N0} songs. Results update after each batch.";
                });
            cancel.Token.ThrowIfCancellationRequested();
            if (_closed) return;
            _status.Text = $"Reviewed {reviewed:N0} of {songs.Length:N0} songs. {choices.Count} suggestions." +
                (first && reviewed < songs.Length ? " Stopped at your requested count." : " Review the reasons before playing.");
            if (play && choices.Count > 0) await _apply(choices.Select(x => x.Id).ToArray(), true);
        }
        finally
        {
            _cancel = null;
            _actions.IsEnabled = _rows.Count > 0;
            _scope.IsEnabled = _choose.IsEnabled = _first.IsEnabled = _question.IsEnabled = _limit.IsEnabled = true;
        }
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
