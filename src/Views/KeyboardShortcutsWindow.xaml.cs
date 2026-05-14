using System.Windows;

namespace LosasPlus.Views;

/// <summary>
/// Diálogo modal con la lista de atajos de teclado de LosasPlus. Read-only:
/// no permite reasignar (eso sería commit 34 con la pestaña Configuración).
/// </summary>
public partial class KeyboardShortcutsWindow : Window
{
    public KeyboardShortcutsWindow() => InitializeComponent();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
