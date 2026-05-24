using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LosasPlus.Grillas;
using LosasPlus.Models;
using LosasPlus.Topologia;
using LosasPlus.ViewModels;

namespace LosasPlus.Views.PlantaEstructural;

/// <summary>
/// Canvas 2D personalizado para la vista de Planta Estructural (Liga C
/// paso C1). Hereda <see cref="FrameworkElement"/> y compone 5 capas de
/// <see cref="DrawingVisual"/> con un <see cref="TransformGroup"/>
/// común para zoom + pan:
///
/// <list type="number">
///   <item>Capa 0 — Fondo gris oscuro.</item>
///   <item>Capa 1 — Grilla estructural (líneas + etiquetas A/B/C en
///   círculos en los extremos).</item>
///   <item>Capa 2 — Vigas como líneas naranjas gruesas con etiqueta
///   "V-{Id}".</item>
///   <item>Capa 3 — Columnas como cuadrados azules con etiqueta
///   "C-{Id}" centrada.</item>
///   <item>Capa 4 — Zapatas como rectángulos punteados grises.</item>
/// </list>
///
/// <para>
/// Convención de coordenadas: <c>PxPorMetro = 50</c>. El origen world
/// (0,0) está en el centro del canvas tras el primer Encuadrar. La Y
/// se invierte en el ScaleTransform (factor -1 en Y) para que crezca
/// hacia arriba en pantalla — convención clásica de planos
/// estructurales.
/// </para>
///
/// <para>
/// Edición (drag, crear viga, borrar) llegará en Liga C paso C2. En C1
/// solo hay click-selection: MouseDown izquierdo busca el elemento bajo
/// el cursor y lo publica via <c>SeleccionService.FijarSeleccion</c>.
/// </para>
/// </summary>
public sealed class PlantaEstructuralCanvas : FrameworkElement
{
    // ===== Constantes de render =====
    private const double PxPorMetro      = 50.0;
    private const double MinScale        = 0.05;
    private const double MaxScale        = 20.0;
    private const double ZoomStep        = 1.15;
    private const double LadoColumnaM    = 0.40;   // dimensión visual default
    private const double LadoZapataMargin = 0.20;   // recubrimiento visual

    // ===== Visuals + transformaciones =====
    private readonly VisualCollection _children;
    private readonly DrawingVisual    _layerFondo;
    private readonly DrawingVisual    _layerGrilla;
    private readonly DrawingVisual    _layerVigas;
    private readonly DrawingVisual    _layerColumnas;
    private readonly DrawingVisual    _layerZapatas;

    private readonly ScaleTransform     _scale     = new(1.0, -1.0, 0, 0);
    private readonly TranslateTransform _translate = new(0, 0);
    private readonly TransformGroup     _grupo     = new();

    // ===== Estado de interacción (zoom/pan) =====
    private Point? _panStart;
    private double _panInicialX, _panInicialY;

    // ===== Brushes/pens reutilizables =====
    private static readonly Brush BrushFondo       = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly Pen   PenGrilla        = new(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)) { Opacity = 0.5 }, 1.0);
    private static readonly Brush BrushEtiquetaEje = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
    private static readonly Brush BrushColumna     = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly Pen   PenColumnaBorde  = new(new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF)), 1.5);
    private static readonly Pen   PenViga          = new(new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)), 4.0);
    private static readonly Pen   PenZapata        = new(new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)), 1.5)
    { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
    private static readonly Brush BrushEtiquetaColumna = Brushes.White;
    private static readonly Brush BrushEtiquetaViga    = new SolidColorBrush(Color.FromRgb(0xFD, 0xBA, 0x74));

    static PlantaEstructuralCanvas()
    {
        BrushFondo.Freeze();
        PenGrilla.Freeze();
        BrushEtiquetaEje.Freeze();
        BrushColumna.Freeze();
        PenColumnaBorde.Freeze();
        PenViga.Freeze();
        PenZapata.Freeze();
        BrushEtiquetaColumna.Freeze();
        BrushEtiquetaViga.Freeze();
    }

    public PlantaEstructuralCanvas()
    {
        // Construir las 5 capas con el TransformGroup compartido.
        _grupo.Children.Add(_scale);
        _grupo.Children.Add(_translate);

        _layerFondo    = NuevaCapa();
        _layerGrilla   = NuevaCapa(_grupo);
        _layerVigas    = NuevaCapa(_grupo);
        _layerColumnas = NuevaCapa(_grupo);
        _layerZapatas  = NuevaCapa(_grupo);

        _children = new VisualCollection(this)
        {
            _layerFondo,
            _layerGrilla,
            _layerZapatas,   // zapatas debajo de las columnas
            _layerVigas,
            _layerColumnas,
        };

        // Inputs.
        MouseWheel += OnMouseWheel;
        MouseDown  += OnMouseDown;
        MouseMove  += OnMouseMove;
        MouseUp    += OnMouseUp;
        Focusable  = true;
        // FrameworkElement no tiene Background propio — el fondo se
        // pinta en la capa 0 (RenderFondo). Lo único que necesitamos
        // es asegurar que recibe inputs en TODO su area: asignamos
        // la capa de fondo opaca (color sólido) que cubre ActualWidth/Height.
    }

    // ===== DependencyProperties =====
    public static readonly DependencyProperty ProyectoProperty =
        DependencyProperty.Register(nameof(Proyecto), typeof(Proyecto), typeof(PlantaEstructuralCanvas),
            new PropertyMetadata(null, (d, _) => ((PlantaEstructuralCanvas)d).Redibujar()));

    public static readonly DependencyProperty NivelActivoProperty =
        DependencyProperty.Register(nameof(NivelActivo), typeof(Nivel), typeof(PlantaEstructuralCanvas),
            new PropertyMetadata(null, (d, _) => ((PlantaEstructuralCanvas)d).Redibujar()));

    public static readonly DependencyProperty GrillaActivaProperty =
        DependencyProperty.Register(nameof(GrillaActiva), typeof(GrillaEstructural?), typeof(PlantaEstructuralCanvas),
            new PropertyMetadata(null, (d, _) => ((PlantaEstructuralCanvas)d).Redibujar()));

    public static readonly DependencyProperty RevisionProperty =
        DependencyProperty.Register(nameof(Revision), typeof(int), typeof(PlantaEstructuralCanvas),
            new PropertyMetadata(0, (d, _) => ((PlantaEstructuralCanvas)d).Redibujar()));

    public static readonly DependencyProperty SeleccionProperty =
        DependencyProperty.Register(nameof(Seleccion), typeof(SeleccionService), typeof(PlantaEstructuralCanvas),
            new PropertyMetadata(null));

    public Proyecto? Proyecto
    { get => (Proyecto?)GetValue(ProyectoProperty);   set => SetValue(ProyectoProperty, value); }
    public Nivel? NivelActivo
    { get => (Nivel?)GetValue(NivelActivoProperty);    set => SetValue(NivelActivoProperty, value); }
    public GrillaEstructural? GrillaActiva
    {
        get
        {
            var v = GetValue(GrillaActivaProperty);
            return v is GrillaEstructural g ? g : (GrillaEstructural?)null;
        }
        set => SetValue(GrillaActivaProperty, value);
    }
    public int Revision
    { get => (int)GetValue(RevisionProperty);           set => SetValue(RevisionProperty, value); }
    public SeleccionService? Seleccion
    { get => (SeleccionService?)GetValue(SeleccionProperty); set => SetValue(SeleccionProperty, value); }

    // ===== Hit-test internal map: rect en world coords → DomainKey =====
    private readonly List<(Rect WorldRect, DomainKey Key)> _hitRects = new();

    // ===== Render principal =====
    private void Redibujar()
    {
        RenderFondo();
        RenderGrilla();
        RenderZapatas();
        RenderVigas();
        RenderColumnas();

        // Encuadrar al primer render si nunca lo hicimos.
        if (_translate.X == 0 && _translate.Y == 0 && ActualWidth > 0)
            EncuadrarAlContenido();
    }

    private void RenderFondo()
    {
        using var dc = _layerFondo.RenderOpen();
        dc.DrawRectangle(BrushFondo, null, new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)));
    }

    private void RenderGrilla()
    {
        using var dc = _layerGrilla.RenderOpen();
        var grillaOpt = GrillaActiva;
        if (grillaOpt is null) return;
        var grilla = grillaOpt.Value;
        if (grilla.EstaVacia) return;

        // Calcular extensión en metros del bounding box de la grilla + margen.
        var (xMin, xMax, yMin, yMax) = ExtensionGrilla(grilla, margenM: 1.5);

        // Líneas verticales (EjeX): atraviesan rango Y.
        foreach (var ex in grilla.EjesX)
        {
            var p1 = new Point(ex.PosicionMetros * PxPorMetro, yMin * PxPorMetro);
            var p2 = new Point(ex.PosicionMetros * PxPorMetro, yMax * PxPorMetro);
            dc.DrawLine(PenGrilla, p1, p2);
            // Etiqueta arriba (yMax)
            DibujarEtiquetaEje(dc, ex.Nombre,
                new Point(ex.PosicionMetros * PxPorMetro, yMax * PxPorMetro + 18));
        }
        // Líneas horizontales (EjeY): atraviesan rango X.
        foreach (var ey in grilla.EjesY)
        {
            var p1 = new Point(xMin * PxPorMetro, ey.PosicionMetros * PxPorMetro);
            var p2 = new Point(xMax * PxPorMetro, ey.PosicionMetros * PxPorMetro);
            dc.DrawLine(PenGrilla, p1, p2);
            DibujarEtiquetaEje(dc, ey.Nombre,
                new Point(xMin * PxPorMetro - 18, ey.PosicionMetros * PxPorMetro));
        }
    }

    private void RenderColumnas()
    {
        _hitRects.RemoveAll(h => h.Key.Tipo == TipoElemento.Columna);
        using var dc = _layerColumnas.RenderOpen();
        var nivel = NivelActivo;
        if (nivel is null) return;

        double ladoPx = LadoColumnaM * PxPorMetro;
        foreach (var col in nivel.Columnas)
        {
            // Coords: PosX/Y si están definidas, sino grilla artificial (mismo
            // patrón que GrafoProyectadoBuilder fallback).
            var (xM, yM) = ResolverPosColumna(col, nivel);
            double cx = xM * PxPorMetro;
            double cy = yM * PxPorMetro;
            var rect = new Rect(cx - ladoPx / 2, cy - ladoPx / 2, ladoPx, ladoPx);
            dc.DrawRectangle(BrushColumna, PenColumnaBorde, rect);
            DibujarEtiqueta(dc, $"C-{col.Id}", new Point(cx, cy), BrushEtiquetaColumna, 9);
            // Registrar hit-test rect en world coords (metros).
            var worldRect = new Rect(xM - LadoColumnaM / 2, yM - LadoColumnaM / 2, LadoColumnaM, LadoColumnaM);
            _hitRects.Add((worldRect, new DomainKey(TipoElemento.Columna, col.Id)));
        }
    }

    private void RenderVigas()
    {
        _hitRects.RemoveAll(h => h.Key.Tipo == TipoElemento.Viga);
        using var dc = _layerVigas.RenderOpen();
        var nivel = NivelActivo;
        if (nivel is null) return;

        // Versión simple C1: las vigas se renderizan SOLO si el dominio
        // futuro las dota de endpoints en planta. Hoy las Vigas no tienen
        // PosX1/Y1/PosX2/Y2 en el dominio — por convención provisional,
        // la dibujamos como una línea horizontal de longitud = sum(tramos)
        // partiendo del origen del nivel (0,0) en dirección +X. Esto NO
        // refleja conectividad real — eso llegará en C2 cuando agreguemos
        // edición y endpoints en planta.
        double offsetY = 0;
        foreach (var viga in nivel.Vigas)
        {
            double longitudM = 0;
            foreach (var t in viga.Tramos) longitudM += t.Longitud;
            if (longitudM < 0.01) continue;
            // Posición provisional: cada viga "apilada" en su propia fila Y.
            offsetY -= 0.5;
            var p1 = new Point(0, offsetY * PxPorMetro);
            var p2 = new Point(longitudM * PxPorMetro, offsetY * PxPorMetro);
            dc.DrawLine(PenViga, p1, p2);
            DibujarEtiqueta(dc, $"V-{viga.Id}",
                new Point((p1.X + p2.X) / 2, p1.Y - 12), BrushEtiquetaViga, 9);
            // Hit-test world rect — banda fina alrededor de la línea.
            var worldRect = new Rect(0, offsetY - 0.15, longitudM, 0.30);
            _hitRects.Add((worldRect, new DomainKey(TipoElemento.Viga, viga.Id)));
        }
    }

    private void RenderZapatas()
    {
        _hitRects.RemoveAll(h => h.Key.Tipo == TipoElemento.Zapata);
        using var dc = _layerZapatas.RenderOpen();
        var nivel = NivelActivo;
        if (nivel is null) return;

        foreach (var zap in nivel.Zapatas)
        {
            // Coords desde PosX/Y o desde la columna pareada (mismo Id).
            var (xM, yM) = ResolverPosZapata(zap, nivel);
            double bM = zap.Dimensiones.LargoB;
            double lM = zap.Dimensiones.AnchoL;
            double bPx = bM * PxPorMetro;
            double lPx = lM * PxPorMetro;
            var rect = new Rect(xM * PxPorMetro - bPx / 2, yM * PxPorMetro - lPx / 2, bPx, lPx);
            dc.DrawRectangle(null, PenZapata, rect);
            var worldRect = new Rect(xM - bM / 2, yM - lM / 2, bM, lM);
            _hitRects.Add((worldRect, new DomainKey(TipoElemento.Zapata, zap.Id)));
        }
    }

    // ===== Helpers de coordenadas =====

    private static (double xM, double yM) ResolverPosColumna(LosasPlus.Columnas.Columna col, Nivel nivel)
    {
        if (col.PosX.HasValue && col.PosY.HasValue)
            return (col.PosX.Value, col.PosY.Value);
        // Fallback: grilla artificial 8×N a 6m (mismo que GrafoProyectadoBuilder).
        int idx = Math.Max(1, col.Id) - 1;
        return ((idx % 8) * 6.0, (idx / 8) * 6.0);
    }

    private static (double xM, double yM) ResolverPosZapata(LosasPlus.Zapatas.ZapataAislada zap, Nivel nivel)
    {
        if (zap.PosX.HasValue && zap.PosY.HasValue)
            return (zap.PosX.Value, zap.PosY.Value);
        // Heredar de la columna pareada (mismo Id) si existe.
        foreach (var col in nivel.Columnas)
            if (col.Id == zap.Id && col.PosX.HasValue && col.PosY.HasValue)
                return (col.PosX.Value, col.PosY.Value);
        int idx = Math.Max(1, zap.Id) - 1;
        return ((idx % 8) * 6.0, (idx / 8) * 6.0);
    }

    private static (double xMin, double xMax, double yMin, double yMax)
        ExtensionGrilla(GrillaEstructural grilla, double margenM)
    {
        double xMin = 0, xMax = 0, yMin = 0, yMax = 0;
        bool primero = true;
        foreach (var ex in grilla.EjesX)
        {
            if (primero) { xMin = xMax = ex.PosicionMetros; primero = false; }
            else { if (ex.PosicionMetros < xMin) xMin = ex.PosicionMetros;
                   if (ex.PosicionMetros > xMax) xMax = ex.PosicionMetros; }
        }
        primero = true;
        foreach (var ey in grilla.EjesY)
        {
            if (primero) { yMin = yMax = ey.PosicionMetros; primero = false; }
            else { if (ey.PosicionMetros < yMin) yMin = ey.PosicionMetros;
                   if (ey.PosicionMetros > yMax) yMax = ey.PosicionMetros; }
        }
        return (xMin - margenM, xMax + margenM, yMin - margenM, yMax + margenM);
    }

    private void EncuadrarAlContenido()
    {
        // Centrar el origen world en el centro del canvas.
        _translate.X = ActualWidth / 2;
        _translate.Y = ActualHeight / 2;
    }

    private static void DibujarEtiqueta(DrawingContext dc, string texto, Point centro, Brush fg, double tamFuente)
    {
        var ft = new FormattedText(texto, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), tamFuente, fg, 1.25);
        // Compensar la inversión Y del ScaleTransform (-1) para que el
        // texto se lea derecho: dibujar con un ScaleTransform local (1, -1).
        dc.PushTransform(new ScaleTransform(1, -1, centro.X, centro.Y));
        dc.DrawText(ft, new Point(centro.X - ft.Width / 2, centro.Y - ft.Height / 2));
        dc.Pop();
    }

    private static void DibujarEtiquetaEje(DrawingContext dc, string texto, Point posicion)
    {
        var ft = new FormattedText(texto, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"),
            10, BrushEtiquetaEje, 1.25);
        dc.PushTransform(new ScaleTransform(1, -1, posicion.X, posicion.Y));
        // Pequeño círculo de fondo para legibilidad.
        dc.DrawEllipse(BrushFondo, new Pen(BrushEtiquetaEje, 1.0),
            new Point(posicion.X, posicion.Y), 9, 9);
        dc.DrawText(ft, new Point(posicion.X - ft.Width / 2, posicion.Y - ft.Height / 2));
        dc.Pop();
    }

    // ===== Inputs: zoom, pan, click-select =====

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep;
        double nuevoX = Math.Abs(_scale.ScaleX) * factor;
        if (nuevoX < MinScale || nuevoX > MaxScale) return;
        // Zoom centrado en el cursor: ajustar translate.
        var pos = e.GetPosition(this);
        double absX = (pos.X - _translate.X) / _scale.ScaleX;
        double absY = (pos.Y - _translate.Y) / _scale.ScaleY;
        _scale.ScaleX = nuevoX * Math.Sign(_scale.ScaleX);
        _scale.ScaleY = nuevoX * Math.Sign(_scale.ScaleY);
        _translate.X = pos.X - absX * _scale.ScaleX;
        _translate.Y = pos.Y - absY * _scale.ScaleY;
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers == ModifierKeys.Control))
        {
            _panStart = e.GetPosition(this);
            _panInicialX = _translate.X;
            _panInicialY = _translate.Y;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
            HitTestYSeleccionar(e.GetPosition(this));
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_panStart is { } start)
        {
            var p = e.GetPosition(this);
            _translate.X = _panInicialX + (p.X - start.X);
            _translate.Y = _panInicialY + (p.Y - start.Y);
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_panStart is not null)
        {
            _panStart = null;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            e.Handled = true;
        }
    }

    private void HitTestYSeleccionar(Point pixelPos)
    {
        // Pixel → world (m): invertir TransformGroup.
        double xWorldPx = (pixelPos.X - _translate.X) / _scale.ScaleX;
        double yWorldPx = (pixelPos.Y - _translate.Y) / _scale.ScaleY;
        double xM = xWorldPx / PxPorMetro;
        double yM = yWorldPx / PxPorMetro;

        DomainKey? mejor = null;
        // Iterar en orden inverso (los últimos dibujados son más prioritarios:
        // columnas > vigas > zapatas en el orden de _children).
        for (int i = _hitRects.Count - 1; i >= 0; i--)
        {
            var (rect, key) = _hitRects[i];
            if (rect.Contains(xM, yM)) { mejor = key; break; }
        }
        if (mejor.HasValue)
            Seleccion?.FijarSeleccion(mejor.Value, this);
    }

    // ===== Plomería de FrameworkElement =====
    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        // Re-render del fondo (que depende del tamaño) + re-encuadrar.
        RenderFondo();
        if (_translate.X == 0 && _translate.Y == 0) EncuadrarAlContenido();
    }

    private static DrawingVisual NuevaCapa(Transform? transform = null)
    {
        var v = new DrawingVisual();
        if (transform is not null) v.Transform = transform;
        return v;
    }
}
