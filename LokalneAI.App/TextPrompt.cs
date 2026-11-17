using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LokalneAI.App;

/// <summary>Małe okno z jednym polem tekstowym (np. nowa nazwa rozmowy albo grupy).</summary>
internal static class TextPrompt
{
    /// <summary>Pyta o tekst; null = Anuluj albo pusty tekst.</summary>
    public static string? Ask(Window? owner, string title, string label, string initial)
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 6, 0, 12), Padding = new Thickness(4, 3, 4, 3) };
        var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Anuluj", IsCancel = true, Width = 80 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var window = new Window
        {
            Title = title,
            Content = panel,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13,
        };
        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) =>
        {
            box.SelectAll();
            Keyboard.Focus(box);
        };
        return window.ShowDialog() == true && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
    }
}
