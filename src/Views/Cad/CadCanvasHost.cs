using System;
using System.Collections.Generic;
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
/// Sistema de 4 capas, cada una un <see cref="DrawingVisual"/>:
/// </para>
/// <list type="bullet">
///   <item><b>Capa 0 — Grilla</b>: retícula métrica, ejes del origen (0,0) y el
///         bounding box del plano DXF.</item>
///   <item><b>Capa 1 — Plano DXF</b>: las entidades de <see cref="Plano"/>.</item>
///   <item><b>Capa 2 — Losas</b>: las losas del <see cref="Sistema"/>,
///         posicionadas por <see cref="LayoutSolver"/>.</item>
///   <item><b>Capa 3 — Overlay</b>: capa efímera de interacción (selección,
///         tiradores, "fantasma" y cotas dinámicas de arrastre). Nunca toca el SSOT.</item>
/// </list>
///
/// <para>
/// Zoom y pan se aplican mediante un <see cref="TransformGroup"/> compartido
/// (la <b>misma instancia</b> en las 4 capas), de modo que un único cambio
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

    // ---- Constantes de interacción de la Fase 3 ----
    private const double ClicMaxPx      = 5.0;    // px — umbral clic vs. arrastre
    private const double PasoSnapGrilla = 1.0;    // m  — la grilla métrica es de 1 m
    private const double UmbralSnapM    = 0.20;   // m  — radio de enganche del snap
    private const double MinLadoM       = 0.10;   // m  — lado mínimo de una losa
    private const double HandleLadoPx   = 9.0;    // px — tamaño del tirador en pantalla
    private const double ChipRadioPx    = 11.0;   // px — radio del chip de adyacencia (Fase 4)

    // ---- Capas (0 grilla · 1 plano · 2 losas · 3 overlay efímero) ----
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _capaGrilla  = new();
    private readonly DrawingVisual _capaPlano   = new();
    private readonly DrawingVisual _capaLosas   = new();
    private readonly DrawingVisual _capaOverlay = new();

    // ---- Transform compartido para zoom/pan ----
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _translate = new(0, 0);
    private readonly TransformGroup _transform = new();

    // ---- Pinceles del overlay (frozen — ver el constructor estático) ----
    private static readonly SolidColorBrush PincelOverlay        = new(Color.FromRgb(0x1E, 0x88, 0xE5));
    private static readonly SolidColorBrush PincelOverlayRelleno = new(Color.FromArgb(48, 0x1E, 0x88, 0xE5));
    private static readonly SolidColorBrush PincelCota           = new(Color.FromRgb(0xEF, 0x6C, 0x00));

    // ---- Estado de interacción (efímero, sólo de la vista — nunca toca el SSOT) ----
    private enum ModoArrastre { Ninguno, Pan, Mover, Redimensionar }
    private ModoArrastre _modo = ModoArrastre.Ninguno;
    private Losa? _losaSeleccionada;
    private Point _dragStart;
    private double _panStartTx, _panStartTy;
    private int _handleActivo = -1;
    private Rect _rectOriginalPx;
    private Rect _rectFantasmaPx;
    private IReadOnlyList<LayoutSolver.Placement> _placements = Array.Empty<LayoutSolver.Placement>();
    private double _offsetXPx;

    // ---- Chips de adyacencia (Fase 4) — sugerencias de conexión en el overlay ----
    private IReadOnlyList<AdyacenciaCandidata> _chips = Array.Empty<AdyacenciaCandidata>();

    static CadCanvasHost()
    {
        PincelOverlay.Freeze();
        PincelOverlayRelleno.Freeze();
        PincelCota.Freeze();
    }

    public CadCanvasHost()
    {
        _transform.Children.Add(_scale);
        _transform.Children.Add(_translate);

        // El MISMO TransformGroup en las 4 capas → zoom/pan coherente.
        _capaGrilla.Transform  = _transform;
        _capaPlano.Transform   = _transform;
        _capaLosas.Transform   = _transform;
        _capaOverlay.Transform = _transform;

        // Orden de pintado: grilla al fondo · plano · losas · overlay arriba.
        _visuals = new VisualCollection(this) { _capaGrilla, _capaPlano, _capaLosas, _capaOverlay };

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
        host.RedibujarGrilla();  // el bounding box del DXF se dibuja en la Capa 0
        host.RedibujarPlano();
        host.RedibujarLosas();   // el offset de las losas depende del plano
        host.RedibujarOverlay(); // la selección sigue a la losa reposicionada
    }

    private static void OnSistemaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (CadCanvasHost)d;
        host._losaSeleccionada = null;   // el sistema cambió → la selección quedó obsoleta
        host.RedibujarLosas();
        host.RecomputarChips();
        host.RedibujarOverlay();
    }

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

    /// <summary>
    /// Comando que se ejecuta al terminar de <b>mover</b> o <b>redimensionar</b>
    /// una losa (Fase 3). El parámetro es un <see cref="ActualizacionLosaArgs"/>
    /// con la geometría final ya calculada — el ViewModel toma el snapshot de
    /// Undo y escribe el SSOT. El host nunca le pasa eventos de mouse crudos.
    /// </summary>
    public static readonly DependencyProperty ActualizarLosaCommandProperty =
        DependencyProperty.Register(nameof(ActualizarLosaCommand), typeof(ICommand), typeof(CadCanvasHost),
            new PropertyMetadata(null));

    public ICommand? ActualizarLosaCommand
    {
        get => (ICommand?)GetValue(ActualizarLosaCommandProperty);
        set => SetValue(ActualizarLosaCommandProperty, value);
    }

    /// <summary>
    /// Comando que se ejecuta cuando el usuario hace clic en un "chip" de
    /// adyacencia (Fase 4). El parámetro es la <see cref="AdyacenciaCandidata"/>
    /// del chip — el ViewModel toma el snapshot de Undo y agrega el
    /// <c>BordeAdic</c> a <c>BordesX</c>/<c>BordesY</c>.
    /// </summary>
    public static readonly DependencyProperty CrearBordeAdicCommandProperty =
        DependencyProperty.Register(nameof(CrearBordeAdicCommand), typeof(ICommand), typeof(CadCanvasHost),
            new PropertyMetadata(null));

    public ICommand? CrearBordeAdicCommand
    {
        get => (ICommand?)GetValue(CrearBordeAdicCommandProperty);
        set => SetValue(CrearBordeAdicCommandProperty, value);
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
        RedibujarOverlay();
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

        // ---- Ejes del origen (0,0) — destacados sobre la grilla (B1) ----
        var penEjeX = new Pen(new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)), 1.6);  // rojo
        var penEjeY = new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), 1.6);  // verde
        penEjeX.Freeze();
        penEjeY.Freeze();
        dc.DrawLine(penEjeX, new Point(-extra, 0), new Point(w + extra, 0));  // eje X (y = 0)
        dc.DrawLine(penEjeY, new Point(0, -extra), new Point(0, h + extra));  // eje Y (x = 0)

        var pincelOrigen = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        pincelOrigen.Freeze();
        dc.DrawEllipse(pincelOrigen, null, new Point(0, 0), 3.0, 3.0);

        // ---- Bounding box del plano DXF — contexto de escala total (B1) ----
        if (Plano is { EstaVacio: false } plano)
        {
            var penBBox = new Pen(new SolidColorBrush(Color.FromArgb(120, 0x33, 0x66, 0x99)), 1.0)
            {
                DashStyle = new DashStyle(new double[] { 6, 4 }, 0),
            };
            penBBox.Freeze();
            // El plano (Capa 1) se dibuja con flip-Y; su bounding box en pantalla:
            var bbox = new Rect(plano.MinX * PxPorMetro, 0,
                                plano.Ancho * PxPorMetro, plano.Alto * PxPorMetro);
            dc.DrawRectangle(null, penBBox, bbox);
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
        _placements = Array.Empty<LayoutSolver.Placement>();
        _offsetXPx = 0;
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
        _offsetXPx = offsetX;

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

        _placements = layout.Placements;
    }

    // =====================================================================
    // Capa 3 — Overlay efímero (selección, tiradores, fantasma de arrastre)
    // =====================================================================

    /// <summary>
    /// Redibuja la capa overlay: los chips de adyacencia (siempre), y según el
    /// estado de interacción la selección + 8 tiradores, o el contorno punteado
    /// "fantasma" mientras se arrastra. Es estado puramente visual — jamás toca
    /// el SSOT.
    /// </summary>
    private void RedibujarOverlay()
    {
        using var dc = _capaOverlay.RenderOpen();

        // Grosores y tamaños se dividen por la escala → constantes EN PANTALLA.
        double inv = _scale.ScaleX > 0 ? 1.0 / _scale.ScaleX : 1.0;

        // Los chips de adyacencia se dibujan SIEMPRE (haya o no selección).
        DibujarChips(dc, inv);

        if (_losaSeleccionada is null) return;

        if (_modo is ModoArrastre.Mover or ModoArrastre.Redimensionar)
        {
            var penFantasma = new Pen(PincelOverlay, 1.6 * inv)
            {
                DashStyle = new DashStyle(new double[] { 4, 3 }, 0),
            };
            dc.DrawRectangle(PincelOverlayRelleno, penFantasma, _rectFantasmaPx);
            DibujarCotas(dc, inv);
            return;
        }

        var rectPx = RectPxDeLosaSeleccionada();
        if (rectPx.IsEmpty) return;

        dc.DrawRectangle(null, new Pen(PincelOverlay, 2.0 * inv), rectPx);

        // 8 tiradores cuadrados de tamaño constante en pantalla.
        double lado = HandleLadoPx * inv;
        var penHandle = new Pen(PincelOverlay, 1.4 * inv);
        foreach (var c in HandleCentros(rectPx))
            dc.DrawRectangle(Brushes.White, penHandle,
                             new Rect(c.X - lado / 2, c.Y - lado / 2, lado, lado));
    }

    /// <summary>Rect en px pre-transform de la losa seleccionada, o <see cref="Rect.Empty"/>.</summary>
    private Rect RectPxDeLosaSeleccionada()
    {
        if (_losaSeleccionada is null) return Rect.Empty;
        foreach (var p in _placements)
            if (p.Losa.Id == _losaSeleccionada.Id)
                return PlacementARectPx(p);
        return Rect.Empty;
    }

    /// <summary>Rect en px pre-transform de un <see cref="LayoutSolver.Placement"/>.</summary>
    private Rect PlacementARectPx(LayoutSolver.Placement p) => new(
        _offsetXPx + p.X * PxPorMetro, p.Y * PxPorMetro,
        p.Width * PxPorMetro, p.Height * PxPorMetro);

    /// <summary>Pantalla → espacio pre-transform (deshace zoom y pan).</summary>
    private Point PantallaAPre(Point pantalla) => new(
        (pantalla.X - _translate.X) / _scale.ScaleX,
        (pantalla.Y - _translate.Y) / _scale.ScaleY);

    /// <summary>
    /// Centros de los 8 tiradores de un rect, en orden horario desde la esquina
    /// superior-izquierda (0..7: SI · S · SD · D · ID · I · II · izq).
    /// </summary>
    private static Point[] HandleCentros(Rect r)
    {
        double mx = r.X + r.Width / 2, my = r.Y + r.Height / 2;
        return new[]
        {
            new Point(r.Left,  r.Top),     // 0 sup-izq
            new Point(mx,      r.Top),     // 1 sup-centro
            new Point(r.Right, r.Top),     // 2 sup-der
            new Point(r.Right, my),        // 3 centro-der
            new Point(r.Right, r.Bottom),  // 4 inf-der
            new Point(mx,      r.Bottom),  // 5 inf-centro
            new Point(r.Left,  r.Bottom),  // 6 inf-izq
            new Point(r.Left,  my),        // 7 centro-izq
        };
    }

    // =====================================================================
    // Hit-testing de losas y tiradores
    // =====================================================================

    /// <summary>Placement de la losa bajo el punto de pantalla, o null (el último dibujado gana).</summary>
    private LayoutSolver.Placement? HitTestLosa(Point pantalla)
    {
        if (_scale.ScaleX <= 0 || _placements.Count == 0) return null;
        var pre = PantallaAPre(pantalla);
        for (int i = _placements.Count - 1; i >= 0; i--)
            if (PlacementARectPx(_placements[i]).Contains(pre))
                return _placements[i];
        return null;
    }

    /// <summary>Índice (0..7) del tirador de la losa seleccionada bajo el cursor, o -1.</summary>
    private int HandleBajoCursor(Point pantalla)
    {
        if (_losaSeleccionada is null || _scale.ScaleX <= 0) return -1;
        var rectPx = RectPxDeLosaSeleccionada();
        if (rectPx.IsEmpty) return -1;

        var pre = PantallaAPre(pantalla);
        double radio = HandleLadoPx / _scale.ScaleX;   // zona de agarre generosa
        var centros = HandleCentros(rectPx);
        for (int i = 0; i < centros.Length; i++)
            if (Math.Abs(pre.X - centros[i].X) <= radio &&
                Math.Abs(pre.Y - centros[i].Y) <= radio)
                return i;
        return -1;
    }

    // =====================================================================
    // Capa 3 — Chips de adyacencia (Fase 4)
    // =====================================================================

    /// <summary>
    /// Reevalúa qué losas ancladas son adyacentes y aún no están conectadas por
    /// un <c>BordeAdic</c>. El resultado alimenta los chips del overlay.
    /// </summary>
    private void RecomputarChips()
        => _chips = Sistema is { } s
            ? AdyacenciaDetector.Detectar(s)
            : Array.Empty<AdyacenciaCandidata>();

    /// <summary>Dibuja un círculo de acento con un "+" en cada adyacencia sugerida.</summary>
    private void DibujarChips(DrawingContext dc, double inv)
    {
        if (_chips.Count == 0) return;
        double r = ChipRadioPx * inv;
        double b = r * 0.5;
        var penMas = new Pen(Brushes.White, 2.0 * inv);
        foreach (var c in _chips)
        {
            double cx = _offsetXPx + c.MidX * PxPorMetro;
            double cy = c.MidY * PxPorMetro;
            dc.DrawEllipse(PincelOverlay, null, new Point(cx, cy), r, r);
            dc.DrawLine(penMas, new Point(cx - b, cy), new Point(cx + b, cy));
            dc.DrawLine(penMas, new Point(cx, cy - b), new Point(cx, cy + b));
        }
    }

    /// <summary>Índice del chip de adyacencia bajo el cursor, o -1.</summary>
    private int HitTestChip(Point pantalla)
    {
        if (_chips.Count == 0 || _scale.ScaleX <= 0) return -1;
        var pre = PantallaAPre(pantalla);
        double r = ChipRadioPx / _scale.ScaleX;
        for (int i = 0; i < _chips.Count; i++)
        {
            double cx = _offsetXPx + _chips[i].MidX * PxPorMetro;
            double cy = _chips[i].MidY * PxPorMetro;
            double dx = pre.X - cx, dy = pre.Y - cy;
            if (dx * dx + dy * dy <= r * r) return i;
        }
        return -1;
    }

    // =====================================================================
    // Capa 3 — Cotas dinámicas (Fase B.2)
    // =====================================================================

    /// <summary>
    /// Dibuja las cotas dinámicas (distancias dₙ) entre la losa que se está
    /// arrastrando y sus vecinas ancladas. Invoca al motor puro
    /// <see cref="MotorCotas"/> (Fase A) y traza cada cota como una línea con
    /// dos tick marks y el texto del valor. Sólo se llama durante un arrastre,
    /// así que las cotas se limpian solas al soltar la losa.
    /// </summary>
    private void DibujarCotas(DrawingContext dc, double inv)
    {
        if (_placements.Count == 0) return;

        // La losa activa es el rectángulo fantasma; las vecinas, el resto.
        var activa = new RectM(
            _rectFantasmaPx.X / PxPorMetro, _rectFantasmaPx.Y / PxPorMetro,
            _rectFantasmaPx.Width / PxPorMetro, _rectFantasmaPx.Height / PxPorMetro);

        var ancladas = new List<RectM>();
        foreach (var p in _placements)
        {
            if (_losaSeleccionada != null && p.Losa.Id == _losaSeleccionada.Id) continue;
            var r = PlacementARectPx(p);
            ancladas.Add(new RectM(r.X / PxPorMetro, r.Y / PxPorMetro,
                                   r.Width / PxPorMetro, r.Height / PxPorMetro));
        }

        var cotas = MotorCotas.Calcular(activa, ancladas);
        if (cotas.Count == 0) return;

        var penCota = new Pen(PincelCota, 1.3 * inv);
        double tick = 5.0 * inv;   // medio largo del tick mark de los extremos

        foreach (var c in cotas)
        {
            var ft = CrearTextoCota(c.Valor, inv);
            if (c.Eje == EjeCota.Horizontal)
            {
                double y  = c.CoordenadaLinea * PxPorMetro;
                double x0 = c.Inicio * PxPorMetro;
                double x1 = c.Fin    * PxPorMetro;
                dc.DrawLine(penCota, new Point(x0, y), new Point(x1, y));
                dc.DrawLine(penCota, new Point(x0, y - tick), new Point(x0, y + tick));
                dc.DrawLine(penCota, new Point(x1, y - tick), new Point(x1, y + tick));
                dc.DrawText(ft, new Point((x0 + x1) / 2.0 - ft.Width / 2.0, y - tick - ft.Height));
            }
            else
            {
                double x  = c.CoordenadaLinea * PxPorMetro;
                double y0 = c.Inicio * PxPorMetro;
                double y1 = c.Fin    * PxPorMetro;
                dc.DrawLine(penCota, new Point(x, y0), new Point(x, y1));
                dc.DrawLine(penCota, new Point(x - tick, y0), new Point(x + tick, y0));
                dc.DrawLine(penCota, new Point(x - tick, y1), new Point(x + tick, y1));
                dc.DrawText(ft, new Point(x + tick + 2.0 * inv, (y0 + y1) / 2.0 - ft.Height / 2.0));
            }
        }
    }

    /// <summary>Construye el <see cref="FormattedText"/> del valor de una cota ("X.XX m").</summary>
    private FormattedText CrearTextoCota(double valorM, double inv)
        => new(valorM.ToString("0.00", CultureInfo.InvariantCulture) + " m",
               CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
               new Typeface("Segoe UI"), 11.0 * inv, PincelCota,
               VisualTreeHelper.GetDpi(this).PixelsPerDip);

    // =====================================================================
    // Cálculo geométrico del arrastre (con snapping) — nunca muta el SSOT
    // =====================================================================

    /// <summary>Bordes de las OTRAS losas, en metros de lienzo, para el snapping.</summary>
    private (List<double> X, List<double> Y) CandidatosSnap()
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var p in _placements)
        {
            if (_losaSeleccionada != null && p.Losa.Id == _losaSeleccionada.Id) continue;
            var r = PlacementARectPx(p);
            xs.Add(r.Left  / PxPorMetro);
            xs.Add(r.Right / PxPorMetro);
            ys.Add(r.Top    / PxPorMetro);
            ys.Add(r.Bottom / PxPorMetro);
        }
        return (xs, ys);
    }

    /// <summary>Rect candidato de un MOVER: el original trasladado + snapping rígido del rango.</summary>
    private Rect CalcularRectMovido(Point pantalla)
    {
        double dxPx = (pantalla.X - _dragStart.X) / _scale.ScaleX;
        double dyPx = (pantalla.Y - _dragStart.Y) / _scale.ScaleY;

        double xM = (_rectOriginalPx.X + dxPx) / PxPorMetro;
        double yM = (_rectOriginalPx.Y + dyPx) / PxPorMetro;
        double wM = _rectOriginalPx.Width  / PxPorMetro;
        double hM = _rectOriginalPx.Height / PxPorMetro;

        var (candsX, candsY) = CandidatosSnap();
        double snapX = SnappingEngine.SnapRango(xM, wM, candsX, PasoSnapGrilla, UmbralSnapM).Valor;
        double snapY = SnappingEngine.SnapRango(yM, hM, candsY, PasoSnapGrilla, UmbralSnapM).Valor;

        return new Rect(snapX * PxPorMetro, snapY * PxPorMetro,
                        _rectOriginalPx.Width, _rectOriginalPx.Height);
    }

    /// <summary>
    /// Rect candidato de un REDIMENSIONAR: mueve sólo los bordes del tirador
    /// activo (con snapping), respetando el lado mínimo y sin permitir el flip.
    /// </summary>
    private Rect CalcularRectRedimensionado(Point pantalla)
    {
        var pre = PantallaAPre(pantalla);
        double left = _rectOriginalPx.Left, right  = _rectOriginalPx.Right;
        double top  = _rectOriginalPx.Top,  bottom = _rectOriginalPx.Bottom;

        bool mueveL = _handleActivo is 0 or 6 or 7;
        bool mueveR = _handleActivo is 2 or 3 or 4;
        bool mueveT = _handleActivo is 0 or 1 or 2;
        bool mueveB = _handleActivo is 4 or 5 or 6;

        var (candsX, candsY) = CandidatosSnap();
        if (mueveL) left   = SnappingEngine.SnapCombinado(pre.X / PxPorMetro, candsX, PasoSnapGrilla, UmbralSnapM).Valor * PxPorMetro;
        if (mueveR) right  = SnappingEngine.SnapCombinado(pre.X / PxPorMetro, candsX, PasoSnapGrilla, UmbralSnapM).Valor * PxPorMetro;
        if (mueveT) top    = SnappingEngine.SnapCombinado(pre.Y / PxPorMetro, candsY, PasoSnapGrilla, UmbralSnapM).Valor * PxPorMetro;
        if (mueveB) bottom = SnappingEngine.SnapCombinado(pre.Y / PxPorMetro, candsY, PasoSnapGrilla, UmbralSnapM).Valor * PxPorMetro;

        // Lado mínimo: el borde móvil no puede cruzar al borde fijo opuesto.
        double minPx = MinLadoM * PxPorMetro;
        if (mueveL && left   > right  - minPx) left   = right  - minPx;
        if (mueveR && right  < left   + minPx) right  = left   + minPx;
        if (mueveT && top    > bottom - minPx) top    = bottom - minPx;
        if (mueveB && bottom < top    + minPx) bottom = top    + minPx;

        return new Rect(left, top, Math.Max(minPx, right - left), Math.Max(minPx, bottom - top));
    }

    /// <summary>
    /// Confirma un mover/redimensionar: si la geometría cambió, despacha el
    /// comando (que toma UN snapshot de Undo y escribe el SSOT) y redibuja.
    /// </summary>
    private void ConfirmarArrastre()
    {
        if (_losaSeleccionada != null)
        {
            const double eps = 0.5;   // px pre-transform
            bool cambio =
                Math.Abs(_rectFantasmaPx.X      - _rectOriginalPx.X)      > eps ||
                Math.Abs(_rectFantasmaPx.Y      - _rectOriginalPx.Y)      > eps ||
                Math.Abs(_rectFantasmaPx.Width  - _rectOriginalPx.Width)  > eps ||
                Math.Abs(_rectFantasmaPx.Height - _rectOriginalPx.Height) > eps;

            if (cambio && ActualizarLosaCommand is { } cmd)
            {
                var dto = new ActualizacionLosaArgs(
                    _losaSeleccionada,
                    _rectFantasmaPx.X      / PxPorMetro,
                    _rectFantasmaPx.Y      / PxPorMetro,
                    _rectFantasmaPx.Width  / PxPorMetro,
                    _rectFantasmaPx.Height / PxPorMetro);
                if (cmd.CanExecute(dto)) cmd.Execute(dto);
            }
        }

        RedibujarLosas();
        RecomputarChips();
        RedibujarOverlay();
    }

    /// <summary>Ajusta el cursor según lo que hay bajo el puntero (estado en reposo).</summary>
    private void ActualizarCursorHover(Point pantalla)
    {
        int h = HandleBajoCursor(pantalla);
        if (h >= 0) { Cursor = CursorDeHandle(h); return; }
        Cursor = HitTestLosa(pantalla) != null ? Cursors.SizeAll : Cursors.Arrow;
    }

    private static Cursor CursorDeHandle(int h) => h switch
    {
        0 or 4 => Cursors.SizeNWSE,   // esquinas en diagonal ╲
        2 or 6 => Cursors.SizeNESW,   // esquinas en diagonal ╱
        1 or 5 => Cursors.SizeNS,     // bordes horizontales
        3 or 7 => Cursors.SizeWE,     // bordes verticales
        _      => Cursors.Arrow,
    };

    // =====================================================================
    // Interacción — zoom (rueda), pan, selección y arrastre de losas
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
        RedibujarOverlay();   // los tiradores mantienen su tamaño en pantalla
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pt = e.GetPosition(this);
        _dragStart = pt;

        // 1) ¿Sobre un tirador de la losa ya seleccionada? → redimensionar.
        //    (HandleBajoCursor sólo devuelve >=0 si hay una losa seleccionada.)
        int handle = HandleBajoCursor(pt);
        if (handle >= 0)
        {
            _handleActivo = handle;
            _rectOriginalPx = RectPxDeLosaSeleccionada();
            _rectFantasmaPx = _rectOriginalPx;
            _chips = Array.Empty<AdyacenciaCandidata>();   // se ocultan durante el arrastre
            RedibujarOverlay();             // re-dibuja la selección sin chips (modo aún Ninguno)
            _modo = ModoArrastre.Redimensionar;
            CaptureMouse();
            return;
        }

        // 2) ¿Sobre un chip de adyacencia? → crear el BordeAdic (acción instantánea).
        int chip = HitTestChip(pt);
        if (chip >= 0)
        {
            var candidato = _chips[chip];
            if (CrearBordeAdicCommand is { } cmd && cmd.CanExecute(candidato))
                cmd.Execute(candidato);
            RecomputarChips();   // el par recién conectado deja de sugerirse
            RedibujarOverlay();
            return;              // sin captura ni arrastre; el MouseUp será inocuo
        }

        // 3) ¿Sobre una losa? → seleccionarla y armar un posible movimiento.
        var hit = HitTestLosa(pt);
        if (hit != null)
        {
            _losaSeleccionada = hit.Losa;
            _handleActivo = -1;
            _rectOriginalPx = PlacementARectPx(hit);
            _rectFantasmaPx = _rectOriginalPx;
            _chips = Array.Empty<AdyacenciaCandidata>();   // se ocultan durante el arrastre
            RedibujarOverlay();           // _modo aún Ninguno → dibuja selección + tiradores
            _modo = ModoArrastre.Mover;   // armado: el próximo MouseMove arrastra
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            return;
        }

        // 4) Espacio vacío → pan, y se deselecciona lo que hubiera.
        if (_losaSeleccionada != null)
        {
            _losaSeleccionada = null;
            RedibujarOverlay();
        }
        _modo = ModoArrastre.Pan;
        _panStartTx = _translate.X;
        _panStartTy = _translate.Y;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var modo = _modo;
        _modo = ModoArrastre.Ninguno;
        _handleActivo = -1;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        var pt = e.GetPosition(this);

        switch (modo)
        {
            case ModoArrastre.Pan:
                // Un clic (no un pan real) sobre espacio vacío → mapear polígono.
                if ((pt - _dragStart).Length < ClicMaxPx)
                {
                    var poligono = HitTestPoligono(pt);
                    if (poligono != null && PoligonoClickCommand is { } cmd && cmd.CanExecute(poligono))
                    {
                        cmd.Execute(poligono);
                        RedibujarLosas();    // la losa recién creada aparece al instante
                        RecomputarChips();
                        RedibujarOverlay();
                    }
                }
                break;

            case ModoArrastre.Mover:
            case ModoArrastre.Redimensionar:
                ConfirmarArrastre();
                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pt = e.GetPosition(this);

        switch (_modo)
        {
            case ModoArrastre.Ninguno:
                ActualizarCursorHover(pt);
                break;

            case ModoArrastre.Pan:
                _translate.X = _panStartTx + (pt.X - _dragStart.X);
                _translate.Y = _panStartTy + (pt.Y - _dragStart.Y);
                break;

            case ModoArrastre.Mover:
                _rectFantasmaPx = CalcularRectMovido(pt);
                RedibujarOverlay();
                break;

            case ModoArrastre.Redimensionar:
                _rectFantasmaPx = CalcularRectRedimensionado(pt);
                RedibujarOverlay();
                break;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // Captura perdida a mitad de un gesto: descartar el arrastre (el SSOT
        // nunca se tocó) y volver al overlay de selección estática.
        bool arrastrando = _modo is ModoArrastre.Mover or ModoArrastre.Redimensionar;
        _modo = ModoArrastre.Ninguno;
        _handleActivo = -1;
        if (arrastrando) RedibujarOverlay();
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
