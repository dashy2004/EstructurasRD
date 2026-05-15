using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using LosasPlus.Services;

namespace LosasPlus.Views;

/// <summary>
/// Ventana modal del Doctor de archivos .DL. Muestra el reporte del
/// <see cref="DLDoctor"/> con shading por severidad y un botón de acción
/// contextual:
/// <list type="bullet">
///   <item>Si el archivo es JSON disfrazado: "Abrir como proyecto" — llama al
///         callback <see cref="OnAbrirComoProyecto"/>.</item>
///   <item>Si el doctor generó contenido reparado: "Aplicar reparación y abrir"
///         — escribe el .reparado.DL y llama a <see cref="OnAbrirDL"/>.</item>
///   <item>Si el archivo está limpio: la acción principal queda oculta.</item>
/// </list>
/// </summary>
public partial class DLDoctorWindow : Window
{
    private readonly DLDiagnostico _diag;

    /// <summary>Callback que el MainWindow inyecta para abrir un .DL reparado.</summary>
    public Action<string>? OnAbrirDL { get; set; }

    /// <summary>Callback para abrir un archivo como proyecto .lpx.json (caso JSON disfrazado).</summary>
    public Action<string>? OnAbrirComoProyecto { get; set; }

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
            AccionPrincipalBtn.Visibility = Visibility.Collapsed;
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
            AccionPrincipalBtn.Visibility = Visibility.Visible;
            AccionHint.Text = "El archivo contiene un proyecto JSON de MemoriaPlus guardado con " +
                              "extensión .DL. La acción lo abrirá usando el opener de proyecto.";
        }
        else if (_diag.ContenidoReparado != null)
        {
            HeaderTitle.Text = "⚠ Archivo con problemas reparables";
            AccionPrincipalBtn.Content = "Aplicar reparación y abrir";
            AccionPrincipalBtn.Visibility = Visibility.Visible;
            AccionHint.Text = $"Se generará una copia reparada en {_diag.PathSugeridoReparado} " +
                              "(el original no se modifica).";
        }
        else if (_diag.TieneErrores)
        {
            HeaderTitle.Text = "✗ Archivo con errores no auto-reparables";
            AccionPrincipalBtn.Visibility = Visibility.Collapsed;
            AccionHint.Text = "Revisá los errores en la tabla. Algunos requieren edición manual " +
                              "del .DL en un editor de texto plano.";
        }
        else
        {
            // Sólo info/warn que no afectan apertura
            HeaderTitle.Text = "⚠ Archivo con avisos (abre OK)";
            AccionPrincipalBtn.Visibility = Visibility.Collapsed;
            AccionHint.Text = "El archivo abre correctamente, pero el doctor encontró cosas que " +
                              "merecen tu atención.";
        }
    }

    private void OnAccionPrincipalClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_diag.EsArchivoJsonDisfrazado)
            {
                OnAbrirComoProyecto?.Invoke(_diag.ArchivoOriginal);
                DialogResult = true;
                Close();
                return;
            }
            if (_diag.ContenidoReparado != null)
            {
                var pathReparado = DLDoctor.AplicarReparacionADisco(_diag);
                MessageBox.Show(this,
                    $"Archivo reparado guardado en:\n{pathReparado}\n\nAhora se abrirá automáticamente.",
                    "Doctor .DL", MessageBoxButton.OK, MessageBoxImage.Information);
                OnAbrirDL?.Invoke(pathReparado);
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Error aplicando reparación: " + ex.Message,
                "Doctor .DL", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCopiarReporteClick(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"DOCTOR .DL — {_diag.ArchivoOriginal}");
        sb.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm}");
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
            Clipboard.SetText(sb.ToString());
            MessageBox.Show(this, "Reporte copiado al portapapeles.",
                "Doctor .DL", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo copiar al portapapeles: " + ex.Message,
                "Doctor .DL", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCerrarClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>Fila para el DataGrid (solo strings — el grid bindea contra esto).</summary>
    private sealed record IssueRow(string Severity, string Codigo, string Descripcion);
}
