using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LosasPlus.Services;

namespace LosasPlus.Views;

/// <summary>
/// Visor PDF multipágina (H3 — convergencia v0.8.1). Toolbar con abrir, navegación
/// prev/next página y zoom 50–200%. Rasteriza la página actual con
/// <see cref="PdfImportador"/> (Docnet.Core/PDFium → BGRA → <c>WriteableBitmap</c>),
/// por lo que respeta el pin SkiaSharp 2.88 de Avalonia. Es <b>code-behind puro</b>
/// (sin ViewModel): mantiene su propio estado de página/zoom y su file-picker, así que
/// es reubicable y no acopla al <c>MainViewModel</c>.
/// </summary>
public partial class PdfViewerControl : UserControl
{
    private string? _path;
    private int _pagina;      // índice 0-based de la página visible
    private int _total;
    private double _zoom = 1.0;
    private bool _cargando;

    private const double ZoomMin = 0.5, ZoomMax = 2.0, ZoomStep = 0.25;
    private const int AnchoBasePx = 1100;   // ancho de render a zoom 1.0

    public PdfViewerControl()
    {
        InitializeComponent();
        ActualizarToolbar();
    }

    /// <summary>Carga un PDF y muestra su primera página. Reutilizable desde fuera.</summary>
    public async void Cargar(string path)
    {
        _path = path;
        _pagina = 0;
        _total = await PdfImportador.ContarPaginasAsync(path);
        if (_total <= 0)
        {
            _path = null;
            PaginaImg.Source = null;
            LblEstado.Text = "No se pudo abrir el PDF.";
            ActualizarToolbar();
            return;
        }
        LblEstado.Text = Path.GetFileName(path);
        await RenderAsync();
    }

    private async void OnAbrir(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir PDF",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf", "*.PDF" } },
            },
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } p)
            Cargar(p);
    }

    private async void OnPrev(object? sender, RoutedEventArgs e)
    {
        if (_pagina > 0) { _pagina--; await RenderAsync(); }
    }

    private async void OnNext(object? sender, RoutedEventArgs e)
    {
        if (_pagina < _total - 1) { _pagina++; await RenderAsync(); }
    }

    private async void OnZoomIn(object? sender, RoutedEventArgs e)
    {
        double z = Math.Min(ZoomMax, _zoom + ZoomStep);
        if (z != _zoom) { _zoom = z; await RenderAsync(); }
    }

    private async void OnZoomOut(object? sender, RoutedEventArgs e)
    {
        double z = Math.Max(ZoomMin, _zoom - ZoomStep);
        if (z != _zoom) { _zoom = z; await RenderAsync(); }
    }

    /// <summary>Rasteriza la página actual al zoom actual y la muestra.</summary>
    private async Task RenderAsync()
    {
        if (_path is null || _cargando) return;
        _cargando = true;
        try
        {
            int ancho = (int)(AnchoBasePx * _zoom);
            var bmp = await PdfImportador.RasterizarPaginaIndiceAsync(_path, _pagina, ancho);
            if (bmp is not null) PaginaImg.Source = bmp;
            ActualizarToolbar();
        }
        finally { _cargando = false; }
    }

    private void ActualizarToolbar()
    {
        LblPagina.Text = _total > 0 ? $"pág {_pagina + 1} / {_total}" : "—";
        LblZoom.Text = $"{(int)(_zoom * 100)}%";
        BtnPrev.IsEnabled = _path is not null && _pagina > 0;
        BtnNext.IsEnabled = _path is not null && _pagina < _total - 1;
    }
}
