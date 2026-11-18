using System.Windows;
using System.Windows.Threading;

namespace LokalneAI.App;

public partial class App : Application
{
    private const string InstanceName = "Amadeusz.LokalneAI.JednaKopia";
    private StartupOptions _options = StartupOptions.Parse([]);
    private Mutex? _single;
    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _options = StartupOptions.Parse(e.Args);
        DispatcherUnhandledException += OnUnhandledException;

        if (!_options.IsAutoTest && !BecomeSingleInstance())
        {
            Shutdown(0); // Amadeusz już działa (np. w zasobniku) — pokazał swoje okno
            return;
        }

        var viewModel = new MainViewModel(_options);
        var window = new MainWindow(viewModel);
        MainWindow = window;

        if (_options.IsAutoTest)
        {
            // Kontrola bez klikania: okno poza ekranem, bez przejmowania fokusu użytkownika.
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -5000;
            window.Top = 0;
            window.Show();
            _ = AutoTest.RunAsync(window, viewModel, _options);
        }
        else
        {
            window.EnableTray();
            window.Show();
            ListenForShowRequests(window);
        }
    }

    /// <summary>
    /// Jedna kopia programu na użytkownika: drugie uruchomienie (skrót, Amadeusz.exe) prosi działającą kopię o pokazanie okna
    /// i kończy się. Zwraca false, gdy to jest ta druga kopia.
    /// </summary>
    private bool BecomeSingleInstance()
    {
        _single = new Mutex(true, InstanceName, out var created);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Pokaz");
        if (created)
        {
            return true;
        }

        _showSignal.Set();
        return false;
    }

    private void ListenForShowRequests(MainWindow window)
    {
        var signal = _showSignal;
        if (signal is null)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            while (true)
            {
                signal.WaitOne();
                Dispatcher.BeginInvoke(window.ShowFromTray);
            }
        }) { IsBackground = true, Name = "Amadeusz: pokaż okno" };
        thread.Start();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LokalneAI", "bledy.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"{DateTimeOffset.Now:O} {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            // Zapis dziennika błędów się nie udał — zostaje komunikat w oknie.
        }

        e.Handled = true;
        if (_options.IsAutoTest)
        {
            Shutdown(3);
            return;
        }

        MessageBox.Show(
            $"Nieoczekiwany błąd: {e.Exception.Message}\n\nSzczegóły zapisano w {logPath}",
            "LokalneAI",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
