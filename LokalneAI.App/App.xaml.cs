using System.Windows;
using System.Windows.Threading;

namespace LokalneAI.App;

public partial class App : Application
{
    private StartupOptions _options = StartupOptions.Parse([]);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _options = StartupOptions.Parse(e.Args);
        DispatcherUnhandledException += OnUnhandledException;

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
            window.Show();
        }
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
