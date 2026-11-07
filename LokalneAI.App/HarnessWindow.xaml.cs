using System.Windows;

namespace LokalneAI.App;

/// <summary>Zakładka Code w osobnym oknie (przycisk „W osobnym oknie”): własna rozmowa, te same ustawienia.</summary>
public partial class HarnessWindow : Window
{
    public HarnessWindow(Func<HarnessContext> contextProvider)
    {
        InitializeComponent();
        View.Attach(contextProvider, allowSeparateWindow: false);
        Closed += (_, _) =>
        {
            View.SaveChoices();
            View.Shutdown();
        };
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        Root.Margin = new Thickness(maximized ? 7 : 0);
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
