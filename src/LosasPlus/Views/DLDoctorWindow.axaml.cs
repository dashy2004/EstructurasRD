using System;
using System.Text;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LosasPlus.Services;
using Shared.UI.Services;

namespace LosasPlus.Views;

/// <summary>
/// Ventana modal del Doctor de archivos .DL. Port a Avalonia: Visibility→IsVisible,
/// MessageBox→AppServices, Clipboard→AppServices, DialogResult→Close(bool).
/// </summary>
public partial class DLDoctorWindow : Window
{
    private readonly DLDiagnostico _diag;

    public Action<string>? OnAbrirDL { get; set; }
    public Action<string>? OnAbrirComoProyecto { get; set; }

    // Constructor sin parámetros para el cargador XAML.
    public DLDoctorWindow() : this(new DLDiagnostico()) { }

    public DLDoctorWindow(DLDiagnostico diag)
    {
        InitializeComponent();
        _diag = diag ?? throw new ArgumentNullException(nameof(diag));
        Render();
    }

    private void Render()
    {
        PathLabel.Text = _diag.ArchivoOriginal;
        IssuesGrid.ItemsSource = _diag.Issues
            .Select(i => new IssueRow(i.Severity.ToString(), i.Codigo, i.Descripcion))
            .ToList();

        var (info, warn, err) = _diag.ConteoPorSeveridad;

        if (_diag.EstaLimpio)
        {
            HeaderTitle.Text = "✓ Archivo .DL OK";
            ResumenLabel.Text = "El doctor no encontró ningún problema. El archivo se puede abrir " +
                                "directamente desde File → 'Abrir .DL legacy…'.";
            AccionHint.Text = "";
            AccionPrincipalBtn.IsVisible = false;
            return;
        }

        var partes = new System.Collections.Generic.List<string>();
        if (err  > 0) partes.Add($"{err} error(es)");
        if (warn > 0) partes.Add($"{warn} aviso(s)");
        if (info > 0) partes.Add($"{info} info(s)");
        ResumenLabel.Text = "Hallazgos: " + string.Join(" · ", partes);

        if (_diag.EsArchivoJsonDisfrazado)
        {
            HeaderTitle.Text = "⚠ El archivo es JSON, no .DL legacy";
            AccionPrincipalBtn.Content = "Abrir como proyecto (.lpx.json)";
            AccionPrincipalBtn.IsVisible = true;
            AccionHint.Text = "El archivo contiene un proyecto JSON de MemoriaPlus guardado con " +
                              "extensión .DL. La acción lo abrirá usando el opener de proyecto.";
        }
        else if (_diag.ContenidoReparado != null)
        {
            HeaderTitle.Text = "⚠ Archivo con problemas reparables";
            AccionPrincipalBtn.Content = "Aplicar reparación y abrir";
            AccionPrincipalBtn.IsVisible = true;
            AccionHint.Text = $"Se generará una copia reparada en {_diag.PathSugeridoReparado} " +
                              "(el original no se modifica).";
        }
        else if (_diag.TieneErrores)
        {
            HeaderTitle.Text = "✗ Archivo con errores no auto-reparables";
            AccionPrincipalBtn.IsVisible = false;
            AccionHint.Text = "Revisá los errores en la tabla. Algunos requieren edición manual " +
                              "del .DL en un editor de texto plano.";
        }
        else
        {
            HeaderTitle.Text = "⚠ Archivo con avisos (abre OK)";
            AccionPrincipalBtn.IsVisible = false;
            AccionHint.Text = "El archivo abre correctamente, pero el doctor encontró cosas que " +
                              "merecen tu atención.";
        }
    }

    private async void OnAccionPrincipalClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_diag.EsArchivoJsonDisfrazado)
            {
                OnAbrirComoProyecto?.Invoke(_diag.ArchivoOriginal);
                Close(true);
                return;
            }
            if (_diag.ContenidoReparado != null)
            {
                var pathReparado = DLDoctor.AplicarReparacionADisco(_diag);
                await AppServices.MessageBox.InfoAsync("Doctor .DL",
                    $"Archivo reparado guardado en:\n{pathReparado}\n\nAhora se abrirá automáticamente.");
                OnAbrirDL?.Invoke(pathReparado);
                Close(true);
            }
        }
        catch (Exception ex)
        {
            await AppServices.MessageBox.InfoAsync("Doctor .DL", "Error aplicando reparación: " + ex.Message);
        }
    }

    private async void OnCopiarReporteClick(object? sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"DOCTOR .DL — {_diag.ArchivoOriginal}");
        sb.AppendLine($"PuedeAbrir: {_diag.PuedeAbrir}");
        if (_diag.EsArchivoJsonDisfrazado)
            sb.AppendLine("Especial: archivo JSON disfrazado de .DL");
        sb.AppendLine();
        sb.AppendLine($"Hallazgos: {_diag.Issues.Count}");
        sb.AppendLine(new string('-', 60));
        foreach (var i in _diag.Issues)
        {
            sb.AppendLine($"[{i.Severity}] {i.Codigo}");
            sb.AppendLine($"  {i.Descripcion}");
            if (i.FixDescripcion != null) sb.AppendLine($"  Fix: {i.FixDescripcion}");
            sb.AppendLine();
        }
        try
        {
            await AppServices.Clipboard.SetTextAsync(sb.ToString());
            await AppServices.MessageBox.InfoAsync("Doctor .DL", "Reporte copiado al portapapeles.");
        }
        catch (Exception ex)
        {
            await AppServices.MessageBox.InfoAsync("Doctor .DL", "No se pudo copiar al portapapeles: " + ex.Message);
        }
    }

    private void OnCerrarClick(object? sender, RoutedEventArgs e) => Close();

    private sealed record IssueRow(string Severity, string Codigo, string Descripcion);
}
