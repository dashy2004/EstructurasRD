using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LosasPlus.Services;
using Microsoft.Win32;

namespace LosasPlus.Views;

public partial class ReglamentoView : UserControl
{
    public ReglamentoView()
    {
        InitializeComponent();
        Loaded += (_, __) =>
        {
            // Inicializar el campo de carpeta con la preferencia guardada o el default resuelto.
            var carpeta = ReglamentoService.LoadCarpetaPreference();
            if (string.IsNullOrEmpty(carpeta))
                carpeta = ReglamentoService.ResolveCarpetaPdfs();
            CarpetaBox.Text = carpeta;
        };
    }

    private void OnBrowseCarpeta(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Seleccioná cualquier PDF dentro de la carpeta de reglamentos",
            Filter = "PDF (*.pdf)|*.pdf|Todos|*.*",
            CheckFileExists = true,
        };
        // Si ya hay path, abrir ese directorio
        if (!string.IsNullOrEmpty(CarpetaBox.Text) && Directory.Exists(CarpetaBox.Text))
            dlg.InitialDirectory = CarpetaBox.Text;
        if (dlg.ShowDialog() == true)
            CarpetaBox.Text = Path.GetDirectoryName(dlg.FileName) ?? "";
    }

    private void OnSaveCarpeta(object sender, RoutedEventArgs e)
    {
        ReglamentoService.SaveCarpetaPreference(CarpetaBox.Text.Trim());
        MessageBox.Show("Carpeta de reglamentos guardada.\n\nPersistida en %AppData%\\LosasPlus\\reglamentos_path.txt",
            "Reglamentos", MessageBoxButton.OK, MessageBoxImage.Information);
        // Refrescar el detalle para que el botón "Abrir PDF" se habilite/deshabilite con el path nuevo
        OnSelectionChanged(NormasList, null!);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DetalleHost.Children.Clear();
        if (NormasList.SelectedItem is not Norma n) return;

        // Encabezado
        DetalleHost.Children.Add(new TextBlock
        {
            Text = $"{n.Codigo} — {n.Titulo}",
            FontWeight = FontWeights.Bold,
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
        });
        DetalleHost.Children.Add(new TextBlock
        {
            Text = $"Edición: {n.Edicion}    ·    Ámbito: {n.Ambito}",
            Foreground = (Brush)Application.Current.Resources["FgMuted"],
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        // Acciones: Abrir PDF + URL externa
        var acciones = new System.Windows.Controls.WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var pdfPath = ReglamentoService.PdfPathFor(n);
        if (pdfPath != null)
        {
            var btn = new Button
            {
                Content = "📄 Abrir PDF",
                Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)Application.Current.Resources["PrimaryButton"],
                ToolTip = pdfPath,
            };
            btn.Click += (_, __) => OpenWithShell(pdfPath);
            acciones.Children.Add(btn);
        }
        else if (!string.IsNullOrWhiteSpace(n.Archivo))
        {
            // PDF declarado pero no encontrado: mostrar pista
            var hint = new TextBlock
            {
                Text = $"⚠ No se encontró {n.Archivo} en la carpeta configurada — corregí la ruta arriba.",
                Foreground = (Brush)Application.Current.Resources["Warn"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            };
            acciones.Children.Add(hint);
        }
        if (!string.IsNullOrEmpty(n.Url))
        {
            var btn = new Button
            {
                Content = "🌐 Sitio oficial",
                ToolTip = n.Url,
            };
            btn.Click += (_, __) => OpenWithShell(n.Url);
            acciones.Children.Add(btn);
        }
        if (acciones.Children.Count > 0) DetalleHost.Children.Add(acciones);

        // Resumen
        DetalleHost.Children.Add(Sep());
        DetalleHost.Children.Add(new TextBlock { Text = "Resumen", FontWeight = FontWeights.Bold, FontSize = 13 });
        DetalleHost.Children.Add(new TextBlock { Text = n.Resumen, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });

        // Secciones relevantes
        if (n.Secciones != null && n.Secciones.Count > 0)
        {
            DetalleHost.Children.Add(Sep());
            DetalleHost.Children.Add(new TextBlock { Text = "Secciones relevantes", FontWeight = FontWeights.Bold, FontSize = 13 });
            foreach (var sec in n.Secciones)
                DetalleHost.Children.Add(new TextBlock
                {
                    Text = "• " + sec,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                });
        }

        // Aplicación a losas
        if (!string.IsNullOrWhiteSpace(n.AplicacionLosas))
        {
            DetalleHost.Children.Add(Sep());
            DetalleHost.Children.Add(new TextBlock
            {
                Text = "Aplicación al cálculo de losas",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["Accent"],
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
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir:\n{path}\n\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static Separator Sep() => new Separator { Margin = new Thickness(0, 12, 0, 12) };
}
