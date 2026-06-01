using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MemoriaPlus.Services;

/// <summary>
/// Implementación de <see cref="IMessageBoxService"/> con una ventana Avalonia
/// construida en código (sin axaml, para no depender de recursos de vistas).
/// Reemplaza System.Windows.MessageBox. Devuelve la respuesta vía
/// <c>ShowDialog&lt;bool&gt;</c> sobre el TopLevel activo.
/// </summary>
public sealed class AvaloniaMessageBoxService : IMessageBoxService
{
    public Task<bool> ConfirmYesNoAsync(string title, string message)
        => ShowAsync(title, message, yesNo: true);

    public Task InfoAsync(string title, string message)
        => ShowAsync(title, message, yesNo: false);

    private static async Task<bool> ShowAsync(string title, string message, bool yesNo)
    {
        var owner = AppServices.TopLevel as Window;

        var result = false;

        var msg = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        var window = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children = { msg, buttons },
            },
        };

        if (yesNo)
        {
            var yes = new Button { Content = "Sí", IsDefault = true, MinWidth = 80 };
            var no = new Button { Content = "No", IsCancel = true, MinWidth = 80 };
            yes.Click += (_, _) => { result = true; window.Close(); };
            no.Click += (_, _) => { result = false; window.Close(); };
            buttons.Children.Add(no);
            buttons.Children.Add(yes);
        }
        else
        {
            var ok = new Button { Content = "Aceptar", IsDefault = true, IsCancel = true, MinWidth = 80 };
            ok.Click += (_, _) => { result = true; window.Close(); };
            buttons.Children.Add(ok);
        }

        if (owner is not null)
            await window.ShowDialog(owner);
        else
            window.Show();

        return result;
    }
}
