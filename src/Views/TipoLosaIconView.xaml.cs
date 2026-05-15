using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LosasPlus.Models;

namespace LosasPlus.Views;

/// <summary>
/// Renderiza el icono visual de un tipo de losa.
///
/// <para>
/// Estrategia: <b>siempre render programático</b> a partir de
/// <see cref="TipoLosa.Catalogo"/>[<see cref="Codigo"/>].<c>Bordes</c>
/// (N/E/S/W en orden [0,1,2,3]). Esto garantiza que los 24 tipos del catálogo
/// se vean consistentes entre sí (mismos colores, mismo grosor, misma
/// orientación) y entre las 3 superficies donde aparecen:
/// </para>
/// <list type="bullet">
///   <item>Celda <c>Tipo</c> del DataGrid de losas.</item>
///   <item><c>TiposLosaPanel</c> de la barra inferior.</item>
///   <item><c>SelectorTipoLosaWindow</c> modal del selector.</item>
/// </list>
///
/// <para>
/// Los SVGs en <c>Resources/icons/tipo_NN.svg</c> se mantienen en disco pero
/// no se consumen: tenían colores hardcoded (rojo <c>#ff3131</c>, fill gris)
/// y faltaban 2 archivos (23, 41), lo que generaba 24 iconos visualmente
/// inconsistentes. El render programático lee colores dinámicos del tema
/// (<c>FgPrimary</c>, <c>BgInput</c>, <c>FgMuted</c>) y nunca queda desfasado.
/// </para>
///
/// El control se redibuja al cambiar <see cref="Codigo"/>, el tamaño o el tema.
/// </summary>
public partial class TipoLosaIconView : UserControl
{
    public static readonly DependencyProperty CodigoProperty =
        DependencyProperty.Register(nameof(Codigo), typeof(int), typeof(TipoLosaIconView),
            new PropertyMetadata(0, OnCodigoChanged));

    public int Codigo
    {
        get => (int)GetValue(CodigoProperty);
        set => SetValue(CodigoProperty, value);
    }

    public TipoLosaIconView()
    {
        InitializeComponent();
        SizeChanged += (_, __) => Render();
        Loaded += (_, __) => Render();
        App.ThemeChanged += _ => Render();
    }

    private static void OnCodigoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TipoLosaIconView)d).Render();

    private void Render()
    {
        // Fuente única de verdad: TipoLosa.Catalogo[codigo].Bordes. Los SVGs
        // legacy quedaron deprecados — ver doc-comment de la clase.
        Svg.Visibility = Visibility.Collapsed;
        Canvas.Visibility = Visibility.Visible;
        DrawProgrammatic();
    }

    // ---------- Fallback programático ----------

    private Brush Stroke() => (Brush?)TryFindResource("FgPrimary") ?? Brushes.Black;
    private Brush Fill()   => (Brush?)TryFindResource("BgInput")   ?? Brushes.White;
    private Brush Muted()  => (Brush?)TryFindResource("FgMuted")   ?? Brushes.Gray;

    private void DrawProgrammatic()
    {
        Canvas.Children.Clear();
        if (!TipoLosa.Catalogo.TryGetValue(Codigo, out var t)) return;

        double w = ActualWidth, h = ActualHeight;
        if (w < 12 || h < 12) return;

        double pad = 6;
        double size = Math.Min(w, h) - 2 * pad;
        double left = (w - size) / 2;
        double top  = (h - size) / 2;
        double right = left + size;
        double bottom = top + size;

        DrawBorder(t.Bordes[0], left, top,    right, top,    new Vector(0, 1));    // N
        DrawBorder(t.Bordes[1], right, top,   right, bottom, new Vector(-1, 0));   // E
        DrawBorder(t.Bordes[2], left, bottom, right, bottom, new Vector(0, -1));   // S
        DrawBorder(t.Bordes[3], left, top,    left,  bottom, new Vector(1, 0));    // W

        double rcirc = Math.Min(size * 0.22, 16);
        var ellipse = new Ellipse
        {
            Width = rcirc * 2, Height = rcirc * 2,
            Stroke = Stroke(), StrokeThickness = 1, Fill = Fill(),
        };
        Canvas.SetLeft(ellipse, left + size / 2 - rcirc);
        Canvas.SetTop(ellipse, top + size / 2 - rcirc);
        Canvas.Children.Add(ellipse);

        var label = new TextBlock
        {
            Text = Codigo.ToString(),
            Foreground = Stroke(),
            FontWeight = FontWeights.Bold,
            FontSize = Math.Min(rcirc * 0.95, 13),
            FontFamily = new FontFamily("Segoe UI"),
        };
        label.Measure(new Size(rcirc * 2, rcirc * 2));
        Canvas.SetLeft(label, left + size / 2 - label.DesiredSize.Width / 2);
        Canvas.SetTop(label, top + size / 2 - label.DesiredSize.Height / 2);
        Canvas.Children.Add(label);
    }

    private void DrawBorder(BorderKind kind, double x1, double y1, double x2, double y2, Vector perpInside)
    {
        switch (kind)
        {
            case BorderKind.Apoyado:
                AddLine(x1, y1, x2, y2, Stroke(), 1.0, false);
                break;
            case BorderKind.Empotrado:
                AddLine(x1, y1, x2, y2, Stroke(), 2.0, false);
                AddHatchTicks(x1, y1, x2, y2, perpInside);
                break;
            case BorderKind.Libre:
            case BorderKind.Vuelo:
                AddLine(x1, y1, x2, y2, Muted(), 1.0, true);
                break;
        }
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush brush, double thickness, bool dashed)
    {
        var line = new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = brush, StrokeThickness = thickness,
        };
        if (dashed) line.StrokeDashArray = new DoubleCollection(new[] { 3.0, 2.0 });
        Canvas.Children.Add(line);
    }

    private void AddHatchTicks(double x1, double y1, double x2, double y2, Vector perpInside)
    {
        double dx = x2 - x1, dy = y2 - y1;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 6) return;

        double tickLen = 4.0;
        double spacing = 5.5;
        int n = (int)(length / spacing);
        if (n < 2) return;

        for (int i = 1; i < n; i++)
        {
            double t = (double)i / n;
            double bx = x1 + dx * t;
            double by = y1 + dy * t;
            double ex = bx + perpInside.X * tickLen + (dx / length) * tickLen * 0.6;
            double ey = by + perpInside.Y * tickLen + (dy / length) * tickLen * 0.6;
            AddLine(bx, by, ex, ey, Stroke(), 0.8, false);
        }
    }
}
