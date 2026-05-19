using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LosasPlus.Models;
using LosasPlus.Models.Cad;

namespace LosasPlus.Views.Cad;

/// <summary>
/// Vista del modo <b>Lienzo CAD</b>. Aloja el <see cref="CadCanvasHost"/>, la
/// barra de importación de planos y el editor flotante in-canvas (Iteración 2
/// del Epic v1.2): el doble clic sobre una losa abre un popup para editar
/// Lx / Ly / Tipo. El <c>DataContext</c> es el <c>MainViewModel</c> — de ahí
/// salen <c>CadEditor</c> (sub-VM del CAD) y <c>Sistema</c> (sistema activo).
/// </summary>
public partial class CadView : UserControl
{
    private Losa? _losaEnEdicion;
    private double _edicionPosX, _edicionPosY;
    private bool _commitEnCurso;

    public CadView()
    {
        InitializeComponent();
        Canvas.LosaDobleClicada += OnLosaDobleClicada;
    }

    /// <summary>
    /// El <see cref="CadCanvasHost"/> alojado — expuesto para que
    /// <c>MainWindow</c> pueda capturar el lienzo al exportar a Excel.
    /// </summary>
    public CadCanvasHost CanvasHost => Canvas;

    // ---- Editor flotante in-canvas (Iteración 2 v1.2) ----

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
        double maxX = Math.Max(0, Canvas.ActualWidth  - EditorLosa.Width);
        double maxY = Math.Max(0, Canvas.ActualHeight - 160);
        EditorLosa.Margin = new Thickness(
            Math.Clamp(e.RectPantalla.X, 0, maxX),
            Math.Clamp(e.RectPantalla.Y, 0, maxY), 0, 0);

        EditorLosa.Visibility = Visibility.Visible;
        EditorLx.Focus();
        EditorLx.SelectAll();
    }

    /// <summary>Enter confirma la edición; Esc la cancela.</summary>
    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)       { ConfirmarEdicion(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelarEdicion();  e.Handled = true; }
    }

    /// <summary>El editor pierde el foco hacia afuera (clic fuera) → confirmar.</summary>
    private void OnEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_commitEnCurso || EditorLosa.Visibility != Visibility.Visible) return;
        if (EditorTipo.IsDropDownOpen) return;   // el desplegable abierto no es "salir"
        if (e.NewFocus is DependencyObject d && EditorLosa.IsAncestorOf(d)) return;
        ConfirmarEdicion();
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

            EditorLosa.Visibility = Visibility.Collapsed;
            _losaEnEdicion = null;
            Canvas.RefrescarLosas();
        }
        finally { _commitEnCurso = false; }
    }

    private void CancelarEdicion()
    {
        EditorLosa.Visibility = Visibility.Collapsed;
        _losaEnEdicion = null;
    }

    /// <summary>Parsea un double positivo; si el texto es inválido conserva el valor actual.</summary>
    private static double ParsearOFallback(string texto, double fallback)
        => double.TryParse(texto, NumberStyles.Any, CultureInfo.CurrentCulture, out double v) && v > 0
            ? v : fallback;
}
