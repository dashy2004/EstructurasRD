using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Services;

namespace LosasPlus.Views.Cad;

/// <summary>
/// Host de renderizado <b>retained-mode</b> del editor CAD (Fase 1.B del
/// <c>PLAN_CAD_V1.md</c>). Hereda directamente de <see cref="FrameworkElement"/>
/// y dibuja con <see cref="DrawingVisual"/> + <see cref="DrawingContext"/> —
/// <b>sin un solo <c>Shape</c></b> (Rectangle/Line). Esto escala a planos con
/// cientos de entidades, a diferencia del <c>DiagramView</c> basado en
/// <c>Canvas</c> + UIElements (que se conserva intacto y aparte).
///
/// <para>
/// Sistema de 3 capas, cada una un <see cref="DrawingVisual"/>:
/// </para>
/// <list type="bullet">
///   <item><b>Capa 0 — Grilla</b>: retícula métrica de referencia.</item>
///   <item><b>Capa 1 — Plano DXF</b>: las entidades de <see cref="Plano"/>.</item>
///   <item><b>Capa 2 — Losas</b>: las losas del <see cref="Sistema"/>, en modo
///         solo-lectura, posicionadas por <see cref="LayoutSolver"/>.</item>
/// </list>
///
/// <para>
/// Zoom y pan se aplican mediante un <see cref="TransformGroup"/> compartido
/// (la <b>misma instancia</b> en las 3 capas), de modo que un único cambio
/// mueve todo el lienzo de forma coherente.
/// </para>
/// </summary>
public sealed class CadCanvasHost : FrameworkElement
{
    // ---- Constantes de render / interacción ----
    private const double PxPorMetro = 50.0;
    private const double MinScale   = 0.05;
    private const double MaxScale   = 20.0;
    private const double ZoomStep   = 1.15;

    // ---- Capas (Capa 0 grilla, Capa 1 plano, Capa 2 losas) ----
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _capaGrilla = new();
    private readonly DrawingVisual _capaPlano  = new();
    private readonly DrawingVisual _capaLosas  = new();

    // ---- Transform compartido para zoom/pan ----
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _translate = new(0, 0);
    private readonly TransformGroup _transform = new();

    // ---- Estado de pan ----
    private bool _isPanning;
    private Point _panStart;
    private double _panStartTx, _panStartTy;

    public CadCanvasHost()
    {
        _transform.Children.Add(_scale);
        _transform.Children.Add(_translate);

        // El MISMO TransformGroup en las 3 capas → zoom/pan coherente.
        _capaGrilla.Transform = _transform;
        _capaPlano.Transform  = _transform;
        _capaLosas.Transform  = _transform;

        // Orden de pintado: grilla al fondo, plano en medio, losas arriba.
        _visuals = new VisualCollection(this) { _capaGrilla, _capaPlano, _capaLosas };

        Focusable = true;
        ClipToBounds = true;
        Background_HitTestFix();
    }

    /// <summary>
    /// Un <see cref="FrameworkElement"/> sin fondo no recibe eventos de mouse
    /// en sus zonas vacías. Forzamos un fondo transparente vía un visual de
    /// fondo mínimo para que el zoom/pan funcione en todo el área.
    /// </summary>
    private void Background_HitTestFix()
    {
        // No-op explícito: el hit-test se resuelve porque ClipToBounds + el
        // override de OnRender no aplica; usamos el propio host. Mantener el
        // método documenta la decisión. El host capta MouseWheel/MouseDown
        // porque IsHitTestVisible es true por defecto en FrameworkElement
        // cuando hay VisualChildren que cubren el área.
    }

    // =====================================================================
    // Contrato del host de visuales — WPF lo necesita para renderizar
    // =====================================================================

    /// <inheritdoc/>
    protected override int VisualChildrenCount => _visuals.Count;

    /// <inheritdoc/>
    protected override Visual GetVisualChild(int index)
    {
        if (index < 0 || index >= _visuals.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _visuals[index];
    }

    // =====================================================================
    // Dependency Properties — datos a dibujar
    // =====================================================================

    /// <summary>Plano DXF importado a dibujar en la Capa 1. Null = sin plano.</summary>
    public static readonly DependencyProperty PlanoProperty =
        DependencyProperty.Register(nameof(Plano), typeof(PlanoReferencia), typeof(CadCanvasHost),
            new PropertyMetadata(null, OnPlanoChanged));

    public PlanoReferencia? Plano
    {
        get => (PlanoReferencia?)GetValue(PlanoProperty);
        set => SetValue(PlanoProperty, value);
    }

    /// <summary>Sistema activo cuyas losas se dibujan en la Capa 2. Null = sin losas.</summary>
    public static readonly DependencyProperty SistemaProperty =
        DependencyProperty.Register(nameof(Sistema), typeof(Sistema), typeof(CadCanvasHost),
            new PropertyMetadata(null, OnSistemaChanged));

    public Sistema? Sistema
    {
        get => (Sistema?)GetValue(SistemaProperty);
        set => SetValue(SistemaProperty, value);
    }

    private static void OnPlanoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (CadCanvasHost)d;
        host.RedibujarPlano();
        host.RedibujarLosas();   // el offset de las losas depende del plano
    }

    private static void OnSistemaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((CadCanvasHost)d).RedibujarLosas();

    /// <summary>
    /// Comando que se ejecuta cuando el usuario hace clic dentro de un polígono
    /// cerrado de la Capa 1 (plano DXF). El parámetro del comando es la
    /// <see cref="PolilineaCad"/> sobre la que se hizo clic — el ViewModel
    /// decide si mapearla a una losa (Fase 2).
    /// </summary>
    public static readonly DependencyProperty PoligonoClickCommandProperty =
        DependencyProperty.Register(nameof(PoligonoClickCommand), typeof(ICommand), typeof(CadCanvasHost),
            new PropertyMetadata(null));

    public ICommand? PoligonoClickCommand
    {
        get => (ICommand?)GetValue(PoligonoClickCommandProperty);
        set => SetValue(PoligonoClickCommandProperty, value);
    }

    // =====================================================================
    // Ciclo de vida — redibujar la grilla cuando cambia el tamaño
    // =====================================================================

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RedibujarGrilla();
        RedibujarPlano();
        RedibujarLosas();
    }

    // =====================================================================
    // Capa 0 — Grilla métrica
    // =====================================================================

    private void RedibujarGrilla()
    {
        using var dc = _capaGrilla.RenderOpen();

        double w = Math.Max(ActualWidth, 200);
        double h = Math.Max(ActualHeight, 200);
        // Cubrir un área generosa para que la grilla no "se corte" al panear.
        double extra = 2000;
        var penFino   = new Pen(new SolidColorBrush(Color.FromArgb(40, 120, 120, 120)), 0.5);
        var penMetro5 = new Pen(new SolidColorBrush(Color.FromArgb(80, 120, 120, 120)), 0.8);
        penFino.Freeze();
        penMetro5.Freeze();

        // Línea cada 1 m; cada 5 m más marcada.
        for (double xm = -40; xm <= 40; xm += 1)
        {
            double x = xm * PxPorMetro;
            var pen = Math.Abs(xm % 5) < 1e-9 ? penMetro5 : penFino;
            dc.DrawLine(pen, new Point(x, -extra), new Point(x, h + extra));
        }
        for (double ym = -40; ym <= 40; ym += 1)
        {
            double y = ym * PxPorMetro;
            var pen = Math.Abs(ym % 5) < 1e-9 ? penMetro5 : penFino;
            dc.DrawLine(pen, new Point(-extra, y), new Point(w + extra, y));
        }
    }

    // =====================================================================
    // Capa 1 — Plano DXF
    // =====================================================================

    private void RedibujarPlano()
    {
        using var dc = _capaPlano.RenderOpen();
        var plano = Plano;
        if (plano is null || plano.EstaVacio) return;

        // Flip Y: el DXF tiene Y ascendente; en pantalla Y desciende. Mostramos
        // el plano "derecho" reflejando respecto al máximo Y del bounding box.
        double maxY = plano.MaxY;
        Point ToPx(PuntoCad p) => new(p.X * PxPorMetro, (maxY - p.Y) * PxPorMetro);

        var penPlano = new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x99)), 1.2);
        penPlano.Freeze();
        var brushTexto = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        brushTexto.Freeze();

        foreach (var ent in plano.Entidades)
        {
            switch (ent)
            {
                case LineaCad l:
                    dc.DrawLine(penPlano, ToPx(l.Inicio), ToPx(l.Fin));
                    break;

                case PolilineaCad poli when poli.CantidadVertices >= 2:
                {
                    var geo = new StreamGeometry();
                    using (var gc = geo.Open())
                    {
                        gc.BeginFigure(ToPx(poli.Vertices[0]), isFilled: false, isClosed: poli.Cerrada);
                        for (int i = 1; i < poli.Vertices.Count; i++)
                            gc.LineTo(ToPx(poli.Vertices[i]), isStroked: true, isSmoothJoin: false);
                    }
                    geo.Freeze();
                    dc.DrawGeometry(null, penPlano, geo);
                    break;
                }

                case TextoCad t when !string.IsNullOrEmpty(t.Contenido):
                {
                    var ft = new FormattedText(
                        t.Contenido,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),
                        Math.Max(8, t.Altura * PxPorMetro),
                        brushTexto,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(ft, ToPx(t.Posicion));
                    break;
                }

                case ArcoCad a when a.Radio > 0:
                {
                    var centro = ToPx(a.Centro);
                    double rPx = a.Radio * PxPorMetro;
                    if (a.EsCirculoCompleto)
                    {
                        dc.DrawEllipse(null, penPlano, centro, rPx, rPx);
                    }
                    else
                    {
                        // Arco parcial: PathGeometry con un ArcSegment.
                        var geo = ArcoAGeometria(a, ToPx);
                        if (geo != null) dc.DrawGeometry(null, penPlano, geo);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>Construye la geometría de un arco parcial (no círculo completo).</summary>
    private static Geometry? ArcoAGeometria(ArcoCad a, Func<PuntoCad, Point> toPx)
    {
        double rPx = a.Radio * PxPorMetro;
        // Puntos inicial y final del arco en coordenadas de modelo.
        double i = a.AnguloInicioGrados * Math.PI / 180.0;
        double f = a.AnguloFinGrados * Math.PI / 180.0;
        var pIni = new PuntoCad(a.Centro.X + a.Radio * Math.Cos(i), a.Centro.Y + a.Radio * Math.Sin(i));
        var pFin = new PuntoCad(a.Centro.X + a.Radio * Math.Cos(f), a.Centro.Y + a.Radio * Math.Sin(f));
        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(toPx(pIni), isFilled: false, isClosed: false);
            double barrido = a.AnguloFinGrados - a.AnguloInicioGrados;
            gc.ArcTo(toPx(pFin), new Size(rPx, rPx), 0,
                isLargeArc: Math.Abs(barrido) > 180,
                SweepDirection.Counterclockwise, isStroked: true, isSmoothJoin: false);
        }
        geo.Freeze();
        return geo;
    }

    // =====================================================================
    // Capa 2 — Losas (read-only, posicionadas por LayoutSolver)
    // =====================================================================

    private void RedibujarLosas()
    {
        using var dc = _capaLosas.RenderOpen();
        var sistema = Sistema;
        if (sistema is null || sistema.Losas.Count == 0) return;

        LayoutSolver.LayoutResult layout;
        try { layout = LayoutSolver.Solve(sistema); }
        catch { return; }  // geometría degenerada — no dibujar, sin crashear

        var rellenoLosa = new SolidColorBrush(Color.FromArgb(45, 0x2E, 0x7D, 0x32));
        rellenoLosa.Freeze();
        var rellenoHuerf = new SolidColorBrush(Color.FromArgb(45, 0xC1, 0x8A, 0x2C));
        rellenoHuerf.Freeze();
        var penLosa = new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), 2.0);
        penLosa.Freeze();
        var penHuerf = new Pen(new SolidColorBrush(Color.FromRgb(0xC1, 0x8A, 0x2C)), 1.5);
        penHuerf.Freeze();
        var brushId = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1D));
        brushId.Freeze();

        // Offset de las losas:
        //  - Si hay losas ANCLADAS (PosX/PosY), el LayoutSolver no normaliza y
        //    sus coordenadas son absolutas del plano → offset 0, caen sobre su
        //    polígono de origen.
        //  - Si NO hay ancladas (modo Fase 1.B), se desplazan a la derecha del
        //    plano para no superponerse con él.
        bool hayAncladas = false;
        foreach (var l in sistema.Losas)
            if (l.TienePosicionExplicita) { hayAncladas = true; break; }
        double offsetX = hayAncladas
            ? 0
            : ((Plano is { EstaVacio: false }) ? (Plano.MaxX + 2.0) * PxPorMetro : 0);

        foreach (var p in layout.Placements)
        {
            double x = offsetX + p.X * PxPorMetro;
            double y = p.Y * PxPorMetro;
            double w = p.Width * PxPorMetro;
            double h = p.Height * PxPorMetro;
            var rect = new Rect(x, y, w, h);

            dc.DrawRectangle(p.Huerfana ? rellenoHuerf : rellenoLosa,
                             p.Huerfana ? penHuerf : penLosa, rect);

            var ft = new FormattedText(
                p.Id.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                             FontWeights.Bold, FontStretches.Normal),
                Math.Min(20, h * 0.35), brushId,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(x + w / 2 - ft.Width / 2, y + h / 2 - ft.Height / 2));
        }
    }

    // =====================================================================
    // Interacción — zoom (rueda) y pan (drag). Réplica de DiagramView,
    // adaptada a DrawingVisual (transforma el TransformGroup compartido).
    // =====================================================================

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        double factor = e.Delta > 0 ? ZoomStep : 1 / ZoomStep;
        double nuevo = Math.Clamp(_scale.ScaleX * factor, MinScale, MaxScale);
        if (Math.Abs(nuevo - _scale.ScaleX) < 1e-9) return;

        // Zoom centrado en el cursor: el punto bajo el cursor no se desplaza.
        var pt = e.GetPosition(this);
        double k = nuevo / _scale.ScaleX;
        _translate.X = pt.X - k * (pt.X - _translate.X);
        _translate.Y = pt.Y - k * (pt.Y - _translate.Y);
        _scale.ScaleX = _scale.ScaleY = nuevo;
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _isPanning = true;
        _panStart = e.GetPosition(this);
        _panStartTx = _translate.X;
        _panStartTy = _translate.Y;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        bool estabaPanning = _isPanning;
        _isPanning = false;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;

        // Distinguir un clic de un pan: si el mouse casi no se desplazó desde
        // el botón-abajo, fue un clic → hit-test sobre los polígonos del plano.
        var pt = e.GetPosition(this);
        if (estabaPanning && (pt - _panStart).Length < 5.0)
        {
            var poligono = HitTestPoligono(pt);
            if (poligono != null && PoligonoClickCommand is { } cmd && cmd.CanExecute(poligono))
                cmd.Execute(poligono);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isPanning) return;
        var pt = e.GetPosition(this);
        _translate.X = _panStartTx + (pt.X - _panStart.X);
        _translate.Y = _panStartTy + (pt.Y - _panStart.Y);
    }

    /// <summary>
    /// Determina sobre qué polígono cerrado del plano DXF (si alguno) cae el
    /// punto de pantalla <paramref name="pantalla"/>. Convierte el punto de
    /// coordenadas de pantalla a coordenadas DXF (deshaciendo el zoom/pan y el
    /// flip-Y de la capa del plano) y aplica el test punto-en-polígono.
    /// </summary>
    private PolilineaCad? HitTestPoligono(Point pantalla)
    {
        var plano = Plano;
        if (plano is null || plano.EstaVacio) return null;
        if (_scale.ScaleX <= 0) return null;

        // Pantalla → espacio pre-transform (deshacer scale + translate).
        double preX = (pantalla.X - _translate.X) / _scale.ScaleX;
        double preY = (pantalla.Y - _translate.Y) / _scale.ScaleY;
        // Pre-transform → coordenadas DXF (px→m y deshacer el flip-Y del plano).
        double dxfX = preX / PxPorMetro;
        double dxfY = plano.MaxY - preY / PxPorMetro;
        var punto = new PuntoCad(dxfX, dxfY);

        foreach (var ent in plano.Entidades)
        {
            if (ent is PolilineaCad poli && poli.Cerrada &&
                PoligonoLosaMapper.ContienePunto(poli, punto))
                return poli;
        }
        return null;
    }
}
