using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using MagicMusicWall.App;
using MagicMusicWall.Core;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Every path belongs to this uniquely created fixture. No installed data is loaded.
        var directory = Path.Combine(Path.GetTempPath(), "MagicMusicWall-SessionChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var catalog = new LibraryCatalog(directory);
        foreach (var id in new[] { "a", "b", "c" })
        {
            var path = Path.Combine(directory, id + ".wav");
            WriteSilentWave(path);
            catalog.Document.Items.Add(new MediaItem { Id = id, Path = path, Title = "Synthetic " + id, DurationSeconds = 60 });
        }
        catalog.Save();
        new AppPreferences { Volume = 0, RestoreSession = true, ResumePlayback = false }.Save(directory);
        new ListeningSessionStore(directory).Save(new() { ItemIds = ["a", "b", "a"], CurrentIndex = 2, PositionMilliseconds = 9000, WasPlaying = true });
        typeof(App).GetProperty(nameof(App.DataDirectory))!.SetValue(null, directory);
        var app = new App();
        app.InitializeComponent();
        Exception? failure = null;
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(async () =>
        {
            MainWindow? window = null;
            try
            {
                await Wait(() => app.MainWindow is MainWindow, "main window");
                window = (MainWindow)app.MainWindow;
                await Wait(() => Field<bool>(window, "_sessionInitialized"), "session restore");
                var queue = Field<ObservableCollection<MediaItem>>(window, "_queue");
                var player = Field<MediaPlayer>(window, "_player");
                Check(queue.Select(x => x.Id).SequenceEqual(["a", "b", "a"]), "restore preserves duplicate order");
                Check(Field<int>(window, "_queueIndex") == 2 && Field<long?>(window, "_pendingSessionPosition") == 9000, "restore keeps the second occurrence and position");
                Check(!player.IsPlaying && player.Media is null, "default restore does not load or emit audio");
                Call(window, "ShowMiniPlayer");
                var mini = Field<Window>(window, "_miniPlayer");
                window.WindowState = WindowState.Minimized;
                await Task.Delay(200);
                var miniHandle = new WindowInteropHelper(mini).Handle;
                Check(IsWindowVisible(miniHandle) && !IsIconic(miniHandle), "mini player stays visible when main window is minimized");
                window.WindowState = WindowState.Normal;
                mini.WindowState = WindowState.Minimized;
                Call(window, "ShowMiniPlayer");
                await Task.Delay(200);
                Check(ReferenceEquals(mini, Field<Window>(window, "_miniPlayer")) && !IsIconic(miniHandle), "reopening mini restores the existing minimized window");
                mini.Close();
                Check(window.IsVisible && Field<Window?>(window, "_miniPlayer") is null, "closing mini leaves main player open");
                Call(window, "ShowMiniPlayer");
                var finalMini = Field<Window>(window, "_miniPlayer");
                var miniClosed = false;
                finalMini.Closed += (_, _) => miniClosed = true;
                window.Closed += (_, _) => Check(miniClosed, "closing main also closes independent mini player");
                var queueList = (ListBox)window.FindName("QueueList");
                queueList.UpdateLayout();
                Check(((int[])Call(window, "SelectedQueueIndices")!).SequenceEqual([2]), "WPF selection identifies second duplicate occurrence");
                queueList.UnselectAll();
                ((ListBoxItem)queueList.ItemContainerGenerator.ContainerFromIndex(0)).IsSelected = true;
                ((ListBoxItem)queueList.ItemContainerGenerator.ContainerFromIndex(2)).IsSelected = true;
                Check(((int[])Call(window, "SelectedQueueIndices")!).SequenceEqual([0, 2]), "WPF multiple selection identifies duplicate occurrences independently");
                queueList.SelectedIndex = 2;
                var bridgeType = typeof(MainWindow).GetNestedType("RemoteBridge", BindingFlags.NonPublic)!;
                var bridge = (MagicMusicWall.Streaming.IRemoteControl)Activator.CreateInstance(bridgeType, window)!;
                var restoredState = await bridge.GetStateAsync(CancellationToken.None);
                Check(restoredState.PositionMs == 9000 && restoredState.DurationMs == 60000, "remote reports restored paused position and duration");
                await bridge.ExecuteAsync(new MagicMusicWall.Streaming.RemoteCommand("seek", PositionMs: 12000), CancellationToken.None);
                Check(Field<long?>(window, "_pendingSessionPosition") == 12000 && player.Media is null, "remote seeks restored position without starting audio");
                await bridge.ExecuteAsync(new MagicMusicWall.Streaming.RemoteCommand("seek", PositionMs: 9000), CancellationToken.None);
                var images = Path.GetFullPath("../../../../../docs/images", AppContext.BaseDirectory);
                if (Environment.GetEnvironmentVariable("MMW_CAPTURE_SCREENSHOTS") == "1")
                {
                    window.WindowState = WindowState.Normal;
                    window.Width = 1420; window.Height = 900; await Task.Delay(200);
                    Capture(window, Path.Combine(images, "library.png"));
                    window.Width = 1120; window.Height = 720; await Task.Delay(200);
                    Capture(window, Path.Combine(images, "library-compact.png"));
                    window.Width = 1420; window.Height = 900;
                    var setup = (Window)Activator.CreateInstance(typeof(MainWindow).Assembly.GetType("MagicMusicWall.App.SetupWindow")!, BindingFlags.Instance | BindingFlags.NonPublic, null, [window], null)!;
                    setup.Show(); await Task.Delay(200); Capture(setup, Path.Combine(images, "setup-sound.png")); setup.Close();
                    var folders = (Window)Activator.CreateInstance(typeof(MainWindow).Assembly.GetType("MagicMusicWall.App.FolderWindow")!,
                        [Field<LibraryCatalog>(window, "_catalog"), (Func<bool>)(() => true), (Func<Task>)(() => Task.CompletedTask),
                            (Func<Window, Task>)(_ => Task.CompletedTask), (Func<Window, Task>)(_ => Task.CompletedTask), (Action)(() => { }), (Func<string>)(() => "")])!;
                    folders.Owner = window; folders.Show(); await Task.Delay(200); Capture(folders, Path.Combine(images, "manage-folders.png")); folders.Close();
                }

                await Invoke(window, "PlayIndex", 2, null, false, true);
                await Wait(() => player.IsPlaying && player.Time >= 8500, "restored seek");
                Check(player.Volume == 0, "fixture playback stays silent");
                Call(window, "MoveQueueItem", 0, 2);
                Check(Field<int>(window, "_queueIndex") == 1, "moving a duplicate across current preserves current occurrence");
                var version = Field<int>(window, "_playVersion");
                await Invoke(window, "UndoQueueAsync");
                Check(Field<int>(window, "_queueIndex") == 2 && Field<int>(window, "_playVersion") == version, "undo reorder does not restart playback");
                await Invoke(window, "ClearQueueAsync");
                Check(queue.Count == 0 && !player.IsPlaying, "clear queue stops and empties queue");
                await Invoke(window, "UndoQueueAsync");
                Check(queue.Select(x => x.Id).SequenceEqual(["a", "b", "a"]) && Field<int>(window, "_queueIndex") == 2, "undo clear restores exact duplicate occurrence");
                Check(!player.IsPlaying && Field<long?>(window, "_pendingSessionPosition") >= 8500, "undo clear restores paused position");
                await Invoke(window, "RemoveQueueItem", 2, "a");
                Check(queue.Select(x => x.Id).SequenceEqual(["a", "b"]) && !player.IsPlaying, "removing current paused occurrence does not start audio");
                await Invoke(window, "UndoQueueAsync");
                Check(queue.Select(x => x.Id).SequenceEqual(["a", "b", "a"]) && Field<int>(window, "_queueIndex") == 2, "undo removal restores selected occurrence");

                var items = Field<LibraryCatalog>(window, "_catalog").Document.Items;
                Call(window, "QueueNextItems", new object[] { new[] { items.Single(x => x.Id == "c") } });
                Check(queue.Select(x => x.Id).SequenceEqual(["a", "b", "a", "c"]), "play next inserts after current without replacing queue");
                await Invoke(window, "UndoQueueAsync");
                await Invoke(window, "OpenFilesAsync", new object[] { new[] { Path.Combine(directory, "b.wav"), Path.Combine(directory, "a.wav") } });
                Check(queue.Select(x => x.Id).SequenceEqual(["a", "b", "a", "b", "a"]), "external activation appends duplicate songs to restored queue");
                await Invoke(window, "RemoveQueueDuplicates");
                Check(queue.Select(x => x.Id).SequenceEqual(["b", "a"]) && Field<int>(window, "_queueIndex") == 1, "explicit duplicate removal keeps current occurrence");
                await Invoke(window, "SaveListeningSessionAsync");
                var saved = new ListeningSessionStore(directory).Load()!;
                Check(saved.ItemIds.SequenceEqual(["b", "a"]) && saved.CurrentIndex == 1, "checkpoint captures edited occurrence index");
            }
            catch (Exception ex) { failure = ex; Console.Error.WriteLine(ex); }
            finally
            {
                if (window is not null) window.Close(); else app.Shutdown(1);
            }
        });
        app.Run();
        var afterClose = new ListeningSessionStore(directory).Load();
        if (failure is null) Check(afterClose?.ItemIds.SequenceEqual(["b", "a"]) == true, "graceful close preserves final session");
        Console.WriteLine($"Fixture: {directory}");
        return failure is null ? 0 : 1;
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static Task Invoke(object target, string name, params object?[] args) => (Task)Call(target, name, args)!;
    private static async Task Wait(Func<bool> condition, string label)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition()) { if (DateTime.UtcNow > deadline) throw new Exception("Timed out: " + label); await Task.Delay(100); }
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right),
            (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
        Console.WriteLine("Screenshot: " + path);
    }
    private static void WriteSilentWave(string path)
    {
        const int rate = 8000, samples = rate * 60;
        using var stream = new BinaryWriter(File.Create(path));
        stream.Write("RIFF"u8); stream.Write(36 + samples * 2); stream.Write("WAVEfmt "u8);
        stream.Write(16); stream.Write((short)1); stream.Write((short)1); stream.Write(rate);
        stream.Write(rate * 2); stream.Write((short)2); stream.Write((short)16);
        stream.Write("data"u8); stream.Write(samples * 2); stream.Write(new byte[samples * 2]);
    }
}
