using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

/// <summary>Explicit, cancellable library enrichment. Saves provider data separately from file tags.</summary>
internal sealed class MusicBrainzWindow : Window
{
    private readonly MusicLookupService _service;
    private readonly Action<MediaItem, MusicBrainzInfo?> _save;
    private readonly Row[] _rows;
    private readonly DataGrid _grid = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, EnableRowVirtualization = true };
    private readonly TextBox _artist = new() { MaxLength = 256 };
    private readonly TextBox _title = new() { MaxLength = 256 };
    private readonly TextBox _album = new() { MaxLength = 256 };
    private readonly ListBox _candidates = new() { MinHeight = 95, MaxHeight = 160 };
    private readonly TextBox _details = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 90 };
    private readonly TextBlock _status = Ui.Text("Choose Lookup missing to enrich this list, or select one song and search for its recording.", 12, true);
    private readonly ProgressBar _progress = new() { Height = 5, Margin = new Thickness(0, 6, 0, 10) };
    private readonly List<Button> _actions = [];
    private readonly Button _cancel, _search, _use, _remove;
    private readonly StackPanel _query = new();
    private readonly TabControl _views = new();
    private Row? _candidateRow;
    private CancellationTokenSource? _cancellation;
    private Task? _operation;
    private bool _busy, _closed;

    public MusicBrainzWindow(IReadOnlyList<MediaItem> tracks, MusicLookupService service, Action<MediaItem, MusicBrainzInfo?> save)
    {
        _service = service; _save = save;
        _rows = tracks.Where(x => !x.IsVideo).Select(x => new Row(x)).ToArray();
        Ui.Theme(this);
        ThemeReviewViews();
        Title = "MusicBrainz info · Magic Music Wall";
        Width = 1100; Height = 820; MinWidth = 900; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        var header = new StackPanel();
        header.Children.Add(Ui.Text("MusicBrainz library information", 26));
        header.Children.Add(Ui.Text($"{_rows.Length:N0} songs · Saved information is used by the music map, including genre, community tags and available instrument credits.", 13, true));
        header.Children.Add(Ui.Text("Lookups send artist, title, optional album and MusicBrainz IDs. Original audio files and tags stay unchanged. Automatic matches require matching artist, title, duration and known album; uncertain versions need your review.", 12, true));
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 2) };
        actions.Children.Add(Action("Lookup missing", ct => Scan(false, ct)));
        actions.Children.Add(Action("Refresh saved", ct => Scan(true, ct)));
        _cancel = Ui.Button("Cancel", (sender, _) => { _cancellation?.Cancel(); if (sender is Button button) button.IsEnabled = false; _status.Text = "Stopping… saved results are kept."; });
        _cancel.IsEnabled = false; actions.Children.Add(_cancel); header.Children.Add(actions);
        header.Children.Add(_progress); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(_status);
        footer.Children.Add(Ui.Text("Source: MusicBrainz contributors. Core metadata: CC0. Community genres/tags: CC BY-NC-SA 3.0. Credits describe documented performances; missing credits do not mean an instrument is absent.", 11, true));
        var close = Ui.Button("Close", (_, _) => Close()); close.HorizontalAlignment = HorizontalAlignment.Right; footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); body.ColumnDefinitions.Add(new() { Width = new GridLength(430) });
        _grid.Margin = new Thickness(0, 0, 16, 0);
        _grid.Columns.Add(new DataGridTextColumn { Header = "Song", Binding = new Binding("Item.Title"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Artist", Binding = new Binding("Item.Artist"), Width = new DataGridLength(.8, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding(nameof(Row.Status)), Width = 135 });
        _grid.ItemsSource = _rows; AutomationProperties.SetName(_grid, "MusicBrainz library songs");
        body.Children.Add(_grid);
        Grid.SetColumn(_views, 1); body.Children.Add(_views);
        AddSearchField("Artist for search", _artist);
        AddSearchField("Title for search", _title);
        AddSearchField("Album for search (optional)", _album);
        AutomationProperties.SetName(_artist, "MusicBrainz search artist"); AutomationProperties.SetName(_title, "MusicBrainz search title");
        AutomationProperties.SetName(_album, "MusicBrainz search album");
        _search = Action("Search selected song", Search); _query.Children.Add(_search);
        _query.Children.Add(Ui.Text("Select the recording and album that match your copy.", 12, true));
        AutomationProperties.SetName(_candidates, "MusicBrainz recording candidates"); _query.Children.Add(_candidates);
        _use = Action("Use selected match", UseSelected);
        _remove = Action("Remove saved info", ct => { if (_grid.SelectedItem is Row row) Save(row, null); ShowSaved(); return Task.CompletedTask; });
        var findView = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(_use, Dock.Bottom); findView.Children.Add(_use);
        findView.Children.Add(new ScrollViewer { Content = _query, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _views.Items.Add(new TabItem { Header = "Find match", Content = findView });
        var savedView = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(_remove, Dock.Bottom); savedView.Children.Add(_remove);
        AutomationProperties.SetName(_details, "Saved MusicBrainz information"); savedView.Children.Add(_details);
        _views.Items.Add(new TabItem { Header = "Saved info", Content = savedView });
        root.Children.Add(body); Content = root;
        _grid.SelectionChanged += (_, _) => { if (_grid.SelectedItem is Row row) { _artist.Text = row.Item.Artist; _title.Text = row.Item.Title; _album.Text = row.Item.Album; } _candidateRow = null; _candidates.Items.Clear(); ShowSaved(); UpdateActions(); };
        _candidates.SelectionChanged += (_, _) => UpdateActions();
        Closed += (_, _) => { _closed = true; _cancellation?.Cancel(); };
        if (_rows.Length > 0) _grid.SelectedIndex = 0;
        UpdateActions();
    }

    private void AddSearchField(string label, TextBox field)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 5, 6) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(116) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var caption = Ui.Text(label, 12, true);
        caption.Margin = new Thickness(0, 0, 8, 0); caption.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(caption); Grid.SetColumn(field, 1); row.Children.Add(field); _query.Children.Add(row);
    }

    private void ThemeReviewViews()
    {
        _views.SetResourceReference(BackgroundProperty, "PanelBrush");
        _views.SetResourceReference(ForegroundProperty, "TextBrush");
        _views.BorderThickness = new Thickness(0);
        _candidates.SetResourceReference(BackgroundProperty, "BackgroundBrush");
        _candidates.SetResourceReference(ForegroundProperty, "TextBrush");
        _candidates.SetResourceReference(BorderBrushProperty, "LineBrush");
        _candidates.BorderThickness = new Thickness(1);

        // Match the Setup tabs instead of inheriting the system's white tab template.
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.PaddingProperty, new Thickness(16, 12, 16, 12));
        border.SetValue(Border.MarginProperty, new Thickness(0, 0, 4, 0));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7, 7, 0, 0));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
        var header = new FrameworkElementFactory(typeof(ContentPresenter));
        header.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        header.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        border.AppendChild(header);
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(ForegroundProperty, FindResource("MutedBrush")));
        style.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x20, 0x24, 0x30))));
        style.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(TabItem)) { VisualTree = border }));
        var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x39, 0x30, 0x4D))));
        selected.Setters.Add(new Setter(ForegroundProperty, FindResource("TextBrush")));
        style.Triggers.Add(selected);
        _views.Resources[typeof(TabItem)] = style;
    }

    public async Task ShutdownAsync()
    {
        _cancellation?.Cancel();
        if (_operation is { } operation) await operation;
    }

    private Button Action(string label, Func<CancellationToken, Task> action)
    {
        var button = Ui.Button(label, async (_, _) => await Run(action)); _actions.Add(button); return button;
    }

    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (_closed || _busy) return;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        _busy = true; UpdateActions();
        _operation = Execute();
        try { await _operation; }
        finally
        {
            _operation = null; _cancellation = null; _busy = false;
            foreach (var row in _rows.Where(row => row.Status == "Looking up…")) row.Status = "Try again";
            if (!_closed) { UpdateActions(); ShowSaved(); }
        }
        async Task Execute()
        {
            try { await action(cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { if (!_closed) _status.Text = "Stopped. Saved information is kept; Lookup missing skips songs already saved."; }
            catch (Exception ex) { if (!_closed) _status.Text = "Lookup stopped: " + ex.Message + " Saved information is kept."; }
        }
    }

    private void UpdateActions()
    {
        foreach (var button in _actions) button.IsEnabled = !_busy && _rows.Length > 0;
        _cancel.IsEnabled = _busy; _artist.IsEnabled = _title.IsEnabled = _album.IsEnabled = !_busy;
        _search.IsEnabled = !_busy && _grid.SelectedItem is Row;
        _use.IsEnabled = !_busy && _candidateRow == _grid.SelectedItem && _candidates.SelectedItem is ListBoxItem { Tag: RecordingMatch };
        _remove.IsEnabled = !_busy && _grid.SelectedItem is Row { Item.MusicBrainz: not null };
    }

    private async Task Scan(bool refresh, CancellationToken ct)
    {
        var targets = _rows.Where(row => refresh ? row.Item.MusicBrainz is not null : row.Item.MusicBrainz is null).ToArray();
        _progress.Maximum = Math.Max(1, targets.Length); _progress.Value = 0;
        var saved = 0; var review = 0;
        foreach (var row in targets)
        {
            ct.ThrowIfCancellationRequested();
            row.Status = "Looking up…";
            _status.Text = $"{_progress.Value:N0}/{targets.Length:N0} · {row.Item.Artist} — {row.Item.Title} · MusicBrainz requests are paced at one per second.";
            RecordingMatch? match;
            if (refresh && row.Item.MusicBrainz is { } old)
                match = new() { Id = old.RecordingId, Title = old.Title, Artist = old.Artist, Album = old.Album, ReleaseId = old.ReleaseId, FirstReleaseDate = old.FirstReleaseDate };
            else
            {
                if (string.IsNullOrWhiteSpace(row.Item.Artist) || string.IsNullOrWhiteSpace(row.Item.Title))
                { row.Status = "Needs review"; review++; _progress.Value++; continue; }
                var result = await _service.SearchRecordingsAsync(row.Item.Artist, row.Item.Title, ct, row.Item.Album);
                ct.ThrowIfCancellationRequested();
                if (result.Items.Count == 0 && result.Message?.StartsWith("No matching recordings", StringComparison.Ordinal) != true)
                { row.Status = "Try again"; throw new InvalidOperationException(result.Message ?? "MusicBrainz returned no usable response."); }
                match = result.IsComplete ? MusicLookupService.ChooseConfidentMatch(row.Item, result.Items) : null;
            }
            if (match is null) { row.Status = "Needs review"; review++; }
            else
            {
                var result = await _service.GetMusicBrainzInfoAsync(match, ct); ct.ThrowIfCancellationRequested();
                if (result.Info is null) { row.Status = "Try again"; throw new InvalidOperationException(result.Message ?? "MusicBrainz information could not be loaded."); }
                Save(row, result.Info); saved++;
            }
            _progress.Value++;
        }
        _status.Text = $"Finished: {saved:N0} saved, {review:N0} need review. Select a song to review its match. Saved data is available offline in the music map.";
    }

    private async Task Search(CancellationToken ct)
    {
        if (_grid.SelectedItem is not Row row) return;
        _candidates.Items.Clear(); _candidateRow = row;
        var result = await _service.SearchRecordingsAsync(_artist.Text, _title.Text, ct, _album.Text); ct.ThrowIfCancellationRequested();
        if (_grid.SelectedItem != row) return;
        foreach (var match in result.Items)
        {
            var text = new StackPanel(); text.Children.Add(Ui.Text(match.Title + " · " + match.Artist, 13));
            text.Children.Add(Ui.Text($"{match.Album} · {match.DurationSeconds:0}s · {match.FirstReleaseDate}\n{match.Disambiguation}", 12, true));
            _candidates.Items.Add(new ListBoxItem { Content = text, Tag = match });
        }
        _status.Text = result.Message ?? "Choose a recording to save its information.";
    }

    private async Task UseSelected(CancellationToken ct)
    {
        if (_grid.SelectedItem is not Row row || row != _candidateRow || _candidates.SelectedItem is not ListBoxItem { Tag: RecordingMatch match }) return;
        var result = await _service.GetMusicBrainzInfoAsync(match, ct); ct.ThrowIfCancellationRequested();
        if (result.Info is null) throw new InvalidOperationException(result.Message ?? "No MusicBrainz information was returned.");
        Save(row, result.Info);
        _views.SelectedIndex = 1;
        _status.Text = "Saved for the music map. " + result.Message;
    }

    private void Save(Row row, MusicBrainzInfo? info)
    {
        if (_closed) throw new OperationCanceledException();
        _save(row.Item, info); row.Item.MusicBrainz = info; row.Status = info is null ? "Not looked up" : "Saved";
    }

    private void ShowSaved()
    {
        if (_grid.SelectedItem is not Row row) { _details.Text = "Select a song."; return; }
        if (row.Item.MusicBrainz is not { } info) { _details.Text = $"Local file information:\n{row.Item.Title}\n{row.Item.Artist}\n{row.Item.Album}\n{row.Item.DurationText}\n\nNo MusicBrainz information saved yet."; return; }
        var text = new StringBuilder($"Saved MusicBrainz information\n{info.Title}\n{info.Artist}\nAlbum: {info.Album}\nFirst release: {info.FirstReleaseDate}\n\n");
        text.AppendLine("Genres (recording or album context): " + string.Join(", ", info.Genres));
        text.AppendLine("Community tags: " + string.Join(", ", info.Tags));
        text.AppendLine("Credited instruments: " + string.Join(", ", info.Instruments));
        text.AppendLine("Credits: " + string.Join("; ", info.Credits));
        text.AppendLine("\nRetrieved: " + info.RetrievedUtc.ToLocalTime().ToString("g"));
        text.AppendLine("Sources:\n" + string.Join("\n", info.Sources.Prepend(info.SourceUrl).Distinct()));
        _details.Text = text.ToString();
    }

    private sealed class Row(MediaItem item) : INotifyPropertyChanged
    {
        public MediaItem Item { get; } = item;
        private string _status = item.MusicBrainz is null ? "Not looked up" : "Saved";
        public string Status { get => _status; set { _status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
