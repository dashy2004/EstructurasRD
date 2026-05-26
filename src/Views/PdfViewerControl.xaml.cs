using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LosasPlus.Services;

namespace LosasPlus.Views;

/// <summary>
/// Control reutilizable para visualizar PDFs embebidos en la app
/// (Liga E paso E1). Carga la primera página al asignar
/// <see cref="PdfPath"/>, cuenta el total con
/// <see cref="PdfImportador.ContarPaginasAsync"/> y permite navegar
/// con prev/next + un slider de zoom 50%–200%.
///
/// <para>
/// Pipeline:
/// <list type="number">
///   <item>Cambio de <see cref="PdfPath"/> → dispara <c>CargarPdfAsync</c>.</item>
///   <item><c>CargarPdfAsync</c> → cuenta páginas + carga la página 0.</item>
///   <item>Botones prev/next → modifican <c>_indiceActual</c> + recargan.</item>
///   <item>Slider zoom → modifica <c>ImgPagina.LayoutTransform</c> (ScaleTransform).
///   El bitmap se rasteriza siempre a ancho objetivo fijo (1600 px), el zoom es
///   sólo visual — evita re-rasterizar en cada cambio de slider.</item>
/// </list>
/// </para>
/// </summary>
public partial class PdfViewerControl : UserControl
{
    public PdfViewerControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty PdfPathProperty =
        DependencyProperty.Register(nameof(PdfPath), typeof(string), typeof(PdfViewerControl),
            new PropertyMetadata(null, OnPdfPathChanged));

    /// <summary>
    /// Ruta absoluta al archivo PDF que se desea visualizar. Al cambiar, el
    /// control rasteriza la primera página y resetea la navegación. Si es
    /// <c>null</c> o vacío, muestra el placeholder.
    /// </summary>
    public string? PdfPath
    {
        get => (string?)GetValue(PdfPathProperty);
        set => SetValue(PdfPathProperty, value);
    }

    private int _indiceActual;
    private int _totalPaginas;

    private static void OnPdfPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl ctl)
            _ = ctl.CargarPdfAsync();
    }

    private async Task CargarPdfAsync()
    {
        var path = PdfPath;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            ImgPagina.Source = null;
            LblPlaceholder.Visibility = Visibility.Visible;
            LblPlaceholder.Text = string.IsNullOrEmpty(path)
                ? "Selecciona una norma para visualizar su PDF."
                : "PDF no encontrado en disco.";
            LblTotal.Text = "0";
            TxtPagina.Text = "0";
            return;
        }
        LblPlaceholder.Visibility = Visibility.Visible;
        LblPlaceholder.Text = "Cargando PDF…";
        _totalPaginas = await PdfImportador.ContarPaginasAsync(path);
        if (_totalPaginas <= 0)
        {
            LblPlaceholder.Text = "El PDF no pudo abrirse (posiblemente protegido o corrupto).";
            return;
        }
        _indiceActual = 0;
        LblTotal.Text = _totalPaginas.ToString();
        await RenderPaginaActualAsync();
    }

    private async Task RenderPaginaActualAsync()
    {
        var path = PdfPath;
        if (string.IsNullOrEmpty(path)) return;
        TxtPagina.Text = (_indiceActual + 1).ToString();
        BtnAnterior.IsEnabled = _indiceActual > 0;
        BtnSiguiente.IsEnabled = _indiceActual < _totalPaginas - 1;

        var bmp = await PdfImportador.RasterizarPaginaAsync(path, _indiceActual);
        if (bmp is null)
        {
            LblPlaceholder.Visibility = Visibility.Visible;
            LblPlaceholder.Text = $"No se pudo rasterizar la página {_indiceActual + 1}.";
            return;
        }
        ImgPagina.Source = bmp;
        LblPlaceholder.Visibility = Visibility.Collapsed;
    }

    private async void OnAnteriorClick(object sender, RoutedEventArgs e)
    {
        if (_indiceActual <= 0) return;
        _indiceActual--;
        await RenderPaginaActualAsync();
    }

    private async void OnSiguienteClick(object sender, RoutedEventArgs e)
    {
        if (_indiceActual >= _totalPaginas - 1) return;
        _indiceActual++;
        await RenderPaginaActualAsync();
    }

    private async void OnPaginaKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (!int.TryParse(TxtPagina.Text.Trim(), out int n)) return;
        n = System.Math.Clamp(n, 1, System.Math.Max(1, _totalPaginas));
        _indiceActual = n - 1;
        await RenderPaginaActualAsync();
        e.Handled = true;
    }

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        double z = e.NewValue;
        ImgPagina.LayoutTransform = new System.Windows.Media.ScaleTransform(z, z);
        LblZoom.Text = $"{(int)(z * 100)}%";
    }
}
