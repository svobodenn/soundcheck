using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SoundCheck;

public partial class App : Application
{
    // Single-instance guard. Mutex held for app lifetime; the EventWaitHandle
    // lets a second launch poke the first instance to surface its window.
    private const string MutexName = "SoundCheck.SingleInstance.Mutex";
    private const string WakeName  = "SoundCheck.SingleInstance.Wake";
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            // Another instance is already running — wake it and exit quietly.
            try
            {
                if (EventWaitHandle.TryOpenExisting(WakeName, out var wake))
                    wake.Set();
            }
            catch { }
            Shutdown();
            return;
        }
        StartWakeListener();

        // IMPORTANT: set up resources and exception handlers BEFORE base.OnStartup,
        // because that's what creates the StartupUri window — the MainWindow ctor
        // already needs the Accent/AccentDim brushes (e.g. UpdateSortLabel).
        try { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!); } catch { }
        Resources["Accent"]    = new SolidColorBrush(Color.FromRgb(0xC8, 0xA9, 0x6E));
        Resources["AccentDim"] = new SolidColorBrush(Color.FromRgb(0x6B, 0x5A, 0x38));

        // Catch any unhandled UI exception and log to file + show message
        DispatcherUnhandledException += (s, ex) =>
        {
            SoundCheck.Services.Log.Error("Unhandled UI exception", ex.Exception);
            try { File.AppendAllText(LogPath, $"[UI] {DateTime.Now:O}\n{ex.Exception}\n\n"); } catch { }
            MessageBox.Show($"Error: {ex.Exception.Message}\n\nLog: {LogPath}", "SoundCheck — exception", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true; // keep app alive
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            SoundCheck.Services.Log.Error("Unhandled domain exception: " + (ex.ExceptionObject as Exception)?.Message);
            try { File.AppendAllText(LogPath, $"[Domain] {DateTime.Now:O}\n{ex.ExceptionObject}\n\n"); } catch { }
        };

        SoundCheck.Services.Log.Info("App started");
        base.OnStartup(e);
    }

    /// <summary>Background listener: when a second launch signals the wake handle,
    /// surface the existing main window on the UI thread.</summary>
    private void StartWakeListener()
    {
        var wake = new EventWaitHandle(false, EventResetMode.AutoReset, WakeName);
        var t = new Thread(() =>
        {
            while (true)
            {
                wake.WaitOne();
                Dispatcher.Invoke(() => (Current?.MainWindow as MainWindow)?.SurfaceFromAnotherInstance());
            }
        })
        { IsBackground = true, Name = "SoundCheck.WakeListener" };
        t.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SoundCheck", "crash.log");
}
