using System.IO;
using System.Windows;
using System.Security.Cryptography;
using System.Text;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private readonly CancellationTokenSource _activationStop = new();
    public static string DataDirectory { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MagicMusicWall");
    public static string[] Arguments { get; private set; } = [];
    public static bool IsPortable { get; private set; }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Arguments = e.Args;
        var index = Array.IndexOf(e.Args, "--data-dir");
        if (index >= 0 && index + 1 < e.Args.Length) DataDirectory = Path.GetFullPath(e.Args[index + 1]);
        else if (e.Args.Contains("--portable"))
        {
            IsPortable = true;
            DataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        }
        try { Directory.CreateDirectory(DataDirectory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"The app data folder could not be opened. Use a writable folder or USB drive.\n\n{ex.Message}", "Magic Music Wall");
            Shutdown(1); return;
        }
        string[] files;
        try { files = FileActivation.ParsePaths(e.Args, Environment.CurrentDirectory); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { MessageBox.Show(ex.Message, "Magic Music Wall"); Shutdown(1); return; }
        var mutexName = "Local\\MagicMusicWall-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DataDirectory.ToUpperInvariant())))[..24];
        _instanceMutex = new Mutex(true, mutexName, out var firstInstance);
        if (!firstInstance)
        {
            _instanceMutex.Dispose(); _instanceMutex = null;
            try { await FileActivationPipe.SendAsync(mutexName[6..], files); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
            { MessageBox.Show($"The running Magic Music Wall could not be reached. Please try opening the file again.\n\n{ex.Message}", "Magic Music Wall"); }
            Shutdown(); return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            try { File.AppendAllText(Path.Combine(DataDirectory, "errors.log"), $"{DateTimeOffset.Now:u} {args.Exception}\n"); } catch { }
            MessageBox.Show(args.Exception.Message, "Magic Music Wall", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        if (e.Args.Contains("--smoke-test")) { _ = SmokeTest.RunAsync(); return; }
        try
        {
            var window = new MainWindow(files);
            MainWindow = window;
            window.Show();
            _ = FileActivationPipe.ListenAsync(mutexName[6..], paths =>
                Dispatcher.BeginInvoke(async () => await window.OpenFilesAsync(paths)), _activationStop.Token);
        }
        catch (Exception ex) { File.AppendAllText(Path.Combine(DataDirectory, "errors.log"), ex + "\n"); MessageBox.Show(ex.Message, "Magic Music Wall could not start"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { _activationStop.Cancel(); _instanceMutex?.ReleaseMutex(); _instanceMutex?.Dispose(); base.OnExit(e); }
}
