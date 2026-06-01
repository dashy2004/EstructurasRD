using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.ViewModels;

namespace LosasPlus.Views.Cad;

/// <summary>
/// Vista del modo <b>Lienzo CAD</b> — port a Avalonia (Fase E.2: interacción). Aloja
/// el <see cref="CadCanvasHost"/>, la barra de importación de planos y los editores
/// flotantes in-canvas: el doble clic sobre una losa abre un popup para editar
/// Lx / Ly / Tipo; la calibración del PDF abre un popup para indicar la distancia
/// real. El <c>DataContext</c> es el <c>MainViewModel</c> — de ahí salen
/// <c>CadEditor</c> (sub-VM del CAD) y <c>Sistema</c> (sistema activo).
/// </summary>
public partial class CadView : UserControl
{
    private Losa? _losaEnEdicion;
    private double _edicionPosX, _edicionPosY;
    private bool _commitEnCurso;

    // Calibración del PDF: el host dispara el evento tras el 2º click; estos
    // campos guardan los argumentos para usarlos al confirmar o cancelar.
    private double _calPivoteX, _calPivoteY, _calDistanciaActual;
    private bool _calibrandoActivo;

    // InitializeComponent (no AvaloniaXamlLoader.Load): puebla los campos x:Name
    // (Canvas, EditorLosa, EditorTipo, …) que el code-behind referencia.
    public CadView()
    {
        InitializeComponent();
        Canvas.LosaDobleClicada += OnLosaDobleClicada;
        Canvas.CalibrarPdfPuntosListos += Canvas_CalibrarPdfPuntosListos;
    }

    /// <summary>
    /// El <see cref="CadCanvasHost"/> alojado — expuesto para que <c>MainWindow</c>
    /// pueda capturar el lienzo al exportar a Excel.
    /// </summary>
    public CadCanvasHost CanvasHost => Canvas;

    // ---- Editor flotante in-canvas (editar losa) ----

    /// <summary>Doble clic sobre una losa → poblar, posicionar y mostrar el editor.</summary>
    private void OnLosaDobleClicada(object? sender, LosaDobleClicEventArgs e)
    {
        _losaEnEdicion = e.Losa;
        _edicionPosX = e.PosX;
        _edicionPosY = e.PosY;

        EditorLx.Text = e.Losa.Lx.ToString("0.###", CultureInfo.CurrentCulture);
        EditorLy.Text = e.Losa.Ly.ToString("0.###", CultureInfo.CurrentCulture);
        EditorTipo.SelectedValue = e.Losa.Tipo;

        // Anclar el editor sobre la losa, clampeado para que no se salga del lienzo.
        double maxX = Math.Max(0, Canvas.Bounds.Width  - EditorLosa.Width);
        double maxY = Math.Max(0, Canvas.Bounds.Height - 160);
        EditorLosa.Margin = new Thickness(
            Math.Clamp(e.RectPantalla.X, 0, maxX),
            Math.Clamp(e.RectPantalla.Y, 0, maxY), 0, 0);

        EditorLosa.IsVisible = true;
        EditorLx.Focus();
        EditorLx.SelectAll();
    }

    /// <summary>Enter confirma la edición; Esc la cancela.</summary>
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)       { ConfirmarEdicion(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelarEdicion();  e.Handled = true; }
    }

    /// <summary>
    /// El editor pierde el foco hacia afuera → confirmar. Avalonia no entrega el
    /// «NewFocus» en <c>LostFocus</c> (a diferencia de WPF), así que diferimos el
    /// chequeo y consultamos el <see cref="IFocusManager"/>: si el elemento ahora
    /// enfocado sigue siendo descendiente del editor (p.ej. tabular entre TextBox),
    /// no es una salida real.
    /// </summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_commitEnCurso || !EditorLosa.IsVisible) return;
            if (EditorTipo.IsDropDownOpen) return;   // el desplegable abierto no es "salir"
            var foco = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (foco is Visual v && EditorLosa.IsVisualAncestorOf(v)) return;
            ConfirmarEdicion();
        }, DispatcherPriority.Background);
    }

    /// <summary>Empaqueta los valores del editor y dispara <c>ActualizarLosaCommand</c>.</summary>
    private void ConfirmarEdicion()
    {
        if (_commitEnCurso || _losaEnEdicion is null) return;
        _commitEnCurso = true;
        try
        {
            var losa = _losaEnEdicion;
            double lx   = ParsearOFallback(EditorLx.Text, losa.Lx);
            double ly   = ParsearOFallback(EditorLy.Text, losa.Ly);
            int    tipo = EditorTipo.SelectedValue is int t ? t : losa.Tipo;

            var args = new ActualizacionLosaArgs(losa, _edicionPosX, _edicionPosY, lx, ly, tipo);
            if (Canvas.ActualizarLosaCommand is { } cmd && cmd.CanExecute(args))
                cmd.Execute(args);

            EditorLosa.IsVisible = false;
            _losaEnEdicion = null;
            Canvas.RefrescarLosas();
        }
        finally { _commitEnCurso = false; }
    }

    private void CancelarEdicion()
    {
        EditorLosa.IsVisible = false;
        _losaEnEdicion = null;
    }

    /// <summary>Parsea un double positivo; si el texto es inválido conserva el valor actual.</summary>
    private static double ParsearOFallback(string? texto, double fallback)
        => double.TryParse(texto, NumberStyles.Any, CultureInfo.CurrentCulture, out double v) && v > 0
            ? v : fallback;

    // ---- Editor flotante de calibración del PDF ----

    /// <summary>
    /// El host fijó P₂ y disparó el evento. Posicionamos el editor en el punto
    /// medio (clampeado al viewport), guardamos los argumentos y le damos foco al
    /// TextBox de distancia real.
    /// </summary>
    private void Canvas_CalibrarPdfPuntosListos(object? sender, CalibrarPdfPuntosListosEventArgs e)
    {
        _calPivoteX = e.PivoteX;
        _calPivoteY = e.PivoteY;
        _calDistanciaActual = e.DistanciaActual;
        _calibrandoActivo = true;

        CalibrarLineaActual.Text =
            $"Línea trazada: {e.DistanciaActual:0.000} m. Indique la medida real:";
        CalibrarDistanciaReal.Text = e.DistanciaActual.ToString("0.000", CultureInfo.CurrentCulture);

        double maxX = Math.Max(0, Canvas.Bounds.Width  - EditorCalibrarPdf.Width);
        double maxY = Math.Max(0, Canvas.Bounds.Height - 180);
        double left = Math.Clamp(e.MidpointPantalla.X - EditorCalibrarPdf.Width / 2.0, 0, maxX);
        double top  = Math.Clamp(e.MidpointPantalla.Y + 12, 0, maxY);
        EditorCalibrarPdf.Margin = new Thickness(left, top, 0, 0);

        EditorCalibrarPdf.IsVisible = true;
        CalibrarDistanciaReal.Focus();
        CalibrarDistanciaReal.SelectAll();
    }

    /// <summary>Enter confirma; Esc cancela todo el flujo.</summary>
    private void OnCalibrarKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)       { ConfirmarCalibracion(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelarCalibracion();  e.Handled = true; }
    }

    /// <summary>Si el editor pierde el foco sin commit explícito → cancelar.</summary>
    private void OnCalibrarLostFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_calibrandoActivo || !EditorCalibrarPdf.IsVisible) return;
            var foco = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (foco is Visual v && EditorCalibrarPdf.IsVisualAncestorOf(v)) return;
            CancelarCalibracion();
        }, DispatcherPriority.Background);
    }

    private void OnCalibrarConfirmar(object? sender, RoutedEventArgs e) => ConfirmarCalibracion();
    private void OnCalibrarCancelar(object? sender, RoutedEventArgs e)  => CancelarCalibracion();

    private void ConfirmarCalibracion()
    {
        if (!_calibrandoActivo) return;
        _calibrandoActivo = false;
        EditorCalibrarPdf.IsVisible = false;

        if (DataContext is not MainViewModel mvm
            || mvm.CadEditor.AplicarCalibrarPdfCommand is not { } cmd) return;

        double real = ParsearOFallback(CalibrarDistanciaReal.Text, _calDistanciaActual);
        var args = new CalibracionPdfArgs(_calPivoteX, _calPivoteY, _calDistanciaActual, real);
        if (cmd.CanExecute(args)) cmd.Execute(args);
    }

    private void CancelarCalibracion()
    {
        if (!_calibrandoActivo) return;
        _calibrandoActivo = false;
        EditorCalibrarPdf.IsVisible = false;

        if (DataContext is MainViewModel mvm
            && mvm.CadEditor.CancelarCalibrarPdfCommand is { } cmd
            && cmd.CanExecute(null))
            cmd.Execute(null);
    }
}
