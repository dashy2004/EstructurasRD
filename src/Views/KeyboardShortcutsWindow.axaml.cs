using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LosasPlus.Views;

/// <summary>Diálogo modal read-only con la lista de atajos de teclado de LosasPlus.</summary>
public partial class KeyboardShortcutsWindow : Window
{
    public KeyboardShortcutsWindow() => AvaloniaXamlLoader.Load(this);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
