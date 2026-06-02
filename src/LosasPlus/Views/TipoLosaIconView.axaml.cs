using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Platform;
using LosasPlus.Models;

namespace LosasPlus.Views;

/// <summary>
/// Icono visual de un tipo de losa: SVG embebido (avares) si existe, o dibujo
/// programático con Shapes según el patrón de bordes N/E/S/W. Port a Avalonia:
/// svgc:SvgViewbox→Avalonia.Svg.Skia.Svg; DependencyProperty→StyledProperty;
/// pack://+GetResourceStream→AssetLoader; Line.X1/Y1→StartPoint/EndPoint;
/// DoubleCollection→AvaloniaList&lt;double&gt;.
/// </summary>
public partial class TipoLosaIconView : UserControl
{
    public static readonly StyledProperty<int> CodigoProperty =
        AvaloniaProperty.Register<TipoLosaIconView, int>(nameof(Codigo));

    public int Codigo { get => GetValue(CodigoProperty); set => SetValue(CodigoProperty, value); }

    private static readonly Lazy<HashSet<int>> _svgAvailable = new(LoadAvailableSvgCodes);

    public TipoLosaIconView()
    {
        InitializeComponent();
        SizeChanged += (_, __) => Render();
        Loaded += (_, __) => Render();
        App.ThemeChanged += _ => Render();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CodigoProperty) Render();
    }

    private static HashSet<int> LoadAvailableSvgCodes()
    {
        var result = new HashSet<int>();
        foreach (var codigo in TipoLosa.Catalogo.Keys)
        {
            try
            {
                var uri = new Uri($"avares://LosasPlus/Resources/icons/tipo_{codigo}.svg");
                if (AssetLoader.Exists(uri)) result.Add(codigo);
            }
            catch { /* fallback programático */ }
        }
        return result;
    }

    private void Render()
    {
        if (_svgAvailable.Value.Contains(Codigo))
        {
            try
            {
                Svg.Path = $"avares://LosasPlus/Resources/icons/tipo_{Codigo}.svg";
                Svg.IsVisible = true;
                Canvas.IsVisible = false;
                Canvas.Children.Clear();
                return;
            }
            catch { /* cae al programático */ }
        }
        Svg.IsVisible = false;
        Canvas.IsVisible = true;
        DrawProgrammatic();
    }

    private IBrush Stroke() => (this.TryFindResource("FgPrimary", out var v) && v is IBrush b) ? b : Brushes.Black;
    private IBrush Fill()   => (this.TryFindResource("BgInput",   out var v) && v is IBrush b) ? b : Brushes.White;
    private IBrush Muted()  => (this.TryFindResource("FgMuted",   out var v) && v is IBrush b) ? b : Brushes.Gray;

    private void DrawProgrammatic()
    {
        Canvas.Children.Clear();
        if (!TipoLosa.Catalogo.TryGetValue(Codigo, out var t)) return;

        double w = Bounds.Width, h = Bounds.Height;
        if (w < 12 || h < 12) return;

        double pad = 6;
        double size = Math.Min(w, h) - 2 * pad;
        double left = (w - size) / 2;
        double top  = (h - size) / 2;
        double right = left + size;
        double bottom = top + size;

        DrawBorder(t.Bordes[0], left, top,    right, top,    new Vector(0, 1));
        DrawBorder(t.Bordes[1], right, top,   right, bottom, new Vector(-1, 0));
        DrawBorder(t.Bordes[2], left, bottom, right, bottom, new Vector(0, -1));
        DrawBorder(t.Bordes[3], left, top,    left,  bottom, new Vector(1, 0));

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
            FontWeight = FontWeight.Bold,
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

    private void AddLine(double x1, double y1, double x2, double y2, IBrush brush, double thickness, bool dashed)
    {
        var line = new Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = brush, StrokeThickness = thickness,
        };
        if (dashed) line.StrokeDashArray = new AvaloniaList<double> { 3.0, 2.0 };
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
