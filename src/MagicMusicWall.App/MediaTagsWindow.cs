using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

internal sealed class MediaTagsWindow : Window
{
    private sealed record LookupChoice(string Caption, string Provider, string SourceUrl, IReadOnlyDictionary<string, string> Values);
    private sealed record TagChange(string Field, string Before, string After);
    private sealed class TagRow(string label, bool numeric, bool multiline)
    {
        public string Label { get; } = label;
        public bool Numeric { get; } = numeric;
        public CheckBox Apply { get; } = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        public TextBlock Current { get; } = new() { Text = "Not read", TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = multiline ? 70 : 38, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 3, 12, 3) };
        public TextBox Proposed { get; } = new() { MaxLength = numeric ? 4 : multiline ? 16384 : 1024, MinHeight = 32, Height = multiline ? 76 : 32, Padding = new Thickness(7, 4, 7, 4), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden, Margin = new Thickness(0, 3, 0, 3), FontSize = 13 };
    }

    private readonly MediaItem _item;
    private readonly MusicLookupService _music;
    private readonly MovieLookupService _movies;
    private readonly Func<string> _movieToken;
    private readonly Func<MediaTagPatch, CancellationToken, Task<MediaTagWriteResult>> _save;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, TagRow> _rows = [];
    private readonly Dictionary<string, string> _currentValues = [];
    private readonly TextBox _title = new() { MaxLength = 256 };
    private readonly TextBox _artist = new() { MaxLength = 256 };
    private readonly ListBox _matches = new() { DisplayMemberPath = nameof(LookupChoice.Caption), Height = 112 };
    private readonly TextBlock _status = Ui.Text("Reading file tags…", 12, true);
    private readonly TextBlock _previewCredit = Ui.Text("No online match previewed. You can edit proposed values manually.", 12, true);
    private readonly TextBlock _tokenHelp = Ui.Text("", 12, true);
    private readonly TextBox _backup = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, FontSize = 11, Padding = new Thickness(6), MaxHeight = 52, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button _search, _cancelSearch, _preview, _source, _reload, _saveButton, _close;
    private readonly bool _canWrite;
    private CancellationTokenSource? _lookupCancel;
    private int _lookupVersion;
    private bool _reading, _lookingUp, _saving, _closed, _loaded, _updatingRows, _manualEdits;
    private string _previewDescription = "", _previewUrl = "";

    public MediaTagsWindow(MediaItem item, MusicLookupService music, MovieLookupService movies, Func<string> movieToken,
        Func<MediaTagPatch, CancellationToken, Task<MediaTagWriteResult>> save)
    {
        _item = item; _music = music; _movies = movies; _movieToken = movieToken; _save = save;
        _canWrite = MediaTagService.SupportsWriting(item.Path);
        Ui.Theme(this);
        Title = "Metadata · Magic Music Wall"; Width = 940; Height = 820; MinWidth = 720; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel();
        header.Children.Add(Ui.Text("Metadata", 23));
        var path = Ui.Text(item.Path, 12, true); path.ToolTip = item.Path; path.MaxHeight = 42;
        header.Children.Add(path);
        header.Children.Add(QueryFields());
        _search = Ui.Button("Search online", async (_, _) => await SearchAsync());
        _cancelSearch = Ui.Button("Cancel lookup", (_, _) => CancelLookup());
        _preview = Ui.Button("Preview selected", (_, _) => PreviewSelected());
        _source = Ui.Button("Open selected source", (_, _) => OpenSource((_matches.SelectedItem as LookupChoice)?.SourceUrl));
        _reload = Ui.Button("Reload file tags", async (_, _) => await LoadTagsAsync(true));
        var actions = new WrapPanel();
        foreach (var button in new[] { _search, _cancelSearch, _preview, _source, _reload }) actions.Children.Add(button);
        header.Children.Add(actions);
        var provider = Ui.Text(item.IsVideo ? MovieLookupService.Attribution : "Music metadata: MusicBrainz. Search sends only the artist and title fields above; no files or paths are uploaded.", 11, true);
        if (item.IsVideo) provider.Text += " Search sends only the title above; no files or paths are uploaded.";
        provider.Margin = new Thickness(0, 3, 0, 7); header.Children.Add(provider);
        var setupLink = new Hyperlink(new Run("Setup instructions"));
        setupLink.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
        setupLink.Click += (_, _) => MessageBox.Show(this, "Close this window, open Setup, add your TMDB API Read Access Token, and save. Then reopen Metadata to search for movies. No token is entered in this window.", "Movie lookup setup", MessageBoxButton.OK, MessageBoxImage.Information);
        _tokenHelp.Inlines.Add(new Run("Movie lookup needs a TMDB token. ")); _tokenHelp.Inlines.Add(setupLink);
        _tokenHelp.Visibility = Visibility.Collapsed; header.Children.Add(_tokenHelp);
        AutomationProperties.SetName(_matches, "Online metadata matches");
        var matchStyle = new Style(typeof(ListBoxItem), (Style)FindResource(typeof(ListBoxItem)));
        matchStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
        matchStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(LookupChoice.Caption))));
        _matches.ItemContainerStyle = matchStyle;
        _matches.FontSize = 12;
        header.Children.Add(_matches);
        _previewCredit.Margin = new Thickness(0, 8, 0, 8); header.Children.Add(_previewCredit);
        var support = Ui.Text(_canWrite ? "Check only the fields to write. Unchecked fields stay unchanged. Use 0 to clear year, track, or disc." : $"Writing tags is not supported for {Path.GetExtension(item.Path)} files. Online lookup and preview are still available.", 12, true);
        header.Children.Add(support);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);

        var footer = new StackPanel();
        _status.Margin = new Thickness(0, 10, 0, 6); footer.Children.Add(_status); footer.Children.Add(_backup);
        var footerButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _close = Ui.Button("Close", (_, _) => Close()); _close.IsCancel = true;
        _saveButton = Ui.Button("Save checked tags", async (_, _) => await SaveAsync());
        _saveButton.SetResourceReference(StyleProperty, "AccentButton");
        footerButtons.Children.Add(_close); footerButtons.Children.Add(_saveButton); footer.Children.Add(footerButtons);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(EditorTable());
        Content = root;

        _matches.SelectionChanged += (_, _) => UpdateControls();
        _title.TextChanged += (_, _) => QueryChanged(); _artist.TextChanged += (_, _) => QueryChanged();
        Loaded += async (_, _) => await LoadTagsAsync(false);
        Closing += (_, e) =>
        {
            if (_saving) { e.Cancel = true; _status.Text = "Tags are being saved. Wait for the save to finish before closing."; return; }
            _closed = true; _lookupCancel?.Cancel(); _lifetime.Cancel();
        };
        Closed += (_, _) => _lifetime.Dispose();
        UpdateControls();
    }

    private UIElement QueryFields()
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        var titlePanel = new StackPanel { Margin = new Thickness(0, 0, _item.IsVideo ? 0 : 10, 0) };
        var titleLabel = Ui.Text(_item.IsVideo ? "Movie title for lookup" : "Song title for lookup", 12, true); titleLabel.Margin = new Thickness(0, 0, 0, 5);
        titlePanel.Children.Add(titleLabel); titlePanel.Children.Add(_title);
        _title.Text = string.IsNullOrWhiteSpace(_item.Title) ? Path.GetFileNameWithoutExtension(_item.Path) : _item.Title;
        AutomationProperties.SetName(_title, "Lookup title"); grid.Children.Add(titlePanel);
        var artistPanel = new StackPanel();
        var artistLabel = Ui.Text("Artist for lookup", 12, true); artistLabel.Margin = new Thickness(0, 0, 0, 5);
        artistPanel.Children.Add(artistLabel); artistPanel.Children.Add(_artist); _artist.Text = _item.Artist;
        AutomationProperties.SetName(_artist, "Lookup artist"); Grid.SetColumn(artistPanel, 1); grid.Children.Add(artistPanel);
        if (_item.IsVideo) { artistPanel.Visibility = Visibility.Collapsed; Grid.SetColumnSpan(titlePanel, 2); }
        return grid;
    }

    private UIElement EditorTable()
    {
        var outer = new DockPanel();
        var heading = Columns();
        AddCell(heading, Ui.Text("Write", 11, true), 0, 0);
        AddCell(heading, Ui.Text("Field", 11, true), 0, 1);
        AddCell(heading, Ui.Text("Current file tag", 11, true), 0, 2);
        AddCell(heading, Ui.Text("Proposed value", 11, true), 0, 3);
        DockPanel.SetDock(heading, Dock.Top); outer.Children.Add(heading);
        var table = Columns();
        var definitions = new (string Key, string Label, bool Numeric, bool Multiline)[]
        {
            ("Title", "Title", false, false), ("Artist", "Artist", false, false), ("Album", "Album", false, false),
            ("AlbumArtist", "Album artist", false, false), ("Genre", "Genre", false, false),
            ("Year", "Year", true, false), ("TrackNumber", "Track", true, false), ("DiscNumber", "Disc", true, false),
            ("Comment", "Comment / overview", false, true)
        };
        for (var index = 0; index < definitions.Length; index++)
        {
            var (key, label, numeric, multiline) = definitions[index];
            var row = new TagRow(label, numeric, multiline); _rows.Add(key, row); _currentValues[key] = "";
            table.RowDefinitions.Add(new() { Height = GridLength.Auto });
            AutomationProperties.SetName(row.Apply, "Write " + label);
            AutomationProperties.SetName(row.Proposed, "Proposed " + label);
            row.Apply.Checked += (_, _) => UpdateControls(); row.Apply.Unchecked += (_, _) => UpdateControls();
            row.Proposed.TextChanged += (_, _) =>
            {
                if (_updatingRows) return;
                _manualEdits = true; UpdatePreviewCredit(); UpdateControls();
            };
            var name = Ui.Text(label, 12); name.Margin = new Thickness(0, 5, 8, 5); name.VerticalAlignment = VerticalAlignment.Center;
            AddCell(table, row.Apply, index, 0); AddCell(table, name, index, 1); AddCell(table, row.Current, index, 2); AddCell(table, row.Proposed, index, 3);
        }
        outer.Children.Add(new ScrollViewer { Content = table, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        return outer;
    }

    private static Grid Columns()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(48) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(108) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1.25, GridUnitType.Star) });
        return grid;
    }
    private static void AddCell(Grid grid, UIElement element, int row, int column)
    { Grid.SetRow(element, row); Grid.SetColumn(element, column); grid.Children.Add(element); }

    private async Task LoadTagsAsync(bool preserveProposals)
    {
        if (_reading || _saving || _closed) return;
        var preserve = preserveProposals && (_manualEdits || _rows.Values.Any(row => row.Apply.IsChecked == true));
        _reading = true; _status.Text = "Reading tags directly from the file…"; UpdateControls();
        try
        {
            var token = _lifetime.Token;
            var tags = await Task.Run(() => MediaTagService.Read(_item.Path), token);
            if (_closed) return;
            ApplyTags(tags, !preserve); _loaded = true;
            _status.Text = _canWrite ? "File tags loaded. Preview a match or edit values, then check the fields you want to save." : "File tags loaded for viewing. This format cannot be written here.";
        }
        catch (OperationCanceledException) { if (!_closed) _status.Text = "Reading file tags was cancelled."; }
        catch (Exception ex)
        {
            if (!_closed) { _loaded = false; _status.Text = "File tags could not be read. Saving is disabled; online lookup still works. " + ex.Message; }
        }
        finally { _reading = false; if (!_closed) UpdateControls(); }
    }

    private void ApplyTags(MediaTags tags, bool resetProposals)
    {
        var values = new Dictionary<string, string>
        {
            ["Title"] = tags.Title ?? "", ["Artist"] = tags.Artist ?? "", ["Album"] = tags.Album ?? "", ["AlbumArtist"] = tags.AlbumArtist ?? "",
            ["Genre"] = tags.Genre ?? "", ["Comment"] = tags.Comment ?? "", ["Year"] = tags.Year.ToString(CultureInfo.InvariantCulture),
            ["TrackNumber"] = tags.TrackNumber.ToString(CultureInfo.InvariantCulture), ["DiscNumber"] = tags.DiscNumber.ToString(CultureInfo.InvariantCulture)
        };
        _updatingRows = true;
        try
        {
            foreach (var (key, row) in _rows)
            {
                _currentValues[key] = values[key]; row.Current.Text = Display(values[key]); row.Current.ToolTip = row.Current.Text;
                if (resetProposals) { row.Proposed.Text = values[key]; row.Apply.IsChecked = false; }
            }
        }
        finally { _updatingRows = false; }
        if (resetProposals) { _previewDescription = ""; _previewUrl = ""; _manualEdits = false; UpdatePreviewCredit(); }
    }

    private void QueryChanged()
    {
        CancelLookup(false); _matches.ItemsSource = null;
        if (!_closed) { _status.Text = "Search fields changed. Search online to use these details."; UpdateControls(); }
    }
    private void CancelLookup(bool report = true)
    {
        ++_lookupVersion; _lookupCancel?.Cancel(); _lookupCancel = null; _lookingUp = false;
        if (report) _status.Text = "Online lookup cancelled.";
        if (!_closed) UpdateControls();
    }

    private async Task SearchAsync()
    {
        if (_saving || _lookingUp || _closed || string.IsNullOrWhiteSpace(_title.Text)) return;
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _lookupCancel = cancel; var version = ++_lookupVersion;
        _lookingUp = true; _matches.ItemsSource = null; _status.Text = _item.IsVideo ? "Searching TMDB…" : "Searching MusicBrainz…"; UpdateControls();
        try
        {
            var choices = new List<LookupChoice>(); string? message;
            if (_item.IsVideo)
            {
                var token = _movieToken(); _tokenHelp.Visibility = string.IsNullOrWhiteSpace(token) ? Visibility.Visible : Visibility.Collapsed;
                var result = await _movies.SearchAsync(_title.Text.Trim(), token, cancel.Token); message = result.Message;
                foreach (var match in result.Items)
                {
                    var values = new Dictionary<string, string> { ["Title"] = match.Title, ["Genre"] = string.Join("; ", match.Genres), ["Comment"] = match.Overview };
                    AddYear(values, match.ReleaseDate);
                    var original = match.OriginalTitle.Length > 0 && match.OriginalTitle != match.Title ? " · " + match.OriginalTitle : "";
                    choices.Add(new($"{match.Title}{original}\nRelease: {match.ReleaseDate}", "TMDB", match.SourceUrl, values));
                }
            }
            else
            {
                var result = await _music.SearchRecordingsAsync(_artist.Text.Trim(), _title.Text.Trim(), cancel.Token); message = result.Message;
                foreach (var match in result.Items)
                {
                    var date = string.IsNullOrWhiteSpace(match.ReleaseDate) ? match.FirstReleaseDate : match.ReleaseDate;
                    var values = new Dictionary<string, string> { ["Title"] = match.Title, ["Artist"] = match.Artist, ["Album"] = match.Album };
                    AddYear(values, date);
                    choices.Add(new($"{match.Title} · {match.Artist}\n{match.Album} · {date}", "MusicBrainz", match.SourceUrl, values));
                }
            }
            if (_closed || cancel.IsCancellationRequested || version != _lookupVersion) return;
            _matches.ItemsSource = choices;
            _status.Text = message ?? (choices.Count == 0 ? "No matches found. Adjust the lookup fields and try again." : $"{choices.Count} matches. Select the correct release, then choose Preview selected. No tags have changed.");
        }
        catch (OperationCanceledException) { if (!_closed && version == _lookupVersion) _status.Text = "Online lookup cancelled. You can try again."; }
        catch (Exception ex)
        {
            if (!_closed && version == _lookupVersion)
            {
                _status.Text = "Online lookup failed. " + ex.Message;
                if (_item.IsVideo) _tokenHelp.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            cancel.Dispose();
            if (version == _lookupVersion) { _lookupCancel = null; _lookingUp = false; if (!_closed) UpdateControls(); }
        }
    }

    private static void AddYear(Dictionary<string, string> values, string date)
    {
        if (date.Length >= 4 && uint.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year is > 0 and <= 9999)
            values["Year"] = year.ToString(CultureInfo.InvariantCulture);
    }

    private void PreviewSelected()
    {
        if (_saving || _reading || _matches.SelectedItem is not LookupChoice choice) return;
        _updatingRows = true;
        try
        {
            foreach (var (key, row) in _rows)
            {
                row.Proposed.Text = _currentValues[key]; row.Apply.IsChecked = false;
                if (!choice.Values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) continue;
                row.Proposed.Text = value;
                row.Apply.IsChecked = _loaded && !string.Equals(value, _currentValues[key], StringComparison.Ordinal);
            }
        }
        finally { _updatingRows = false; }
        _previewDescription = choice.Provider + " — " + choice.Caption.Replace('\n', ' '); _previewUrl = choice.SourceUrl; _manualEdits = false;
        UpdatePreviewCredit(); UpdateControls();
        _status.Text = _loaded ? "Preview applied to proposed values. Review the checked fields; the file is unchanged until you confirm Save." : "Preview loaded. File tags could not be read, so saving remains disabled. Reload file tags to retry.";
    }

    private void UpdatePreviewCredit()
    {
        _previewCredit.Inlines.Clear();
        _previewCredit.Inlines.Add(new Run(_previewDescription.Length > 0
            ? "Preview source: " + _previewDescription + (_manualEdits ? " · Edited manually. " : ". ")
            : _manualEdits ? "Proposed values edited manually. " : "No online match previewed. You can edit proposed values manually. "));
        if (SourceUri(_previewUrl) is not null)
        {
            var link = new Hyperlink(new Run("Open preview source")); link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
            link.Click += (_, _) => OpenSource(_previewUrl); _previewCredit.Inlines.Add(link);
        }
    }

    private void UpdateControls()
    {
        if (_updatingRows || _closed) return;
        _search.IsEnabled = !_saving && !_lookingUp && !string.IsNullOrWhiteSpace(_title.Text);
        _cancelSearch.IsEnabled = !_saving && _lookingUp;
        _preview.IsEnabled = !_saving && !_reading && _matches.SelectedItem is LookupChoice;
        _source.IsEnabled = !_saving && _matches.SelectedItem is LookupChoice choice && SourceUri(choice.SourceUrl) is not null;
        _reload.IsEnabled = !_saving && !_reading;
        _title.IsEnabled = _artist.IsEnabled = _matches.IsEnabled = !_saving;
        _close.IsEnabled = !_saving;
        foreach (var row in _rows.Values) { row.Apply.IsEnabled = row.Proposed.IsEnabled = !_saving && !_reading; }
        _saveButton.IsEnabled = !_saving && !_reading && _loaded && _canWrite && _rows.Any(pair => pair.Value.Apply.IsChecked == true && !string.Equals(pair.Value.Proposed.Text, _currentValues[pair.Key], StringComparison.Ordinal));
    }

    private async Task SaveAsync()
    {
        if (_saving || _reading || !_loaded || !_canWrite || _closed) return;
        try
        {
            var changes = new List<TagChange>();
            string? SelectedText(string key)
            {
                var row = _rows[key]; var value = row.Proposed.Text;
                if (row.Apply.IsChecked != true || value == _currentValues[key]) return null;
                var limit = key == "Comment" ? 16384 : 1024;
                if (value.Length > limit) throw new ArgumentException($"{row.Label} must be at most {limit:N0} characters.");
                changes.Add(new(row.Label, _currentValues[key], value)); return value;
            }
            uint? SelectedNumber(string key)
            {
                var row = _rows[key];
                if (row.Apply.IsChecked != true) return null;
                if (!uint.TryParse(row.Proposed.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > 9999)
                    throw new ArgumentException($"{row.Label} must be a whole number from 0 to 9999. Use 0 to clear it.");
                var text = value.ToString(CultureInfo.InvariantCulture);
                if (text == _currentValues[key]) return null;
                changes.Add(new(row.Label, _currentValues[key], text)); return value;
            }
            var patch = new MediaTagPatch
            {
                Title = SelectedText("Title"), Artist = SelectedText("Artist"), Album = SelectedText("Album"), AlbumArtist = SelectedText("AlbumArtist"),
                Genre = SelectedText("Genre"), Comment = SelectedText("Comment"), Year = SelectedNumber("Year"),
                TrackNumber = SelectedNumber("TrackNumber"), DiscNumber = SelectedNumber("DiscNumber")
            };
            if (changes.Count == 0) { _status.Text = "Check at least one changed value before saving."; return; }
            if (!ConfirmChanges(changes)) return;
            CancelLookup(false); _saving = true; _status.Text = "Saving checked tags and creating the original-file backup…"; UpdateControls();
            var result = await _save(patch, _lifetime.Token);
            ApplyTags(result.Tags, true); _loaded = true;
            _backup.Text = result.BackupPath; _backup.Visibility = Visibility.Visible;
            _status.Text = $"Saved {changes.Count} checked fields. Original-file backup:";
        }
        catch (OperationCanceledException) { if (!_closed) _status.Text = "Saving was cancelled. Reload file tags to check the current values before trying again."; }
        catch (Exception ex) { if (!_closed) _status.Text = "Tags could not be saved. " + ex.Message; }
        finally { _saving = false; if (!_closed) UpdateControls(); }
    }

    private bool ConfirmChanges(IReadOnlyList<TagChange> changes)
    {
        var review = new Window { Owner = this, Title = "Confirm tag changes", Width = 760, Height = 570, MinWidth = 580, MinHeight = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        Ui.Theme(review);
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel(); header.Children.Add(Ui.Text(Path.GetFileName(_item.Path), 19)); header.Children.Add(Ui.Text(_item.Path, 12, true));
        header.Children.Add(Ui.Text("This changes tags in the original media file. A backup of the original file will be created. If this file is playing, its playback will stop before saving. Unchecked fields are preserved.", 13));
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var cancel = Ui.Button("Cancel", (_, _) => review.Close()); cancel.IsCancel = true;
        var confirm = Ui.Button("Save these changes", (_, _) => review.DialogResult = true); confirm.SetResourceReference(StyleProperty, "AccentButton");
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13, Padding = new Thickness(12), Text = string.Join("\n\n", changes.Select(change => $"{change.Field}\nBefore: {Display(change.Before)}\nAfter:  {Display(change.After)}")) };
        AutomationProperties.SetName(details, "Checked tag changes before and after"); root.Children.Add(details); review.Content = root;
        return review.ShowDialog() == true;
    }

    private static string Display(string value) => value.Length == 0 ? "(empty)" : value;
    private static Uri? SourceUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return null;
        return uri.Host is "musicbrainz.org" or "www.musicbrainz.org" or "themoviedb.org" or "www.themoviedb.org" ? uri : null;
    }
    private void OpenSource(string? value)
    {
        var uri = SourceUri(value); if (uri is null) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { _status.Text = "The source page could not be opened. " + ex.Message; }
    }
}
