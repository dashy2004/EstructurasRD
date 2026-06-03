using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using LosasPlus.Services;
using Shared.UI.Services;

namespace LosasPlus.Views;

/// <summary>
/// Vista de reglamentos: lista de normas + detalle construido en código. Port a
/// Avalonia: OpenFileDialog→AppServices; Brush→IBrush+FindResource; Style→Theme
/// (ControlTheme); MessageBox→AppServices; ToolTip.SetTip; Process→AppServices.Launcher.
/// </summary>
public partial class ReglamentoView : UserControl
{
    public ReglamentoView()
    {
        InitializeComponent();
        Loaded += (_, __) =>
        {
            var carpeta = ReglamentoService.LoadCarpetaPreference();
            if (string.IsNullOrEmpty(carpeta))
                carpeta = ReglamentoService.ResolveCarpetaPdfs();
            CarpetaBox.Text = carpeta;
        };
    }

    private async void OnBrowseCarpeta(object? sender, RoutedEventArgs e)
    {
        var ruta = await AppServices.Dialogs.OpenFileAsync(
            "Seleccioná cualquier PDF dentro de la carpeta de reglamentos",
            new FileFilter("PDF", new[] { "*.pdf" }),
            new FileFilter("Todos", new[] { "*.*" }));
        if (ruta != null)
            CarpetaBox.Text = Path.GetDirectoryName(ruta) ?? "";
    }

    private async void OnSaveCarpeta(object? sender, RoutedEventArgs e)
    {
        ReglamentoService.SaveCarpetaPreference((CarpetaBox.Text ?? "").Trim());
        await AppServices.MessageBox.InfoAsync("Reglamentos",
            "Carpeta de reglamentos guardada.\n\nPersistida en %AppData%\\LosasPlus\\reglamentos_path.txt");
        OnSelectionChanged(NormasList, null!);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        DetalleHost.Children.Clear();
        if (NormasList.SelectedItem is not Norma n) return;

        DetalleHost.Children.Add(new TextBlock
        {
            Text = $"{n.Codigo} — {n.Titulo}",
            FontWeight = FontWeight.Bold,
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
        });
        DetalleHost.Children.Add(new TextBlock
        {
            Text = $"Edición: {n.Edicion}    ·    Ámbito: {n.Ambito}",
            Foreground = Brush("FgMuted"),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        var acciones = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var pdfPath = ReglamentoService.PdfPathFor(n);
        if (pdfPath != null)
        {
            var btn = new Button
            {
                Content = "📄 Abrir PDF",
                Margin = new Thickness(0, 0, 8, 0),
                Theme = Application.Current?.FindResource("PrimaryButton") as ControlTheme,
            };
            ToolTip.SetTip(btn, pdfPath);
            btn.Click += (_, __) => OpenWithShell(pdfPath);
            acciones.Children.Add(btn);
        }
        else if (!string.IsNullOrWhiteSpace(n.Archivo))
        {
            acciones.Children.Add(new TextBlock
            {
                Text = $"⚠ No se encontró {n.Archivo} en la carpeta configurada — corregí la ruta arriba.",
                Foreground = Brush("Warn"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }
        if (!string.IsNullOrEmpty(n.Url))
        {
            var btn = new Button { Content = "🌐 Sitio oficial" };
            ToolTip.SetTip(btn, n.Url);
            btn.Click += (_, __) => OpenWithShell(n.Url);
            acciones.Children.Add(btn);
        }
        if (acciones.Children.Count > 0) DetalleHost.Children.Add(acciones);

        DetalleHost.Children.Add(Sep());
        DetalleHost.Children.Add(new TextBlock { Text = "Resumen", FontWeight = FontWeight.Bold, FontSize = 13 });
        DetalleHost.Children.Add(new TextBlock { Text = n.Resumen, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });

        if (n.Secciones != null && n.Secciones.Count > 0)
        {
            DetalleHost.Children.Add(Sep());
            DetalleHost.Children.Add(new TextBlock { Text = "Secciones relevantes", FontWeight = FontWeight.Bold, FontSize = 13 });
            foreach (var sec in n.Secciones)
                DetalleHost.Children.Add(new TextBlock
                {
                    Text = "• " + sec,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                });
        }

        if (!string.IsNullOrWhiteSpace(n.AplicacionLosas))
        {
            DetalleHost.Children.Add(Sep());
            DetalleHost.Children.Add(new TextBlock
            {
                Text = "Aplicación al cálculo de losas",
                FontWeight = FontWeight.Bold,
                FontSize = 13,
                Foreground = Brush("Accent"),
            });
            DetalleHost.Children.Add(new TextBlock
            {
                Text = n.AplicacionLosas,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }
    }

    private static void OpenWithShell(string path)
    {
        try { AppServices.Launcher.OpenInDefaultApp(path); }
        catch (Exception ex)
        {
            _ = AppServices.MessageBox.InfoAsync("Error", $"No se pudo abrir:\n{path}\n\n{ex.Message}");
        }
    }

    private static IBrush? Brush(string key) => Application.Current?.FindResource(key) as IBrush;

    private static Separator Sep() => new Separator { Margin = new Thickness(0, 12, 0, 12) };
}
