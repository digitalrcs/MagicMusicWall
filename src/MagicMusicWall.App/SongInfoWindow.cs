using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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
    private readonly ListBox _articles = new() { MaxHeight = 130 };
    private readonly TextBox _lyrics = Reader();
    private readonly ListBox _lyricChoices = new() { MaxHeight = 150, Visibility = Visibility.Collapsed };
    private readonly TextBox _article = Reader();
    private readonly TextBlock _lyricsCredit = Ui.Text("", 12, true);
    private readonly TextBlock _articleCredit = Ui.Text("", 12, true);
    private readonly List<Button> _actions = [];
    private readonly Grid _views = new();
    private readonly List<Button> _tabs = [];
    private string _lyricsUrl = "", _articleUrl = "";
    private bool _busy, _closed;

    public SongInfoWindow(MediaItem item, MusicLookupService music, SongFactsService facts, Action<RecordingMatch?> saveAlbum, Action<Window>? editTags = null, Action<Window>? enrich = null)
    {
        _item = item; _music = music; _facts = facts; _saveAlbum = saveAlbum;
        Ui.Theme(this);
        Title = "Song info · Magic Music Wall"; Width = 840; Height = 840; MinWidth = 620; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel();
        header.Children.Add(Ui.Text(item.Title, 25));
        _album.Text = AlbumLabel(); header.Children.Add(_album);
        if (enrich is not null)
        {
            var info = Ui.Button("MusicBrainz library info…", (_, _) => enrich(this));
            info.HorizontalAlignment = HorizontalAlignment.Left; header.Children.Add(info);
        }
        if (editTags is not null)
        {
            var edit = Ui.Button("Edit file tags…", (_, _) => { editTags(this); _album.Text = AlbumLabel(); });
            edit.HorizontalAlignment = HorizontalAlignment.Left;
            header.Children.Add(edit);
        }
        var query = new Grid { Margin = new Thickness(0, 12, 0, 10) };
        query.ColumnDefinitions.Add(new()); query.ColumnDefinitions.Add(new());
        var artistPanel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        artistPanel.Children.Add(Ui.Text("Artist for lookup", 12, true)); _artist.Text = item.Artist;
        AutomationProperties.SetName(_artist, "Lookup artist"); artistPanel.Children.Add(_artist); query.Children.Add(artistPanel);
        var titlePanel = new StackPanel(); titlePanel.Children.Add(Ui.Text("Song title for lookup", 12, true)); _title.Text = item.Title;
        AutomationProperties.SetName(_title, "Lookup song title"); titlePanel.Children.Add(_title); Grid.SetColumn(titlePanel, 1); query.Children.Add(titlePanel);
        header.Children.Add(query);
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var name in new[] { "Info & albums", "Lyrics", "Song facts" })
        {
            var index = _tabs.Count;
            var button = Ui.Button(name, (_, _) => SelectTab(index)); _tabs.Add(button); tabs.Children.Add(button);
        }
        header.Children.Add(tabs); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        DockPanel.SetDock(_status, Dock.Bottom); _status.Margin = new Thickness(0, 14, 0, 0); root.Children.Add(_status);
        root.Children.Add(_views);
        _views.Children.Add(AlbumView()); _views.Children.Add(LyricsView()); _views.Children.Add(FactsView());
        Content = root; SelectTab(0);
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
    private static TextBox Reader() => new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, AcceptsReturn = true, FontSize = 15, Padding = new Thickness(16), IsReadOnlyCaretVisible = true };
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
        actions.Children.Add(Action("Open MusicBrainz source", () => { if (_matches.SelectedItem is ListBoxItem { Tag: RecordingMatch match }) OpenSource(match.SourceUrl); return Task.CompletedTask; }));
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
        actions.Children.Add(Action("Open lyrics source", () => { OpenSource(_lyricsUrl); return Task.CompletedTask; }));
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
            button.Content = Ui.Text(caption, 13); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            _lyricChoices.Items.Add(button);
        }
        _lyrics.Text = result.Instrumental ? "Instrumental — no sung lyrics are listed for this recording." : result.PlainText.Length > 0 ? result.PlainText : result.SyncedText.Length > 0 ? result.SyncedText : result.Message ?? "No matching lyrics found.";
        _lyricsCredit.Text = result.Found ? $"{result.SourceName} · {result.MatchedTitle} · {result.MatchedArtist}\n{result.MatchedAlbum}" : result.SourceName;
        _lyricsUrl = result.SourceUrl;
    }

    private UIElement FactsView()
    {
        var panel = new DockPanel();
        DockTop(panel, Ui.Text("Search Wikipedia for background, credits and recording history. Choose the matching article; similar titles may refer to a different song.", 13, true));
        DockTop(panel, Action("Find song facts", async () =>
        {
            _articles.Items.Clear(); _article.Clear(); _articleCredit.Text = ""; _articleUrl = ""; _status.Text = "Finding Wikipedia articles…";
            var result = await _facts.SearchAsync(_artist.Text, _title.Text, _cancel.Token);
            if (_closed) return;
            foreach (var candidate in result)
            {
                var entry = new StackPanel(); entry.Children.Add(Ui.Text(candidate.Title, 16)); entry.Children.Add(Ui.Text(candidate.Description, 12, true));
                _articles.Items.Add(new ListBoxItem { Content = entry, Tag = candidate });
            }
            _status.Text = result.Count == 0 ? "No matching articles found. Try adjusting the artist or title." : "Select an article and choose Read selected article.";
        }));
        DockTop(panel, _articles);
        var actions = new WrapPanel();
        actions.Children.Add(Action("Read selected article", async () =>
        {
            if (_articles.SelectedItem is not ListBoxItem { Tag: FactArticle selected }) { _status.Text = "Select a matching article first."; return; }
            _status.Text = "Loading article…";
            var article = await _facts.GetArticleAsync(selected.Id, _cancel.Token);
            if (_closed) return;
            _article.Text = article.Title + "\n\n" + article.Text; _articleUrl = article.Url; _articleCredit.Text = article.Attribution;
            _status.Text = "Excerpt loaded. Open the source for the full article and its references.";
        }));
        actions.Children.Add(Action("Open article source", () => { OpenSource(_articleUrl); return Task.CompletedTask; }));
        DockTop(panel, actions); Bottom(panel, _articleCredit); panel.Children.Add(_article); return panel;
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
