using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

/// <summary>A bounded similarity network over the entire indexed music collection.</summary>
internal sealed partial class MusicMapWindow : Window
{
    private const int PageSize = 48;
    private readonly Func<bool, Task<MusicMapIndex>> _rebuild;
    private readonly Func<IReadOnlyList<string>, Task> _enqueue;
    private readonly Action<string> _songInfo;
    private readonly Func<IReadOnlyList<string>?, Task> _enrich;
    private readonly Func<IProgress<MusicMapScanProgress>, CancellationToken, Task<MusicMapScanResult>> _analyze;
    private CancellationTokenSource? _analysisCancellation;
    private readonly ComboBox _similarity = new() { Width = 125, ItemsSource = Enum.GetValues<MusicMapSimilarity>() };
    private readonly ProgressBar _analysisProgress = new() { Height = 5, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 5, 14, 3), Visibility = Visibility.Collapsed };
    private readonly TextBlock _analysisText = Text("Optional: analyze up to 60 seconds per file locally for estimated timbre, rhythm and harmony. Unchanged files use cached results; cancelled scans can resume.", 11, true);
    private readonly Button _analyzeButton, _cancelAnalysis;
    private readonly TextBox _search = new() { MinWidth = 180, MaxLength = 256 };
    private readonly ComboBox _kind = new() { Width = 112, ItemsSource = Enum.GetValues<MusicMapKind>() };
    private readonly ComboBox _genre = new() { Width = 170 };
    private readonly Canvas _map = new() { Background = Brush("#141923"), Width = 1040, Height = 680 };
    private readonly ScaleTransform _scale = new(.72, .72);
    private readonly TextBlock _coverage = Text("Building your library map…", 12, true);
    private readonly ListBox _library = new() { DisplayMemberPath = nameof(MusicMapEntity.Name) };
    private readonly Stack<string> _history = new();
    private readonly Button _back;
    private readonly TextBlock _pageLabel = Text("", 12, true);
    private readonly TextBlock _scopeLabel = Text("All indexed music", 12, true);
    private readonly TextBlock _status = Text("", 12, true);
    private readonly StackPanel _details = new();
    private readonly Grid _toolbar = new();
    private readonly Grid _body = new();
    private readonly DispatcherTimer _libraryTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _libraryDirty;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Button _previous, _next, _rescan;
    private readonly ScrollViewer _mapScroll;
    private MusicMapIndex? _index;
    private List<MusicMapEntity> _filtered = [];
    private Dictionary<string, MusicMapEntity> _songs = new(StringComparer.Ordinal);
    private HashSet<string>? _scope;
    private string? _scopeId;
    private MusicMapEntity? _selected;
    private (string[] Ids, string? Folder)? _pendingFocus;

    public void FocusTracks(IReadOnlyList<string> ids, string? folder = null)
    {
        if (_closed || ids.Count == 0) return;
        _pendingFocus = (ids.ToArray(), folder);
        if (_index is not null && !_busy) ApplyPendingFocus();
    }

    private void ApplyPendingFocus()
    {
        if (_index is null || _pendingFocus is not { } focus) return;
        _pendingFocus = null;
        var kind = _kind.SelectedItem is MusicMapKind value ? value : MusicMapKind.Artist;
        var target = focus.Ids.Select(id => _index.Entities.FirstOrDefault(e => e.Kind == kind && e.TrackIds.Contains(id))).FirstOrDefault(e => e is not null);
        if (target is null) return;
        _history.Clear();
        Reveal(target, false);
        if (focus.Folder is not null)
        {
            _scope = focus.Ids.ToHashSet(StringComparer.Ordinal); _scopeId = null; _scopeLabel.Text = focus.Folder;
            Filter();
        }
    }
    private int _page;
    private bool _busy, _closed, _updating;
    private Point? _dragStart;
    private double _dragHorizontal, _dragVertical;

    public MusicMapWindow(Func<bool, Task<MusicMapIndex>> rebuild, Func<IReadOnlyList<string>, Task> enqueue, Action<string> songInfo,
        Func<IProgress<MusicMapScanProgress>, CancellationToken, Task<MusicMapScanResult>> analyze,
        Func<IReadOnlyList<string>?, Task> enrich)
    {
        _rebuild = rebuild; _enqueue = enqueue; _songInfo = songInfo; _analyze = analyze; _enrich = enrich;
        Ui.Theme(this);
        Title = "Magic Music Wall · Music constellation";
        Width = 1380; Height = 900; MinWidth = 1050; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(Text("YOUR LIBRARY, CONNECTED", 11, true));
        heading.Children.Add(Text("Music constellation", 30));
        heading.Children.Add(_coverage);
        var explanation = Text("Explore your library as a moving network. Stronger similarities pull names closer; related neighbors connect to one another. Click to explore, drag to move, then queue what you like.", 12, true);
        explanation.Margin = new Thickness(0, 8, 0, 16); heading.Children.Add(explanation); root.Children.Add(heading);

        _toolbar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var searchGroup = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        searchGroup.Children.Add(Text("Search names, genres, tags or instruments", 11, true)); searchGroup.Children.Add(_search);
        _toolbar.Children.Add(searchGroup);
        AddLabeledControl(_toolbar, "Show", _kind, 1); AddLabeledControl(_toolbar, "Genre", _genre, 2);
        _toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        AddLabeledControl(_toolbar, "Similarity", _similarity, 3);
        _rescan = Button("Rescan library & rebuild map", async () => await Rebuild(true));
        _rescan.VerticalAlignment = VerticalAlignment.Bottom; _rescan.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_rescan, 4); _toolbar.Children.Add(_rescan); Grid.SetRow(_toolbar, 1); root.Children.Add(_toolbar);
        AutomationProperties.SetName(_search, "Search music constellation");
        AutomationProperties.SetName(_kind, "Music constellation item type");
        AutomationProperties.SetName(_genre, "Music constellation genre filter");
        AutomationProperties.SetName(_similarity, "Music constellation similarity mode");
        var analysis = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        analysis.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        analysis.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        analysis.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var progress = new StackPanel(); progress.Children.Add(_analysisText); progress.Children.Add(_analysisProgress); analysis.Children.Add(progress);
        _analysisText.MaxHeight = 46; _analysisText.Margin = new Thickness(0, 0, 14, 0);
        _analyzeButton = Button("Analyze sound", async () => await AnalyzeSound());
        _cancelAnalysis = Button("Cancel analysis", CancelAnalysis);
        _cancelAnalysis.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(_analysisProgress, "Sound analysis progress");
        var analysisActions = new WrapPanel { VerticalAlignment = VerticalAlignment.Top };
        analysisActions.Children.Add(Button("MusicBrainz info", async () => await Enrich(null)));
        analysisActions.Children.Add(_analyzeButton); analysisActions.Children.Add(_cancelAnalysis);
        Grid.SetColumn(analysisActions, 1); Grid.SetColumnSpan(analysisActions, 2); analysis.Children.Add(analysisActions);
        Grid.SetRow(analysis, 2); root.Children.Add(analysis);

        var body = _body; body.Margin = new Thickness(0, 18, 0, 12);
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(360) });
        var mapPanel = new Grid { Margin = new Thickness(0, 0, 18, 0) };
        mapPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        mapPanel.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        mapPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var navigation = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        _back = Button("← Back", Back); DockPanel.SetDock(_back, Dock.Left); navigation.Children.Add(_back);
        var all = Button("All music", () => ResetScope()); DockPanel.SetDock(all, Dock.Left); navigation.Children.Add(all);
        _scopeLabel.VerticalAlignment = VerticalAlignment.Center; _scopeLabel.Margin = new Thickness(10, 0, 0, 0); navigation.Children.Add(_scopeLabel);
        mapPanel.Children.Add(navigation);
        _map.LayoutTransform = _scale;
        _mapScroll = new ScrollViewer { Content = _map, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.Both, Background = Brush("#141923") };
        AutomationProperties.SetName(_mapScroll, "Music constellation network");
        var browse = new Grid();
        browse.ColumnDefinitions.Add(new() { Width = new GridLength(190) });
        browse.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _library.Margin = new Thickness(0, 0, 10, 0);
        AutomationProperties.SetName(_library, "Music constellation library results");
        VirtualizingPanel.SetIsVirtualizing(_library, true);
        browse.Children.Add(_library);
        var graph = new Grid();
        graph.RowDefinitions.Add(new() { Height = GridLength.Auto });
        graph.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        graph.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _networkCaption.Margin = new Thickness(0, 0, 0, 7); graph.Children.Add(_networkCaption);
        Grid.SetRow(_mapScroll, 1); graph.Children.Add(_mapScroll);
        var mapHint = Text("Closer generally means more similar. Other links and label spacing also affect position. Drag a name; click to explore.", 11, true);
        mapHint.Margin = new Thickness(0, 7, 0, 0); Grid.SetRow(mapHint, 2); graph.Children.Add(mapHint);
        Grid.SetColumn(graph, 1); browse.Children.Add(graph);
        Grid.SetRow(browse, 1); mapPanel.Children.Add(browse);
        _library.SelectionChanged += (_, _) =>
        {
            if (_updating || _library.SelectedItem is not MusicMapEntity item || item.Id == _selected?.Id) return;
            FocusEntity(item, remember: true);
        };
        _map.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource != _map) return;
            _dragStart = e.GetPosition(_mapScroll); _dragHorizontal = _mapScroll.HorizontalOffset; _dragVertical = _mapScroll.VerticalOffset;
            _map.CaptureMouse(); _map.Cursor = Cursors.ScrollAll;
        };
        _map.MouseMove += (_, e) =>
        {
            if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(_mapScroll);
            _mapScroll.ScrollToHorizontalOffset(_dragHorizontal + start.X - point.X);
            _mapScroll.ScrollToVerticalOffset(_dragVertical + start.Y - point.Y);
        };
        _map.MouseLeftButtonUp += (_, _) => { _dragStart = null; _map.ReleaseMouseCapture(); _map.Cursor = Cursors.Arrow; };
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var zoom = new StackPanel { Orientation = Orientation.Horizontal };
        zoom.Children.Add(Button("−", () => Zoom(-.1), "Zoom out of music constellation"));
        zoom.Children.Add(Button("Fit", FitMap));
        zoom.Children.Add(Button("+", () => Zoom(.1), "Zoom into music constellation"));
        _motionButton = Button("Pause motion", ToggleMotion); zoom.Children.Add(_motionButton);
        DockPanel.SetDock(zoom, Dock.Left); bottom.Children.Add(zoom);
        var pages = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _previous = Button("← Previous", () => { _page--; _selected = null; DrawMap(); });
        _next = Button("Next →", () => { _page++; _selected = null; DrawMap(); });
        _pageLabel.VerticalAlignment = VerticalAlignment.Center; _pageLabel.Margin = new Thickness(9, 0, 9, 0);
        pages.Children.Add(_previous); pages.Children.Add(_pageLabel); pages.Children.Add(_next); bottom.Children.Add(pages);
        Grid.SetRow(bottom, 2); mapPanel.Children.Add(bottom); body.Children.Add(mapPanel);
        var detailScroll = new ScrollViewer { Content = _details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(16), Background = Brush("#191C25") };
        Grid.SetColumn(detailScroll, 1); body.Children.Add(detailScroll);
        Grid.SetRow(body, 3); root.Children.Add(body);
        Grid.SetRow(_status, 4); root.Children.Add(_status); Content = root;
        _updating = true; _kind.SelectedItem = MusicMapKind.Artist; _similarity.SelectedItem = MusicMapSimilarity.Blended; _genre.ItemsSource = new[] { "All genres" }; _genre.SelectedIndex = 0; _updating = false;
        _kind.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            var track = _selected?.TrackIds.FirstOrDefault();
            Filter();
            if (track is not null && _index is not null)
            {
                var entity = _filtered.FirstOrDefault(e => e.TrackIds.Contains(track));
                if (entity is not null) { _selected = entity; _page = _filtered.IndexOf(entity) / PageSize; DrawMap(); }
            }
        };
        _genre.SelectionChanged += (_, _) => { if (!_updating) Filter(); };
        _similarity.SelectionChanged += (_, _) => { if (!_updating) DrawMap(); };
        _search.TextChanged += (_, _) => { if (!_updating) { _searchTimer.Stop(); _searchTimer.Start(); } };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); Filter(); };
        _search.KeyDown += (_, e) => { if (e.Key == Key.Enter) { _searchTimer.Stop(); Filter(); } };
        Loaded += async (_, _) => await Rebuild(false);
        _libraryTimer.Tick += async (_, _) => { _libraryTimer.Stop(); if (!_busy && _libraryDirty) await Rebuild(false); };
        Closed += (_, _) => { _closed = true; _searchTimer.Stop(); _libraryTimer.Stop(); _analysisCancellation?.Cancel(); StopNetworkMotion(); };
        InitializeNetworkMotion();
        ShowDetails(null);
    }

    private async Task Rebuild(bool scan)
    {
        if (_closed) return;
        if (_busy) { _libraryDirty = true; return; }
        _libraryTimer.Stop(); _searchTimer.Stop(); _libraryDirty = false;
        var previousId = _selected?.Id; var previousGenre = _genre.SelectedItem as string; var initial = _index is null;
        SetBusy(true); _status.Text = scan ? "Scanning configured library folders and rebuilding the map… You can cancel a scan in the main player." : "Mapping every indexed music file…";
        try
        {
            var index = await _rebuild(scan);
            if (_closed) return;
            _index = index;
            _songs = index.Entities.Where(e => e.Kind == MusicMapKind.Song).SelectMany(e => e.TrackIds.Select(id => (id, e))).GroupBy(x => x.id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().e, StringComparer.Ordinal);
            _updating = true; _genre.ItemsSource = new[] { "All genres" }.Concat(index.Genres).Append("Unclassified").Distinct(StringComparer.OrdinalIgnoreCase).ToList(); _genre.SelectedItem = _genre.Items.Cast<string>().Contains(previousGenre) ? previousGenre : "All genres"; _updating = false;
            _coverage.Text = $"{index.TrackCount:N0} music files · {index.Entities.Count(e => e.Kind == MusicMapKind.Artist):N0} artists · {index.Entities.Count(e => e.Kind == MusicMapKind.Album):N0} albums · {index.TaggedTrackCount:N0} with genres · {index.AnalyzedTrackCount:N0} sound-analyzed · {index.EnrichedTrackCount:N0} MusicBrainz";
            if (_scopeId is not null)
            {
                var scopeEntity = index.Entities.FirstOrDefault(e => e.Id == _scopeId);
                _scope = scopeEntity?.TrackIds.ToHashSet(StringComparer.Ordinal);
                _scopeId = scopeEntity?.Id; _scopeLabel.Text = scopeEntity?.Name ?? "All indexed music";
            }
            if (initial) { ResetScope(); FitMap(); }
            else
            {
                Filter();
                var restored = _filtered.FirstOrDefault(e => e.Id == previousId);
                if (restored is not null) { _selected = restored; _page = _filtered.IndexOf(restored) / PageSize; DrawMap(); }
            }
            _status.Text = "Map ready. Click a name to explore, or drag it to feel its connections. Queue actions append songs without changing playback.";
        }
        catch (Exception ex) { if (!_closed) _status.Text = "Map could not be rebuilt: " + ex.Message; }
        finally { if (!_closed) { SetBusy(false); ApplyPendingFocus(); } }
    }

    private MusicMapSimilarity Similarity => _similarity.SelectedItem is MusicMapSimilarity mode ? mode : MusicMapSimilarity.Blended;

    private async Task Enrich(IReadOnlyList<string>? ids)
    {
        if (_busy || _closed) return;
        SetBusy(true);
        try { await _enrich(ids); }
        catch (Exception ex) { if (!_closed) _status.Text = ex.Message; }
        finally { if (!_closed) SetBusy(false); }
        if (!_closed) await Rebuild(false);
    }

    private void CancelAnalysis()
    {
        _analysisCancellation?.Cancel();
        _cancelAnalysis.IsEnabled = false;
        _analysisText.Text = "Stopping analysis… completed results will be kept.";
    }

    private async Task AnalyzeSound()
    {
        if (_closed || _busy || _analysisCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _analysisCancellation = cancellation;
        SetBusy(true); _analysisProgress.Visibility = Visibility.Visible;
        _analysisProgress.IsIndeterminate = true; _cancelAnalysis.Visibility = Visibility.Visible; _cancelAnalysis.IsEnabled = true;
        _analysisText.Text = "Preparing local sound analysis…";
        MusicMapScanProgress? last = null;
        string summary;
        var progress = new Progress<MusicMapScanProgress>(update =>
        {
            if (_closed || _analysisCancellation != cancellation) return;
            last = update;
            _analysisProgress.IsIndeterminate = update.Total <= 0;
            _analysisProgress.Maximum = Math.Max(1, update.Total); _analysisProgress.Value = Math.Clamp(update.Completed, 0, Math.Max(1, update.Total));
            _analysisText.Text = $"{update.Completed:N0}/{update.Total:N0} files · {update.Analyzed:N0} analyzed · {update.Cached:N0} cached · {update.Failed:N0} skipped/failed" +
                (string.IsNullOrWhiteSpace(update.CurrentTitle) ? "" : " — " + update.CurrentTitle);
            _analysisText.ToolTip = _analysisText.Text;
        });
        try
        {
            var result = await _analyze(progress, cancellation.Token);
            summary = $"{(result.Cancelled ? "Analysis cancelled" : "Sound analysis complete")}: {result.Analyzed:N0} analyzed, {result.Cached:N0} cached, {result.Failed:N0} skipped/failed.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            summary = $"Analysis cancelled: {last?.Analyzed ?? 0:N0} analyzed, {last?.Cached ?? 0:N0} cached, {last?.Failed ?? 0:N0} skipped/failed. Completed results are kept.";
        }
        catch (Exception ex) { summary = "Sound analysis stopped: " + ex.Message; }
        finally
        {
            _analysisCancellation = null;
            if (!_closed) { _cancelAnalysis.Visibility = Visibility.Collapsed; _analysisProgress.Visibility = Visibility.Collapsed; SetBusy(false); }
        }
        if (_closed) return;
        await Rebuild(false);
        if (_closed) return;
        _analysisText.Text = summary + " Analyze again to skip unchanged cached files and resume remaining work.";
        _analysisText.ToolTip = _analysisText.Text;
    }

    public void NotifyLibraryChanged()
    {
        if (_closed) return;
        _libraryDirty = true; _libraryTimer.Stop(); _libraryTimer.Start();
    }

    private void SetBusy(bool value)
    {
        _busy = value; _toolbar.IsEnabled = _body.IsEnabled = _analyzeButton.IsEnabled = !value;
        UpdateNetworkMotion();
        if (!value && _libraryDirty && !_closed) { _libraryTimer.Stop(); _libraryTimer.Start(); }
    }
    private void ResetScope()
    {
        _scope = null; _scopeId = null; _scopeLabel.Text = "All indexed music";
        _updating = true; _search.Clear(); _genre.SelectedIndex = 0; _updating = false;
        _history.Clear(); _selected = null; ShowDetails(null); Filter();
    }

    private void Filter()
    {
        if (_closed || _index is null) return;
        _searchTimer.Stop();
        var kind = _kind.SelectedItem is MusicMapKind k ? k : MusicMapKind.Artist;
        var genre = _genre.SelectedItem as string ?? "All genres";
        var query = _search.Text.Trim();
        _filtered = _index.Entities.Where(e => e.Kind == kind && (_scope is null || e.TrackIds.Any(_scope.Contains)) &&
            (genre == "All genres" || (genre == "Unclassified" ? e.Genres.Count == 0 : e.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase))) &&
            (query.Length == 0 || query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(term =>
                e.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || e.Artist.Contains(term, StringComparison.OrdinalIgnoreCase) || e.Album.Contains(term, StringComparison.OrdinalIgnoreCase)
                || e.Genres.Concat(e.Keywords).Concat(e.Instruments).Concat(e.Credits).Any(label => label.Contains(term, StringComparison.OrdinalIgnoreCase)))))
            .OrderBy(e => Route(e), StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
        _page = 0; DrawMap();
    }

    private string Route(MusicMapEntity item) => item.Genres.FirstOrDefault() ?? "Unclassified";
    private void DrawMap()
    {
        StopNetworkMotion();
        var totalPages = Math.Max(1, (_filtered.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, totalPages - 1);
        _previous.IsEnabled = _page > 0; _next.IsEnabled = _page + 1 < totalPages;
        _back.IsEnabled = _history.Count > 0;
        _pageLabel.Text = $"{_page + 1}/{totalPages} · {_filtered.Count:N0} results";
        var visible = _filtered.Skip(_page * PageSize).Take(PageSize).ToList();
        if (_selected is null || !visible.Any(e => e.Id == _selected.Id)) _selected = visible.FirstOrDefault();
        _updating = true; _library.ItemsSource = visible; _library.SelectedItem = _selected; _updating = false;
        if (_selected is null)
        {
            ClearNetwork();
            var empty = Text(_index?.TrackCount == 0 ? "Add music folders in the main player, then rebuild your map." : "No matches. Clear the search or choose All music.", 18, true);
            empty.Width = 600; Place(empty, 90, 250); ShowDetails(null); return;
        }
        var selected = _selected;
        var matches = _index!.Related(selected.Id, 18, Similarity);
        RenderNetwork(selected, matches);
        if (matches.Count == 0)
        {
            var help = Text(Similarity == MusicMapSimilarity.Sound ? "No sound matches yet. Choose Analyze sound to compare locally sampled audio. All music remains available in the library list." : "No related items found yet. Add metadata or choose Analyze sound to build more connections.", 14, true);
            help.Width = 500; help.TextAlignment = TextAlignment.Center; Place(help, (_map.Width - 500) / 2, _map.Height - 105);
        }
        ShowDetails(selected);
    }

    private static Brush ConnectionBrush(string reason)
    {
        if (reason.StartsWith("Estimated sound resemblance", StringComparison.Ordinal)) return Brush("#7BB8EF");
        if (reason.Contains("Shared genre:", StringComparison.Ordinal)) return Brush("#B8A5FF");
        if (reason.Contains("Same artist", StringComparison.Ordinal) || reason.Contains("Same album", StringComparison.Ordinal)) return Brush("#64CEBA");
        if (reason.Contains("instrument", StringComparison.OrdinalIgnoreCase)) return Brush("#64CEBA");
        if (reason.Contains("MusicBrainz", StringComparison.Ordinal)) return Brush("#B8A5FF");
        return Brush("#F1BC69");
    }

    private void FocusEntity(MusicMapEntity entity, bool remember)
    {
        if (remember && _selected is not null && _selected.Id != entity.Id) _history.Push(_selected.Id);
        _selected = entity; DrawMap();
    }

    private void Back()
    {
        if (_index is null || _history.Count == 0) return;
        var id = _history.Pop();
        var entity = _index.Entities.FirstOrDefault(e => e.Id == id);
        if (entity is not null) Reveal(entity, remember: false);
    }

    private void ShowDetails(MusicMapEntity? entity)
    {
        _details.Children.Clear();
        if (entity is null)
        {
            _details.Children.Add(Text("Choose some music", 23));
            _details.Children.Add(Text("Explore an artist, album or song. Select Queue to add its music to the main player's queue, or browse related music to build your own journey.", 13, true));
            _details.Children.Add(Text("Unclassified means no genre tag was found. Those files are still indexed and available to search and queue.", 12, true));
            return;
        }
        _details.Children.Add(Text(entity.Kind.ToString().ToUpperInvariant(), 11, true));
        _details.Children.Add(Text(entity.Name, 24));
        if (entity.Kind != MusicMapKind.Artist) _details.Children.Add(Text(entity.Artist, 13, true));
        _details.Children.Add(Text(string.Join(" · ", entity.Genres.Concat(entity.Decades.Select(d => d + "s"))), 12, true));
        _details.Children.Add(Text($"MusicBrainz information: {entity.MusicBrainzTrackCount:N0}/{entity.TrackIds.Count:N0} songs", 12, true));
        if (entity.Instruments.Count > 0) _details.Children.Add(Text("Credited instruments: " + string.Join(", ", entity.Instruments), 12, true));
        if (entity.MusicBrainzTags.Count > 0) _details.Children.Add(Text("Community tags: " + string.Join(", ", entity.MusicBrainzTags.Take(16)), 12, true));
        _details.Children.Add(Button("MusicBrainz for this selection", async () => await Enrich(entity.TrackIds)));
        var audio = _index?.AudioSummary(entity.Id);
        if (audio is not null)
        {
            var bpm = audio.Tempo > 0 && double.IsFinite(audio.Tempo) ? $" · estimated {audio.Tempo:0} BPM" : "";
            _details.Children.Add(Text($"Sound coverage: {audio.AnalyzedTracks:N0}/{audio.TotalTracks:N0} songs{bpm}", 12, true));
            if (entity.Kind != MusicMapKind.Song) _details.Children.Add(Text("Artist/album sound features are aggregated from analyzed song samples.", 11, true));
        }
        var queue = Button($"+ Queue {entity.TrackIds.Count:N0} {(entity.TrackIds.Count == 1 ? "song" : "songs")}", async () => await Queue(entity.TrackIds));
        queue.SetResourceReference(StyleProperty, "AccentButton"); queue.Margin = new Thickness(0, 12, 0, 8); _details.Children.Add(queue);
        var drill = new WrapPanel();
        if (entity.Kind == MusicMapKind.Artist) drill.Children.Add(Button("Explore albums", () => Drill(entity, MusicMapKind.Album)));
        if (entity.Kind != MusicMapKind.Song) drill.Children.Add(Button("Explore songs", () => Drill(entity, MusicMapKind.Song)));
        else drill.Children.Add(Button("Song info / lyrics", () => _songInfo(entity.TrackIds[0])));
        _details.Children.Add(drill);

        if (entity.Kind != MusicMapKind.Song)
        {
            _details.Children.Add(Section("Songs in this selection"));
            var tracks = entity.TrackIds.Where(_songs.ContainsKey).Select(id => _songs[id]).ToList();
            var list = new ListBox { Height = 180, ItemsSource = tracks, SelectionMode = SelectionMode.Extended, DisplayMemberPath = nameof(MusicMapEntity.Name) };
            AutomationProperties.SetName(list, "Songs in the selected artist or album");
            VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            var actions = new WrapPanel();
            var queueTracks = Button("Queue selected songs", async () => await Queue(list.SelectedItems.Cast<MusicMapEntity>().SelectMany(e => e.TrackIds).ToList()));
            var info = Button("Song info", () => { if (list.SelectedItem is MusicMapEntity song) _songInfo(song.TrackIds[0]); });
            queueTracks.IsEnabled = info.IsEnabled = false;
            list.SelectionChanged += (_, _) => { queueTracks.IsEnabled = list.SelectedItems.Count > 0; info.IsEnabled = list.SelectedItems.Count == 1; };
            actions.Children.Add(queueTracks); actions.Children.Add(info); _details.Children.Add(list); _details.Children.Add(actions);
            _details.Children.Add(Text("Ctrl-click or Shift-click to select several songs.", 11, true));
        }
        _details.Children.Add(Section("Related music"));
        var matches = _index!.Related(entity.Id, 18, Similarity);
        if (matches.Count == 0) _details.Children.Add(Text(Similarity == MusicMapSimilarity.Sound ? "Choose Analyze sound to create local sound comparisons. Unanalyzed music can still be queued." : "No related music found. Explore All music, improve saved tags, or choose Analyze sound.", 12, true));
        foreach (var match in matches)
        {
            var content = new StackPanel(); content.Children.Add(Text(match.Entity.Name, 13)); content.Children.Add(Text(match.Reason, 11, true));
            var related = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 3), Padding = new Thickness(10, 8, 10, 8), ToolTip = match.Entity.Name + "\n" + match.Reason };
            AutomationProperties.SetName(related, "Explore related " + match.Entity.Name + ". " + match.Reason);
            related.Click += (_, _) => Reveal(match.Entity); _details.Children.Add(related);
        }
    }

    private void Drill(MusicMapEntity entity, MusicMapKind kind)
    {
        _scope = entity.TrackIds.ToHashSet(StringComparer.Ordinal); _scopeId = entity.Id; _scopeLabel.Text = entity.Name;
        _updating = true; _search.Clear(); _genre.SelectedIndex = 0; _kind.SelectedItem = kind; _updating = false;
        _selected = null; ShowDetails(null); Filter();
    }

    private void Reveal(MusicMapEntity entity, bool remember = true)
    {
        if (remember && _selected is not null && _selected.Id != entity.Id) _history.Push(_selected.Id);
        _scope = null; _scopeId = null; _scopeLabel.Text = "All indexed music";
        _updating = true; _search.Clear(); _genre.SelectedIndex = 0; _kind.SelectedItem = entity.Kind; _updating = false;
        Filter(); _selected = entity; _page = Math.Max(0, _filtered.FindIndex(e => e.Id == entity.Id)) / PageSize;
        DrawMap(); _library.ScrollIntoView(entity);
    }

    private async Task Queue(IReadOnlyList<string> ids)
    {
        if (_busy || ids.Count == 0) return;
        SetBusy(true);
        try { await _enqueue(ids); if (!_closed) _status.Text = $"Added {ids.Count:N0} {(ids.Count == 1 ? "song" : "songs")} to the queue. Playback is unchanged."; }
        catch (Exception ex) { if (!_closed) _status.Text = "Could not add songs: " + ex.Message; }
        finally { if (!_closed) SetBusy(false); }
    }

    private void FitMap()
    {
        _mapScroll.UpdateLayout();
        var width = Math.Max(1, _mapScroll.ViewportWidth - 18);
        var height = Math.Max(1, _mapScroll.ViewportHeight - 18);
        _scale.ScaleX = _scale.ScaleY = Math.Min(1.3, Math.Min(width / _map.Width, height / _map.Height));
        _mapScroll.ScrollToHorizontalOffset(0);
        _mapScroll.ScrollToVerticalOffset(0);
    }

    private void Zoom(double delta) => _scale.ScaleX = _scale.ScaleY = Math.Clamp(_scale.ScaleX + delta, .2, 1.6);
    private void Place(UIElement item, double x, double y) { Canvas.SetLeft(item, x); Canvas.SetTop(item, y); _map.Children.Add(item); }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static TextBlock Text(string value, double size = 14, bool muted = false) => new() { Text = value, FontSize = size, Foreground = Brush(muted ? "#A3A9BD" : "#F4F3F8"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
    private static TextBlock Section(string title) { var result = Text(title, 14); result.FontWeight = FontWeights.SemiBold; result.Margin = new Thickness(0, 20, 0, 8); return result; }
    private static Button Button(string label, Action action, string? accessibleName = null)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 5, 5), MinHeight = 36 };
        AutomationProperties.SetName(button, accessibleName ?? label); button.Click += (_, _) => action(); return button;
    }
    private static void AddLabeledControl(Grid grid, string label, Control control, int column)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; panel.Children.Add(Text(label, 11, true)); panel.Children.Add(control); Grid.SetColumn(panel, column); grid.Children.Add(panel);
    }
}
