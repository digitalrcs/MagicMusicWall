using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

internal sealed class AiSongNotesWindow : Window
{
    public AiSongNotesWindow(MediaItem item, Func<AppPreferences> settings, Action<Window> webLookup)
    {
        Ui.Theme(this);
        Title = "AI song notes"; Width = 760; Height = 650; MinWidth = 520; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new(22) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = item.Title + " — " + item.Artist, FontSize = 22, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text = "AI notes are interpretations, not verified facts. Generate sends saved song metadata to your configured provider. Web lookup opens MusicBrainz for reviewed, sourced information. Original file tags are unchanged.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 10) });
        var buttons = new WrapPanel(); header.Children.Add(buttons);
        var generate = new Button { Content = "Generate & save notes", Margin = new(0, 0, 8, 8) };
        var web = new Button { Content = "Web lookup (MusicBrainz)", Margin = new(0, 0, 8, 8) };
        var cancelButton = new Button { Content = "Cancel", Margin = new(0, 0, 8, 8) };
        buttons.Children.Add(generate); buttons.Children.Add(web); buttons.Children.Add(cancelButton);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) }; header.Children.Add(status);
        var reader = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(reader); Content = Ui.ScrollPage(root, header, null, 340);
        CancellationTokenSource? pending = null;
        bool closed = false;
        void Display(AiSongNote note)
        {
            reader.Text = note.Text;
            status.Text = $"Saved {note.CreatedUtc.LocalDateTime:g} · {note.Provider} · {note.Model}" +
                (note.Title != item.Title || note.Artist != item.Artist ? " · Song tags changed: regenerate these notes." : "");
        }
        try { if (AiSongNotes.Load(App.DataDirectory, item.Id) is { } saved) Display(saved); }
        catch { status.Text = "Saved notes could not be read. Generate new notes to replace them."; }
        web.Click += (_, _) => webLookup(this);
        cancelButton.Click += (_, _) => pending?.Cancel();
        Closed += (_, _) => { closed = true; pending?.Cancel(); };
        generate.Click += async (_, _) =>
        {
            if (pending is not null) return;
            using var cancel = new CancellationTokenSource(); pending = cancel;
            generate.IsEnabled = web.IsEnabled = false;
            try
            {
                var options = AiCredentials.Options(settings());
                status.Text = $"Asking {options.Provider}…";
                var song = AiPlaylistService.Inventory([item], new Dictionary<string, AudioFeatures>()).Single();
                using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
                var text = await new AiPlaylistService(http).DescribeAsync(options, song, cancel.Token);
                cancel.Token.ThrowIfCancellationRequested();
                if (closed) return;
                if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("The model returned no notes. Existing notes were kept.");
                var note = new AiSongNote(song.Id, song.Title, song.Artist, options.Provider, options.Model, DateTimeOffset.UtcNow, text);
                AiSongNotes.Save(App.DataDirectory, note); Display(note);
            }
            catch (OperationCanceledException) { status.Text = "Cancelled. Existing notes were kept."; }
            catch (Exception ex) { status.Text = ex is HttpRequestException ? "Could not reach the AI provider. Check Setup > AI." : ex is System.Text.Json.JsonException ? "The provider returned an invalid response." : ex.Message; }
            finally { pending = null; generate.IsEnabled = web.IsEnabled = true; }
        };
    }
}
