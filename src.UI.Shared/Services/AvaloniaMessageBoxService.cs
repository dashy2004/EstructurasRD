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

    /// <summary>
    /// Diálogo de 3 botones «Guardar» / «Descartar» / «Cancelar». CRÍTICO
    /// (Fase A — pérdida de datos): el resultado arranca en
    /// <see cref="ResultadoDescarte.Cancelar"/> y SÓLO los clicks explícitos en
    /// «Guardar»/«Descartar» lo cambian. Descartar el diálogo de cualquier forma
    /// (Escape → botón <c>IsCancel</c>; botón X de la ventana → ningún Click) deja
    /// el default <c>Cancelar</c> = quedarse, jamás descarta el trabajo en silencio.
    /// </summary>
    public async Task<ResultadoDescarte> ConfirmarGuardarDescartarCancelarAsync(string titulo, string mensaje)
    {
        var owner = AppServices.TopLevel as Window;

        // Default SEGURO: cualquier forma de cerrar el diálogo sin elegir un botón
        // explícito (Escape / X de la ventana) deja este valor = quedarse.
        var result = ResultadoDescarte.Cancelar;

        var msg = new TextBlock
        {
            Text = mensaje,
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
            Title = titulo,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children = { msg, buttons },
            },
        };

        var guardar = new Button { Content = "Guardar", IsDefault = true, MinWidth = 80 };
        var descartar = new Button { Content = "Descartar", MinWidth = 80 };
        // Cancelar es IsCancel: Escape lo dispara, y deja el default Cancelar.
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 80 };

        guardar.Click   += (_, _) => { result = ResultadoDescarte.Guardar;   window.Close(); };
        descartar.Click += (_, _) => { result = ResultadoDescarte.Descartar; window.Close(); };
        cancelar.Click  += (_, _) => { result = ResultadoDescarte.Cancelar;  window.Close(); };

        buttons.Children.Add(cancelar);
        buttons.Children.Add(descartar);
        buttons.Children.Add(guardar);

        if (owner is not null)
            await window.ShowDialog(owner);
        else
            window.Show();

        return result;
    }

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
