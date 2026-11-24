using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

internal sealed class SongInfoWindow : Window
{
    private readonly MediaItem _item;
    private readonly MusicLookupService _music;
    private readonly SongFactsService _facts;
    private readonly Action<RecordingMatch?> _saveAlbum;
    private readonly CancellationTokenSource _cancel = new();
    private readonly TextBox _artist = new() { MaxLength = 256 };
    private readonly TextBox _title = new() { MaxLength = 256 };
    private readonly TextBlock _album = Ui.Text("", 14, true);
    private readonly TextBlock _status = Ui.Text("Choose a viewer below. Online searches send artist and song details, never your audio files.", 12, true);
    private readonly ListBox _matches = new();
    private readonly ListBox _articles = new();
    private readonly TextBox _lyrics = Reader();
    private readonly ListBox _lyricChoices = new() { MaxHeight = 150, Visibility = Visibility.Collapsed };
    private readonly TextBox _article = Reader();
    private readonly TextBlock _lyricsCredit = Ui.Text("", 12, true);
    private readonly TextBlock _articleCredit = Ui.Text("", 12, true);
    private readonly List<Button> _actions = [];
    private readonly Grid _views = new();
    private readonly List<Button> _tabs = [];
    private string _articleUrl = "";
    private bool _busy, _closed;

    public SongInfoWindow(MediaItem item, MusicLookupService music, SongFactsService facts, Action<RecordingMatch?> saveAlbum, Action<Window>? editTags = null, Action<Window>? enrich = null, Action<Window>? aiNotes = null)
    {
        _item = item; _music = music; _facts = facts; _saveAlbum = saveAlbum;
        Ui.Theme(this);
        Title = "Song info · Magic Music Wall"; Width = 960; Height = Math.Min(940, SystemParameters.WorkArea.Height - 40); MinWidth = 620; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel();
        header.Children.Add(Ui.Text(item.Title, 25));
        _album.Text = AlbumLabel(); header.Children.Add(_album);
        var query = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        query.ColumnDefinitions.Add(new()); query.ColumnDefinitions.Add(new());
        var artistPanel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        artistPanel.Children.Add(Ui.Text("Artist for lookup", 12, true)); _artist.Text = item.Artist;
        AutomationProperties.SetName(_artist, "Lookup artist"); artistPanel.Children.Add(_artist); query.Children.Add(artistPanel);
        var titlePanel = new StackPanel(); titlePanel.Children.Add(Ui.Text("Song title for lookup", 12, true)); _title.Text = item.Title;
        AutomationProperties.SetName(_title, "Lookup song title"); titlePanel.Children.Add(_title); Grid.SetColumn(titlePanel, 1); query.Children.Add(titlePanel);
        header.Children.Add(query);
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var name in new[] { "Overview & albums", "Lyrics", "Articles", "MusicBrainz", "AI notes", "File tags" })
        {
            var index = _tabs.Count;
            var button = Ui.Button(name, (_, _) => SelectTab(index)); _tabs.Add(button); tabs.Children.Add(button);
        }
        header.Children.Add(tabs); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        DockPanel.SetDock(_status, Dock.Bottom); _status.Margin = new Thickness(0, 8, 0, 0); root.Children.Add(_status);
        root.Children.Add(_views);
        _views.Children.Add(AlbumView()); _views.Children.Add(LyricsView()); _views.Children.Add(FactsView());
        _views.Children.Add(DetailView("Saved library information", "MusicBrainz information enriches your library and constellation. It does not write your audio files.",
            () => _item.MusicBrainz is { } info ? $"{info.Title} · {info.Artist}\n{info.Album}\nFirst release: {info.FirstReleaseDate}\n\nGenres: {string.Join(", ", info.Genres)}\nTags: {string.Join(", ", info.Tags)}\nInstruments: {string.Join(", ", info.Instruments)}\n\n{string.Join("\n", info.Credits)}\n\nRetrieved {info.RetrievedUtc.LocalDateTime:g}" : "No MusicBrainz information saved. Open review to find and confirm the correct recording.", "Review MusicBrainz…", enrich));
        _views.Children.Add(DetailView("Saved AI notes", "AI notes are interpretations, not verified facts. Generating notes sends song metadata to your configured AI provider.",
            () => { try { return AiSongNotes.Load(App.DataDirectory, _item.Id)?.Text ?? "No notes saved for this song."; } catch { return "Saved notes could not be read."; } }, "Generate or review notes…", aiNotes));
        _views.Children.Add(DetailView("Physical file tags", "The reviewed editor previews changes and creates a backup before writing. Library album corrections and MusicBrainz information do not change these tags.",
            () => $"File: {_item.Path}\n\nTitle: {_item.Title}\nArtist: {_item.Artist}\nAlbum from file: {_item.TaggedAlbum}\nGenre: {_item.Genre}\nYear: {_item.Year}\nTrack: {_item.TrackNumber} · Disc: {_item.DiscNumber}\n\nThese are the last indexed values. Open the editor to read current file tags.", "Review and edit file tags…", editTags));
        var page = Ui.ScrollPage(root, header, _status, 420);
        AutomationProperties.SetName(page, "Song info page");
        AutomationProperties.SetName(_article, "Article reader");
        AutomationProperties.SetName(_lyrics, "Lyrics reader");
        _article.ToolTip = "Read the selected article here. Scroll inside this area for more text.";
        foreach (var list in new[] { _articles, _matches, _lyricChoices })
        {
            list.BorderThickness = new Thickness(1);
            list.SetResourceReference(Control.BorderBrushProperty, "LineBrush");
        }
        _articles.MouseDoubleClick += Articles_DoubleClick;
        _matches.MouseDoubleClick += Matches_DoubleClick;
        Content = page; SelectTab(0);
        Loaded += async (_, _) => await Run(async () =>
        {
            var local = await Task.Run(() => LocalLyrics.Read(_item), _cancel.Token);
            if (_closed) return;
            if (local is not null) ShowLyrics(local);
            else _lyrics.Text = "No embedded lyrics or matching .lrc / .txt file found. Use Find lyrics online to look up this song.";
        });
        Closed += (_, _) => { _closed = true; _cancel.Cancel(); };
    }

    private string AlbumLabel() => $"{_item.Artist} · {(_item.Album.Length > 0 ? _item.Album : "Album unknown")} · {_item.DurationText}";
    private static TextBox Reader() => new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, AcceptsReturn = true, FontSize = 15, Padding = new Thickness(12), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(19, 24, 33)), IsReadOnlyCaretVisible = true };
    private Button Action(string label, Func<Task> action)
    {
        var button = Ui.Button(label, async (_, _) => await Run(action)); _actions.Add(button); return button;
    }
    private static void DockTop(DockPanel panel, UIElement element) { DockPanel.SetDock(element, Dock.Top); panel.Children.Add(element); }
    private static void Bottom(DockPanel panel, UIElement element) { DockPanel.SetDock(element, Dock.Bottom); panel.Children.Add(element); }

    private UIElement AlbumView()
    {
        var panel = new DockPanel();
        DockTop(panel, Ui.Text("Find possible recordings and albums on MusicBrainz. Choose the release that matches your copy before saving its album name.", 13, true));
        DockTop(panel, Action("Look up song & albums", async () =>
        {
            _matches.Items.Clear(); _status.Text = "Searching MusicBrainz…";
            var result = await _music.SearchRecordingsAsync(_artist.Text, _title.Text, _cancel.Token);
            if (_closed) return;
            foreach (var match in result.Items)
            {
                var details = new StackPanel(); details.Children.Add(Ui.Text(match.Album.Length > 0 ? match.Album : "No album listed", 17));
                details.Children.Add(Ui.Text($"{match.Title} · {match.Artist}", 13));
                var date = match.ReleaseDate.Length > 0 ? $"This release: {match.ReleaseDate}" : "Release date unavailable";
                if (match.FirstReleaseDate.Length > 0) date += $" · Recording first released: {match.FirstReleaseDate}";
                details.Children.Add(Ui.Text(date, 12, true));
                if (match.Disambiguation.Length > 0) details.Children.Add(Ui.Text(match.Disambiguation, 12, true));
                _matches.Items.Add(new ListBoxItem { Content = details, Tag = match });
            }
            _status.Text = result.Message ?? $"{result.Items.Count} possible releases. Select a match to view its source or use its album.";
        }));
        var actions = new WrapPanel();
        actions.Children.Add(Action("Use selected album", () =>
        {
            if (_matches.SelectedItem is not ListBoxItem { Tag: RecordingMatch match } || match.Album.Length == 0) { _status.Text = "Select a result with an album first."; return Task.CompletedTask; }
            _saveAlbum(match); _album.Text = AlbumLabel(); _status.Text = $"Album saved in your library: {match.Album}. Your media file was not changed.";
            return Task.CompletedTask;
        }));
        actions.Children.Add(Action("Open MusicBrainz ↗", () => { if (_matches.SelectedItem is ListBoxItem { Tag: RecordingMatch match }) OpenSource(match.SourceUrl); return Task.CompletedTask; }));
        actions.Children.Add(Action("Restore album from file tag", () => { _saveAlbum(null); _album.Text = AlbumLabel(); _status.Text = "Restored the album from the original file tag."; return Task.CompletedTask; }));
        Bottom(panel, actions); panel.Children.Add(_matches);
        return panel;
    }

    private UIElement LyricsView()
    {
        var panel = new DockPanel();
        DockTop(panel, Ui.Text("Local lyrics load first: embedded tags, then a .lrc or .txt file beside the song with the same filename. Online results are supplied by LRCLIB.", 13, true));
        var actions = new WrapPanel();
        actions.Children.Add(Action("Find lyrics online", async () =>
        {
            _status.Text = "Looking for matching lyrics…";
            var result = await _music.GetLyricsAsync(_artist.Text, _title.Text, _item.Album, _item.DurationSeconds, _cancel.Token);
            if (!_closed) { ShowLyrics(result); _status.Text = result.Message ?? (result.Found ? "Lyrics loaded." : "No matching lyrics found."); }
        }));
        actions.Children.Add(Action("Reload local lyrics", async () =>
        {
            var result = await Task.Run(() => LocalLyrics.Read(_item), _cancel.Token);
            if (!_closed)
            {
                ShowLyrics(result ?? new LyricsResult { Message = "No local lyrics found.", SourceName = "Local library", SourceUrl = "" });
                _status.Text = result?.Message ?? "No local lyrics found.";
            }
        }));
        DockTop(panel, actions); DockTop(panel, _lyricChoices); Bottom(panel, _lyricsCredit); panel.Children.Add(_lyrics); return panel;
    }

    private void ShowLyrics(LyricsResult result)
    {
        _lyricChoices.Items.Clear();
        _lyricChoices.Visibility = result.Choices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var choice in result.Choices)
        {
            var length = choice.DurationSeconds is > 0 and < 864000
                ? TimeSpan.FromSeconds(choice.DurationSeconds).ToString(choice.DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "Length unavailable";
            var caption = $"View lyrics · {choice.Lyrics.MatchedAlbum}\n{choice.Lyrics.MatchedTitle} · {choice.Lyrics.MatchedArtist} · {length}";
            var button = Ui.Button("", async (_, _) => await Run(() =>
            {
                ShowLyrics(choice.Lyrics); _status.Text = choice.Lyrics.Message ?? "Selected lyrics version loaded.";
                return Task.CompletedTask;
            }));
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Content = Ui.Text(caption, 13); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            _lyricChoices.Items.Add(button);
        }
        _lyrics.Text = result.Instrumental ? "Instrumental — no sung lyrics are listed for this recording." : result.PlainText.Length > 0 ? result.PlainText : result.SyncedText.Length > 0 ? result.SyncedText : result.Message ?? "No matching lyrics found.";
        _lyricsCredit.Text = result.Found ? $"{result.SourceName} · {result.MatchedTitle} · {result.MatchedArtist}\n{result.MatchedAlbum}" : result.SourceName;
    }

    private UIElement FactsView()
    {
        var panel = new DockPanel();
        DockTop(panel, Ui.Text("Search Wikipedia for background, credits and recording history. Choose the matching article; similar titles may refer to a different song.", 13, true));
        var actions = new WrapPanel();
        actions.Children.Add(Action("Find song facts", async () =>
        {
            _articles.Items.Clear(); _article.Clear(); _articleCredit.Text = ""; _articleUrl = ""; _status.Text = "Finding Wikipedia articles…";
            var result = await _facts.SearchAsync(_artist.Text, _title.Text, _cancel.Token);
            if (_closed) return;
            foreach (var candidate in result)
            {
                var entry = new StackPanel(); entry.Children.Add(Ui.Text(candidate.Title, 16)); entry.Children.Add(Ui.Text(candidate.Description, 12, true));
                _articles.Items.Add(new ListBoxItem { Content = entry, Tag = candidate });
            }
            _status.Text = result.Count == 0 ? "No matching articles found. Try adjusting the artist or title." : "Double-click an article to read it, or choose Read selected article.";
        }));

        actions.Children.Add(Action("Read selected article", async () =>
        {
            if (_articles.SelectedItem is not ListBoxItem { Tag: FactArticle selected }) { _status.Text = "Select a matching article first."; return; }
            await ReadArticleAsync(selected);
        }));
        actions.Children.Add(Action("Read full article ↗", () => { OpenSource(_articleUrl); return Task.CompletedTask; }));
        DockTop(panel, actions); Bottom(panel, _articleCredit);
        var split = new Grid();
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 70 });
        split.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 180 });
        split.Children.Add(_articles);
        var divider = new GridSplitter { Height = 7, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Rows, Margin = new Thickness(0, 4, 0, 4) };
        divider.SetResourceReference(Control.BackgroundProperty, "LineBrush");
        AutomationProperties.SetName(divider, "Resize article results and reader"); Grid.SetRow(divider, 1); split.Children.Add(divider);
        var reader = new DockPanel(); DockTop(reader, Ui.Text("Article excerpt · open the source for the full article", 12, true)); reader.Children.Add(_article);
        Grid.SetRow(reader, 2); split.Children.Add(reader); panel.Children.Add(split); return panel;
    }

    private UIElement DetailView(string title, string help, Func<string> text, string actionLabel, Action<Window>? open)
    {
        var panel = new DockPanel();
        DockTop(panel, Ui.Text(title, 18)); DockTop(panel, Ui.Text(help, 13, true));
        var reader = Reader(); reader.Text = text();
        if (open is not null) DockTop(panel, Action(actionLabel, () => { open(this); reader.Text = text(); _album.Text = AlbumLabel(); return Task.CompletedTask; }));
        panel.Children.Add(reader); return panel;
    }

    private static ListBoxItem? ClickedResult(ListBox list, MouseButtonEventArgs e) =>
        e.ChangedButton == MouseButton.Left && e.OriginalSource is DependencyObject source
            ? ItemsControl.ContainerFromElement(list, source) as ListBoxItem : null;

    private async void Articles_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_busy || _closed || ClickedResult(_articles, e) is not { Tag: FactArticle selected } row) return;
        e.Handled = true;
        _articles.SelectedItem = row;
        await Run(() => ReadArticleAsync(selected));
    }

    private async void Matches_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_busy || _closed || ClickedResult(_matches, e) is not { Tag: RecordingMatch selected } row) return;
        e.Handled = true;
        _matches.SelectedItem = row;
        await Run(() => { OpenSource(selected.SourceUrl); return Task.CompletedTask; });
    }

    private async Task ReadArticleAsync(FactArticle selected)
    {
        _status.Text = "Loading article…";
        var article = await _facts.GetArticleAsync(selected.Id, _cancel.Token);
        if (_closed) return;
        _article.Text = article.Title + "\n\n" + article.Text;
        _article.ScrollToHome();
        _articleUrl = article.Url; _articleCredit.Text = article.Attribution;
        _status.Text = "Excerpt loaded. Open the source for the full article and its references.";
    }

    private void SelectTab(int index)
    {
        for (var i = 0; i < _views.Children.Count; ++i) _views.Children[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < _tabs.Count; ++i) _tabs[i].Style = (Style)FindResource(i == index ? "AccentButton" : typeof(Button));
    }
    private void OpenSource(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host is "musicbrainz.org" or "lrclib.net" or "en.wikipedia.org")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        else _status.Text = "No online source for this selection.";
    }
    private async Task Run(Func<Task> action)
    {
        if (_busy || _closed) return;
        _busy = true; _artist.IsEnabled = _title.IsEnabled = false; foreach (var button in _actions) button.IsEnabled = false;
        try { await action(); }
        catch (OperationCanceledException) { if (!_closed) _status.Text = "Request cancelled."; }
        catch (Exception ex) { if (!_closed) _status.Text = ex.Message; }
        finally { _busy = false; if (!_closed) { _artist.IsEnabled = _title.IsEnabled = true; foreach (var button in _actions) button.IsEnabled = true; } }
    }
}
