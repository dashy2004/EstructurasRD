using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Services;
using LosasPlus.ViewModels;

namespace LosasPlus.Views.Cad;

/// <summary>
/// Host de renderizado del editor CAD — <b>port a Avalonia (Fase E.1: render)</b>.
///
/// <para>
/// WPF usaba <b>retained-mode</b>: heredaba de <c>FrameworkElement</c> y mantenía
/// 4 <c>DrawingVisual</c> (grilla, plano, losas, overlay) con un
/// <c>TransformGroup</c> compartido para zoom/pan. Avalonia usa <b>render
/// inmediato</b>: este control hereda de <see cref="Control"/> y dibuja TODO en
/// <see cref="Render"/> bajo un único <see cref="Matrix"/> de zoom/pan;
/// <see cref="Visual.InvalidateVisual"/> reemplaza al rebuild de los visuales.
/// </para>
///
/// <para>
/// <b>E.1 porta el render</b> (grilla métrica + ejes, plano DXF, PDF underlay,
/// losas con patrón/rótulo/marcas de acero, muros), el zoom-to-cursor, el pan,
/// el encuadre y la captura PNG. La <b>interacción de edición</b> (mover/
/// redimensionar/dibujar losas y muros, snap, calibración de PDF, chips de
/// adyacencia, editores flotantes) se porta en la Fase E.2; las propiedades de
/// comando y los eventos ya están declarados para no romper los bindings.
/// </para>
/// </summary>
public sealed class CadCanvasHost : Control
{
    // ---- Constantes de render / interacción ----
    private const double PxPorMetro = 50.0;
    private const double MinScale   = 0.05;
    private const double MaxScale   = 20.0;
    private const double ZoomStep   = 1.15;

    // ---- Constantes de interacción (Fase E.2) ----
    private const double ClicMaxPx        = 5.0;    // px — umbral clic vs. arrastre
    private const double PasoSnapGrilla   = 1.0;    // m  — la grilla métrica es de 1 m
    private const double UmbralSnapM      = 0.20;   // m  — radio de enganche del snap
    private const double MinLadoM         = 0.10;   // m  — lado mínimo de una losa
    private const double HandleLadoPx     = 9.0;    // px — tamaño del tirador en pantalla
    private const double ChipRadioPx      = 11.0;   // px — radio del chip de adyacencia
    private const double MinLadoDibujoM   = 0.5;    // m  — lado mínimo de una losa dibujada
    private const double MinLongitudMuroM = 0.30;   // m  — longitud mínima de un muro

    // ---- Transform compartido de zoom/pan (antes ScaleTransform+TranslateTransform) ----
    private double _scaleX = 1, _scaleY = 1;
    private double _tx, _ty;

    // ---- Pinceles del overlay (E.2) ----
    private static readonly SolidColorBrush PincelOverlay        = new(Color.FromRgb(0x1E, 0x88, 0xE5));
    private static readonly SolidColorBrush PincelOverlayRelleno = new(Color.FromArgb(48, 0x1E, 0x88, 0xE5));
    private static readonly SolidColorBrush PincelCota           = new(Color.FromRgb(0xEF, 0x6C, 0x00));

    // ---- Estado de render cacheado ----
    private IReadOnlyList<LayoutSolver.Placement> _placements = Array.Empty<LayoutSolver.Placement>();
    private double _offsetXPx;

    // ---- Cache del LayoutResult (perf) ----
    // LayoutSolver.Solve es BFS+LINQ con allocs; antes corría en CADA Render
    // (pan/zoom/drag/mouse-move → decenas de veces/seg) generando presión de GC y
    // caída de FPS. El layout SÓLO depende de la topología del sistema (losas +
    // sus dimensiones/posiciones + bordes); los cambios de pura vista (pan/zoom)
    // no la alteran. Cacheamos por (instancia de Sistema + RevisionSistema + hash
    // de topología) y reusamos el resultado mientras la topología no cambie.
    //
    // El hash de topología (no sólo RevisionSistema) cubre también las mutaciones
    // in-canvas que reescriben el SSOT sin incrementar la revisión (crear/mover/
    // redimensionar losa, crear borde) y se confían a RefrescarLosas() para
    // repintar — así el cache nunca sirve un layout obsoleto.
    private LayoutSolver.LayoutResult? _layoutCache;
    private Sistema? _layoutCacheSistema;
    private int _layoutCacheRevision = int.MinValue;
    private int _layoutCacheTopologia;

    // ---- Estado de interacción (efímero, sólo de la vista — nunca toca el SSOT) ----
    private enum ModoArrastre { Ninguno, Pan, Mover, Redimensionar, DibujarLosa, DibujarMuro }
    private ModoArrastre _modo = ModoArrastre.Ninguno;
    private Losa? _losaSeleccionada;
    private Point _dragStart;
    private double _panStartTx, _panStartTy;
    private int _handleActivo = -1;
    private Rect _rectOriginalPx;
    private Rect _rectFantasmaPx;

    // ---- Herramientas de dibujo (losa / muro) ----
    private Point _dibujarAnclaPre;   // ancla del trazo de losa (px pre-transform)
    private Point _muroAnclaPre;      // P₁ del muro (px pre-transform)
    private Point _muroFinPre;        // P₂ actual del muro (px pre-transform)

    // ---- Chips de adyacencia — sugerencias de conexión en el overlay ----
    private IReadOnlyList<AdyacenciaCandidata> _chips = Array.Empty<AdyacenciaCandidata>();

    // ---- Snap excluyente y «Mover Conectadas» (BFS sobre el grafo de bordes) ----
    private readonly HashSet<int> _idsExcluidosSnap = new();
    private bool _grupoMoverActivo;
    private readonly Dictionary<int, Rect> _grupoMoverOrigPx = new();
    private readonly List<Rect> _grupoMoverFantasmasPx = new();

    // ---- Calibración interactiva del PDF (P₁/P₂ en px pre-transform) ----
    private Point? _calibrarP1Pre;
    private Point? _calibrarP2Pre;
    private bool _calibrarConfirmando;

    // ---- Captura por-puntero (Avalonia captura por IPointer, no globalmente) ----
    private IPointer? _punteroCapturado;
    private bool EstaCapturado => _punteroCapturado is not null;

    // ---- Cursores cacheados (Avalonia instancia un Cursor por objeto) ----
    private static readonly Cursor CursorArrow   = new(StandardCursorType.Arrow);
    private static readonly Cursor CursorHand    = new(StandardCursorType.Hand);
    private static readonly Cursor CursorCross   = new(StandardCursorType.Cross);
    private static readonly Cursor CursorSizeAll = new(StandardCursorType.SizeAll);
    private static readonly Cursor CursorNS      = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor CursorWE      = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor CursorNWSE    = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor CursorNESW    = new(StandardCursorType.TopRightCorner);

    // ---- Debounce de re-rasterización del PDF tras un zoom ----
    private readonly DispatcherTimer _debounceReraster;

    public CadCanvasHost()
    {
        Focusable = true;
        ClipToBounds = true;

        _debounceReraster = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounceReraster.Tick += (_, _) => EvaluarReRasterizado();
    }

    // =====================================================================
    // Eventos (los dispara la interacción de la Fase E.2; declarados ya para
    // que CadView se suscriba sin romper la compilación)
    // =====================================================================

    /// <summary>Doble clic sobre una losa (modo Puntero) — lo escucha CadView.</summary>
    public event EventHandler<LosaDobleClicEventArgs>? LosaDobleClicada;

    /// <summary>Segundo punto de calibración del PDF fijado — lo escucha CadView.</summary>
    public event EventHandler<CalibrarPdfPuntosListosEventArgs>? CalibrarPdfPuntosListos;

    // =====================================================================
    // StyledProperties — datos a dibujar y comandos (binding desde el VM)
    // =====================================================================

    public static readonly StyledProperty<PlanoReferencia?> PlanoProperty =
        AvaloniaProperty.Register<CadCanvasHost, PlanoReferencia?>(nameof(Plano));
    public PlanoReferencia? Plano { get => GetValue(PlanoProperty); set => SetValue(PlanoProperty, value); }

    public static readonly StyledProperty<Sistema?> SistemaProperty =
        AvaloniaProperty.Register<CadCanvasHost, Sistema?>(nameof(Sistema));
    public Sistema? Sistema { get => GetValue(SistemaProperty); set => SetValue(SistemaProperty, value); }

    public static readonly StyledProperty<PdfReferencia?> PdfProperty =
        AvaloniaProperty.Register<CadCanvasHost, PdfReferencia?>(nameof(Pdf));
    public PdfReferencia? Pdf { get => GetValue(PdfProperty); set => SetValue(PdfProperty, value); }

    public static readonly StyledProperty<Bitmap?> FondoPdfProperty =
        AvaloniaProperty.Register<CadCanvasHost, Bitmap?>(nameof(FondoPdf));
    public Bitmap? FondoPdf { get => GetValue(FondoPdfProperty); set => SetValue(FondoPdfProperty, value); }

    public static readonly StyledProperty<double> OpacidadPdfProperty =
        AvaloniaProperty.Register<CadCanvasHost, double>(nameof(OpacidadPdf), 0.6);
    public double OpacidadPdf { get => GetValue(OpacidadPdfProperty); set => SetValue(OpacidadPdfProperty, value); }

    public static readonly StyledProperty<int> RevisionPlanoProperty =
        AvaloniaProperty.Register<CadCanvasHost, int>(nameof(RevisionPlano));
    public int RevisionPlano { get => GetValue(RevisionPlanoProperty); set => SetValue(RevisionPlanoProperty, value); }

    public static readonly StyledProperty<int> RevisionSistemaProperty =
        AvaloniaProperty.Register<CadCanvasHost, int>(nameof(RevisionSistema));
    public int RevisionSistema { get => GetValue(RevisionSistemaProperty); set => SetValue(RevisionSistemaProperty, value); }

    public static readonly StyledProperty<int> RevisionPdfProperty =
        AvaloniaProperty.Register<CadCanvasHost, int>(nameof(RevisionPdf));
    public int RevisionPdf { get => GetValue(RevisionPdfProperty); set => SetValue(RevisionPdfProperty, value); }

    public static readonly StyledProperty<int> SolicitudEncuadreProperty =
        AvaloniaProperty.Register<CadCanvasHost, int>(nameof(SolicitudEncuadre));
    public int SolicitudEncuadre { get => GetValue(SolicitudEncuadreProperty); set => SetValue(SolicitudEncuadreProperty, value); }

    public static readonly StyledProperty<int> SolicitudEncuadrePdfProperty =
        AvaloniaProperty.Register<CadCanvasHost, int>(nameof(SolicitudEncuadrePdf));
    public int SolicitudEncuadrePdf { get => GetValue(SolicitudEncuadrePdfProperty); set => SetValue(SolicitudEncuadrePdfProperty, value); }

    public static readonly StyledProperty<ModoInteraccionCad> ModoInteraccionProperty =
        AvaloniaProperty.Register<CadCanvasHost, ModoInteraccionCad>(nameof(ModoInteraccion),
            ModoInteraccionCad.Puntero, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public ModoInteraccionCad ModoInteraccion { get => GetValue(ModoInteraccionProperty); set => SetValue(ModoInteraccionProperty, value); }

    public static readonly StyledProperty<bool> ModoCalibrarPdfProperty =
        AvaloniaProperty.Register<CadCanvasHost, bool>(nameof(ModoCalibrarPdf),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public bool ModoCalibrarPdf { get => GetValue(ModoCalibrarPdfProperty); set => SetValue(ModoCalibrarPdfProperty, value); }

    public static readonly StyledProperty<int> MuroSeleccionadoIdProperty =
        AvaloniaProperty.Register<CadCanvasHost, int>(nameof(MuroSeleccionadoId), -1);
    public int MuroSeleccionadoId { get => GetValue(MuroSeleccionadoIdProperty); set => SetValue(MuroSeleccionadoIdProperty, value); }

    public static readonly StyledProperty<bool> SnapActivoProperty =
        AvaloniaProperty.Register<CadCanvasHost, bool>(nameof(SnapActivo), true);
    public bool SnapActivo { get => GetValue(SnapActivoProperty); set => SetValue(SnapActivoProperty, value); }

    public static readonly StyledProperty<bool> MoverConectadasProperty =
        AvaloniaProperty.Register<CadCanvasHost, bool>(nameof(MoverConectadas));
    public bool MoverConectadas { get => GetValue(MoverConectadasProperty); set => SetValue(MoverConectadasProperty, value); }

    // ---- Comandos (parámetro/ejecución cableados en E.2) ----
    public static readonly StyledProperty<System.Windows.Input.ICommand?> PoligonoClickCommandProperty =
        AvaloniaProperty.Register<CadCanvasHost, System.Windows.Input.ICommand?>(nameof(PoligonoClickCommand));
    public System.Windows.Input.ICommand? PoligonoClickCommand { get => GetValue(PoligonoClickCommandProperty); set => SetValue(PoligonoClickCommandProperty, value); }

    public static readonly StyledProperty<System.Windows.Input.ICommand?> ActualizarLosaCommandProperty =
        AvaloniaProperty.Register<CadCanvasHost, System.Windows.Input.ICommand?>(nameof(ActualizarLosaCommand));
    public System.Windows.Input.ICommand? ActualizarLosaCommand { get => GetValue(ActualizarLosaCommandProperty); set => SetValue(ActualizarLosaCommandProperty, value); }

    public static readonly StyledProperty<System.Windows.Input.ICommand?> CrearBordeAdicCommandProperty =
        AvaloniaProperty.Register<CadCanvasHost, System.Windows.Input.ICommand?>(nameof(CrearBordeAdicCommand));
    public System.Windows.Input.ICommand? CrearBordeAdicCommand { get => GetValue(CrearBordeAdicCommandProperty); set => SetValue(CrearBordeAdicCommandProperty, value); }

    public static readonly StyledProperty<System.Windows.Input.ICommand?> CrearLosaCommandProperty =
        AvaloniaProperty.Register<CadCanvasHost, System.Windows.Input.ICommand?>(nameof(CrearLosaCommand));
    public System.Windows.Input.ICommand? CrearLosaCommand { get => GetValue(CrearLosaCommandProperty); set => SetValue(CrearLosaCommandProperty, value); }

    public static readonly StyledProperty<System.Windows.Input.ICommand?> CrearMuroCommandProperty =
        AvaloniaProperty.Register<CadCanvasHost, System.Windows.Input.ICommand?>(nameof(CrearMuroCommand));
    public System.Windows.Input.ICommand? CrearMuroCommand { get => GetValue(CrearMuroCommandProperty); set => SetValue(CrearMuroCommandProperty, value); }

    public static readonly StyledProperty<System.Windows.Input.ICommand?> MoverGrupoCommandProperty =
        AvaloniaProperty.Register<CadCanvasHost, System.Windows.Input.ICommand?>(nameof(MoverGrupoCommand));
    public System.Windows.Input.ICommand? MoverGrupoCommand { get => GetValue(MoverGrupoCommandProperty); set => SetValue(MoverGrupoCommandProperty, value); }

    public static readonly StyledProperty<System.Windows.Input.ICommand?> ReRasterizarPdfCommandProperty =
        AvaloniaProperty.Register<CadCanvasHost, System.Windows.Input.ICommand?>(nameof(ReRasterizarPdfCommand));
    public System.Windows.Input.ICommand? ReRasterizarPdfCommand { get => GetValue(ReRasterizarPdfCommandProperty); set => SetValue(ReRasterizarPdfCommandProperty, value); }

    public static readonly StyledProperty<System.Windows.Input.ICommand?> CancelarCalibrarPdfCommandProperty =
        AvaloniaProperty.Register<CadCanvasHost, System.Windows.Input.ICommand?>(nameof(CancelarCalibrarPdfCommand));
    public System.Windows.Input.ICommand? CancelarCalibrarPdfCommand { get => GetValue(CancelarCalibrarPdfCommandProperty); set => SetValue(CancelarCalibrarPdfCommandProperty, value); }

    /// <summary>
    /// Reacciona a los cambios de propiedad. Las propiedades de datos invalidan
    /// el render; las de "solicitud de encuadre" disparan el zoom-to-fit; un
    /// cambio de tamaño (Bounds) re-dibuja la grilla.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        var p = change.Property;
        if (p == PlanoProperty || p == SistemaProperty || p == PdfProperty || p == FondoPdfProperty
            || p == OpacidadPdfProperty || p == RevisionPlanoProperty || p == RevisionSistemaProperty
            || p == RevisionPdfProperty || p == MuroSeleccionadoIdProperty || p == BoundsProperty)
        {
            // Un cambio del sistema (o su revisión) puede crear/quitar adyacencias:
            // recomputar los chips antes de redibujar el overlay.
            if (p == SistemaProperty || p == RevisionSistemaProperty) RecomputarChips();
            InvalidateVisual();
        }
        else if (p == SolicitudEncuadreProperty) EncuadrarPlano();
        else if (p == SolicitudEncuadrePdfProperty) EncuadrarPdf();
    }

    // =====================================================================
    // Render — dibuja las 3 capas bajo el transform de zoom/pan
    // =====================================================================

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Fondo transparente sobre todo el Bounds: garantiza hit-test (zoom/pan)
        // en las zonas vacías (en WPF lo daba el VisualCollection que cubría el área).
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        var m = Matrix.CreateScale(_scaleX, _scaleY) * Matrix.CreateTranslation(_tx, _ty);
        using (context.PushTransform(m))
        {
            // El render dibuja BAJO el transform de zoom/pan (PushTransform), así
            // que dentro del bloque las coordenadas son PRE-transform (px del
            // lienzo, sin zoom/pan). Por eso TransformPlano() se construye con
            // scaleX/scaleY=1 y tx/ty=0: el zoom/pan ya lo aplica la matriz «m».
            // El hit-test, en cambio, parte de coordenadas de pantalla y debe
            // deshacer también el zoom/pan → usa TransformPlano(incluirZoomPan: true).
            DibujarGrilla(context);
            DibujarPlano(context);
            DibujarLosas(context);
            // Capa 3 — overlay efímero (selección, tiradores, fantasma, chips, cotas,
            // calibración). En WPF era un DrawingVisual aparte; aquí va al final del
            // mismo Render bajo el mismo transform → las constantes /_scaleX siguen
            // dando tamaños constantes en pantalla.
            DibujarOverlay(context);
        }
    }

    /// <summary>
    /// Construye la transformación mundo↔pantalla del plano DXF para el estado
    /// actual. Único punto que conoce la matemática de coordenadas del plano;
    /// el render y el hit-test la comparten para que un clic mapee al mismo
    /// punto-mundo que se dibujó (correcto con Escala/Offset del ajuste espacial).
    ///
    /// <para>
    /// <paramref name="incluirZoomPan"/> = <c>false</c> (render): las primitivas
    /// se dibujan dentro de <c>PushTransform</c>, que ya aplica el zoom/pan, así
    /// que la transformación sólo cubre esc/offset/flip-Y/Px (scale=1, tx=ty=0).
    /// <c>true</c> (hit-test): partimos de coordenadas de pantalla crudas, así
    /// que la transformación también deshace el zoom/pan.
    /// </para>
    /// </summary>
    private CadTransform TransformPlano(bool incluirZoomPan)
    {
        var plano = Plano;
        double esc = plano?.Escala ?? 1.0;
        double offX = plano?.OffsetX ?? 0.0;
        double offY = plano?.OffsetY ?? 0.0;
        double maxY = plano?.MaxY ?? 0.0;
        double sx = incluirZoomPan ? _scaleX : 1.0;
        double sy = incluirZoomPan ? _scaleY : 1.0;
        double tx = incluirZoomPan ? _tx : 0.0;
        double ty = incluirZoomPan ? _ty : 0.0;
        return new CadTransform(PxPorMetro, esc, offX, offY, maxY, sx, sy, tx, ty);
    }

    // =====================================================================
    // Encuadre del plano / PDF y captura de imagen — API pública
    // =====================================================================

    /// <summary>«Zoom to fit» del plano DXF.</summary>
    public void EncuadrarPlano()
    {
        var plano = Plano;
        if (plano is null || plano.EstaVacio) return;

        double w = Bounds.Width, h = Bounds.Height;
        if (w < 1 || h < 1) return;

        // Bbox del plano en px PRE-zoom/pan, vía la MISMA transformación del
        // render (esquinas mundo sup-izq=(MinX,MaxY) / inf-der=(MaxX,MinY)).
        var tf = TransformPlano(incluirZoomPan: false);
        var supIzq = tf.MundoAPantalla(new PuntoCad(plano.MinX, plano.MaxY));
        var infDer = tf.MundoAPantalla(new PuntoCad(plano.MaxX, plano.MinY));
        double bw = infDer.X - supIzq.X;
        double bh = infDer.Y - supIzq.Y;
        if (bw < 1e-6 || bh < 1e-6) return;

        const double margen = 0.92;
        double fit = Math.Clamp(Math.Min(w / bw, h / bh) * margen, MinScale, MaxScale);

        double cx = supIzq.X + bw / 2.0;
        double cy = supIzq.Y + bh / 2.0;
        _scaleX = _scaleY = fit;
        _tx = w / 2.0 - fit * cx;
        _ty = h / 2.0 - fit * cy;

        InvalidateVisual();
        ReiniciarDebounceReraster();
    }

    /// <summary>«Zoom to fit» del PDF underlay.</summary>
    public void EncuadrarPdf()
    {
        var pdf = Pdf;
        if (pdf is null || pdf.EstaVacio) return;

        double w = Bounds.Width, h = Bounds.Height;
        if (w < 1 || h < 1) return;

        double esc = pdf.Escala;
        double bx = pdf.OffsetX * PxPorMetro;
        double by = pdf.OffsetY * PxPorMetro;
        double bw = pdf.Ancho * esc * PxPorMetro;
        double bh = pdf.Alto  * esc * PxPorMetro;
        if (bw < 1e-6 || bh < 1e-6) return;

        const double margen = 0.92;
        double fit = Math.Clamp(Math.Min(w / bw, h / bh) * margen, MinScale, MaxScale);

        double cx = bx + bw / 2.0;
        double cy = by + bh / 2.0;
        _scaleX = _scaleY = fit;
        _tx = w / 2.0 - fit * cx;
        _ty = h / 2.0 - fit * cy;

        InvalidateVisual();
        ReiniciarDebounceReraster();
    }

    /// <summary>
    /// Captura el lienzo CAD completo como imagen, para incrustarla en la
    /// exportación a Excel. Encuadra el plano para componer la toma, renderiza
    /// a un <see cref="RenderTargetBitmap"/> y restaura el zoom/pan previo.
    /// Port a Avalonia: WPF RenderTargetBitmap(dpi,Pbgra32)+Render(this) →
    /// Avalonia RenderTargetBitmap(PixelSize, dpi-Vector)+Render(this).
    /// </summary>
    public Bitmap CaptureCanvasPng()
    {
        double w = Bounds.Width  >= 1 ? Bounds.Width  : 1200;
        double h = Bounds.Height >= 1 ? Bounds.Height : 800;

        Measure(new Size(w, h));
        Arrange(new Rect(0, 0, w, h));

        double sx = _scaleX, sy = _scaleY, tx = _tx, ty = _ty;
        EncuadrarPlano();

        var rtb = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(w), (int)Math.Ceiling(h)), new Vector(96, 96));
        rtb.Render(this);

        _scaleX = sx; _scaleY = sy; _tx = tx; _ty = ty;
        InvalidateVisual();
        return rtb;
    }

    /// <summary>
    /// Re-renderiza la Capa 2 (losas) y el overlay. La invoca CadView tras
    /// confirmar la edición in-canvas. Port a Avalonia: recomputar chips +
    /// InvalidateVisual() (el render rehace el layout y dibuja el overlay).
    /// </summary>
    public void RefrescarLosas() { InvalidarLayout(); RecomputarChips(); InvalidateVisual(); }

    // =====================================================================
    // Capa 0 — Grilla métrica
    // =====================================================================

    private void DibujarGrilla(DrawingContext dc)
    {
        double w = Math.Max(Bounds.Width, 200);
        double h = Math.Max(Bounds.Height, 200);
        var penFino   = new Pen(new SolidColorBrush(Color.FromArgb(40, 120, 120, 120)), 0.5);
        var penMetro5 = new Pen(new SolidColorBrush(Color.FromArgb(80, 120, 120, 120)), 0.8);

        // Grilla en función del viewport visible (antes era ±40 m fijos: se
        // recortaba al panear lejos y dibujaba de más al acercar el zoom).
        // El render corre dentro de PushTransform → trabajamos en px PRE-zoom/pan;
        // invertimos el zoom/pan para conocer el rango visible y dibujamos sólo
        // las líneas dentro de él (más un metro de margen para los bordes).
        double sx = _scaleX > 1e-9 ? _scaleX : 1.0;
        double sy = _scaleY > 1e-9 ? _scaleY : 1.0;
        double preLeft   = (0 - _tx) / sx;
        double preRight  = (w - _tx) / sx;
        double preTop    = (0 - _ty) / sy;
        double preBottom = (h - _ty) / sy;

        int xmMin = (int)Math.Floor(preLeft   / PxPorMetro) - 1;
        int xmMax = (int)Math.Ceiling(preRight  / PxPorMetro) + 1;
        int ymMin = (int)Math.Floor(preTop    / PxPorMetro) - 1;
        int ymMax = (int)Math.Ceiling(preBottom / PxPorMetro) + 1;

        // Tope de seguridad: en un zoom-out extremo el rango podría ser enorme.
        // Limitamos el conteo de líneas para no degradar el frame.
        const int maxLineas = 600;
        if (xmMax - xmMin > maxLineas) { int c = (xmMin + xmMax) / 2; xmMin = c - maxLineas / 2; xmMax = c + maxLineas / 2; }
        if (ymMax - ymMin > maxLineas) { int c = (ymMin + ymMax) / 2; ymMin = c - maxLineas / 2; ymMax = c + maxLineas / 2; }

        double yTop = ymMin * PxPorMetro, yBot = ymMax * PxPorMetro;
        for (int xm = xmMin; xm <= xmMax; xm++)
        {
            double x = xm * PxPorMetro;
            var pen = xm % 5 == 0 ? penMetro5 : penFino;
            dc.DrawLine(pen, new Point(x, yTop), new Point(x, yBot));
        }
        double xLeft = xmMin * PxPorMetro, xRight = xmMax * PxPorMetro;
        for (int ym = ymMin; ym <= ymMax; ym++)
        {
            double y = ym * PxPorMetro;
            var pen = ym % 5 == 0 ? penMetro5 : penFino;
            dc.DrawLine(pen, new Point(xLeft, y), new Point(xRight, y));
        }

        // Ejes del origen (0,0) — se extienden por todo el viewport visible.
        var penEjeX = new Pen(new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)), 1.6);
        var penEjeY = new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), 1.6);
        dc.DrawLine(penEjeX, new Point(xLeft, 0), new Point(xRight, 0));
        dc.DrawLine(penEjeY, new Point(0, yTop), new Point(0, yBot));

        var pincelOrigen = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        dc.DrawEllipse(pincelOrigen, null, new Point(0, 0), 3.0, 3.0);

        // Bounding box del plano DXF.
        if (Plano is { EstaVacio: false } plano)
        {
            var penBBox = new Pen(new SolidColorBrush(Color.FromArgb(120, 0x33, 0x66, 0x99)), 1.0)
            {
                DashStyle = new DashStyle(new double[] { 6, 4 }, 0),
            };
            // Misma transformación que el render del plano: las esquinas mundo
            // (MinX,MaxY)→sup-izq y (MaxX,MinY)→inf-der tras el flip-Y.
            var tf = TransformPlano(incluirZoomPan: false);
            var supIzq = tf.MundoAPantalla(new PuntoCad(plano.MinX, plano.MaxY));
            var infDer = tf.MundoAPantalla(new PuntoCad(plano.MaxX, plano.MinY));
            var bbox = new Rect(supIzq, infDer);
            dc.DrawRectangle(null, penBBox, bbox);
        }
    }

    // =====================================================================
    // Capa 1 — PDF underlay + plano DXF
    // =====================================================================

    private void DibujarPlano(DrawingContext dc)
    {
        // ---- 1a: PDF underlay rasterizado (debajo del DXF) ----
        if (Pdf is { EstaVacio: false } pdf && FondoPdf is { } imgPdf)
        {
            double escPdf = pdf.Escala;
            var rectPdfPx = new Rect(
                pdf.OffsetX * PxPorMetro,
                pdf.OffsetY * PxPorMetro,
                pdf.Ancho * escPdf * PxPorMetro,
                pdf.Alto  * escPdf * PxPorMetro);

            double op = Math.Clamp(OpacidadPdf, 0.0, 1.0);
            using (dc.PushOpacity(op))
                dc.DrawImage(imgPdf, rectPdfPx);
        }

        // ---- 1b: entidades vectoriales del DXF ----
        var plano = Plano;
        if (plano is null || plano.EstaVacio) return;

        double esc = plano.Escala;
        // Render bajo PushTransform → la transformación no incluye zoom/pan.
        var tf = TransformPlano(incluirZoomPan: false);
        Point ToPx(PuntoCad p) => tf.MundoAPantalla(p);

        var penPlano = new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x99)), 1.2);
        var brushTexto = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

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
                        gc.BeginFigure(ToPx(poli.Vertices[0]), isFilled: false);
                        for (int i = 1; i < poli.Vertices.Count; i++)
                            gc.LineTo(ToPx(poli.Vertices[i]));
                        gc.EndFigure(poli.Cerrada);
                    }
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
                        Math.Max(8, t.Altura * esc * PxPorMetro),
                        brushTexto);
                    dc.DrawText(ft, ToPx(t.Posicion));
                    break;
                }

                case ArcoCad a when a.Radio > 0:
                {
                    var centro = ToPx(a.Centro);
                    double rPx = a.Radio * esc * PxPorMetro;
                    if (a.EsCirculoCompleto)
                        dc.DrawEllipse(null, penPlano, centro, rPx, rPx);
                    else
                    {
                        var geo = ArcoAGeometria(a, ToPx, esc);
                        if (geo != null) dc.DrawGeometry(null, penPlano, geo);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>Construye la geometría de un arco parcial (no círculo completo).</summary>
    private static Geometry? ArcoAGeometria(ArcoCad a, Func<PuntoCad, Point> toPx, double esc)
    {
        double rPx = a.Radio * esc * PxPorMetro;
        double i = a.AnguloInicioGrados * Math.PI / 180.0;
        double f = a.AnguloFinGrados * Math.PI / 180.0;
        var pIni = new PuntoCad(a.Centro.X + a.Radio * Math.Cos(i), a.Centro.Y + a.Radio * Math.Sin(i));
        var pFin = new PuntoCad(a.Centro.X + a.Radio * Math.Cos(f), a.Centro.Y + a.Radio * Math.Sin(f));
        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(toPx(pIni), isFilled: false);
            double barrido = a.AnguloFinGrados - a.AnguloInicioGrados;
            gc.ArcTo(toPx(pFin), new Size(rPx, rPx), 0,
                isLargeArc: Math.Abs(barrido) > 180,
                SweepDirection.CounterClockwise);
            gc.EndFigure(false);
        }
        return geo;
    }

    // =====================================================================
    // Capa 2 — Losas (posicionadas por LayoutSolver) + muros
    // =====================================================================

    private void DibujarLosas(DrawingContext dc)
    {
        _placements = Array.Empty<LayoutSolver.Placement>();
        _offsetXPx = 0;
        var sistema = Sistema;
        if (sistema is null) return;
        if (sistema.Losas.Count == 0) { DibujarMuros(dc, sistema); return; }

        var layout = ObtenerLayout(sistema);
        if (layout is null) { DibujarMuros(dc, sistema); return; }

        var rellenoLosa  = new SolidColorBrush(Color.FromArgb(45, 0x2E, 0x7D, 0x32));
        var rellenoHuerf = new SolidColorBrush(Color.FromArgb(45, 0xC1, 0x8A, 0x2C));
        var penLosa  = new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), 2.0);
        var penHuerf = new Pen(new SolidColorBrush(Color.FromRgb(0xC1, 0x8A, 0x2C)), 1.5);
        var brushId  = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1D));
        var penPatron = new Pen(new SolidColorBrush(Color.FromArgb(70, 0x2E, 0x7D, 0x32)), 0.8);
        var penAcero  = new Pen(new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F)), 2.2);

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

            DibujarPatronLosa(dc, rect, p.Losa, penPatron);
            DibujarRotuloLosa(dc, rect, p, brushId);
        }

        DibujarMarcasAcero(dc, layout.Placements, offsetX, sistema, penAcero);
        _placements = layout.Placements;

        DibujarMuros(dc, sistema);
    }

    /// <summary>
    /// Devuelve el <see cref="LayoutSolver.LayoutResult"/> del sistema, cacheado
    /// mientras la topología no cambie. La clave es (instancia de
    /// <see cref="Sistema"/> + <see cref="RevisionSistema"/> + hash de topología);
    /// los cambios de pura vista (pan/zoom) la dejan intacta y reusan el resultado
    /// sin reejecutar la BFS+LINQ del solver en cada frame. Devuelve <c>null</c>
    /// si el solver lanza (mismo fallback que antes: dibujar sólo muros).
    /// </summary>
    private LayoutSolver.LayoutResult? ObtenerLayout(Sistema sistema)
    {
        int topo = HashTopologia(sistema);
        if (_layoutCache is not null
            && ReferenceEquals(_layoutCacheSistema, sistema)
            && _layoutCacheRevision == RevisionSistema
            && _layoutCacheTopologia == topo)
            return _layoutCache;

        try { _layoutCache = LayoutSolver.Solve(sistema); }
        catch { _layoutCache = null; }

        _layoutCacheSistema = sistema;
        _layoutCacheRevision = RevisionSistema;
        _layoutCacheTopologia = topo;
        return _layoutCache;
    }

    /// <summary>
    /// Clave de invalidación del caché de layout — delega en
    /// <see cref="TopologiaPlanta.Hash"/> (UI1.1, fuente única): observa
    /// CoordenadaX/Y + Anclada, así un movimiento hecho en Planta 2D también
    /// invalida el layout del lienzo CAD (jamás se sirve un layout obsoleto).
    /// </summary>
    private static int HashTopologia(Sistema sistema) => TopologiaPlanta.Hash(sistema);

    /// <summary>Invalida el layout cacheado — fuerza un recálculo en el próximo render.</summary>
    private void InvalidarLayout()
    {
        _layoutCache = null;
        _layoutCacheSistema = null;
        _layoutCacheRevision = int.MinValue;
        _layoutCacheTopologia = 0;
    }

    private static void DibujarMuros(DrawingContext dc, Sistema sistema)
    {
        foreach (var muro in sistema.Muros)
        {
            var p1 = new Point(muro.PuntoInicio.X * PxPorMetro, muro.PuntoInicio.Y * PxPorMetro);
            var p2 = new Point(muro.PuntoFin.X    * PxPorMetro, muro.PuntoFin.Y    * PxPorMetro);
            double grosor = Math.Max(muro.Espesor * PxPorMetro, 1.0);

            var pen = new Pen(PaletaMuros.BrushParaEspesor(muro.Espesor), grosor)
            {
                LineCap = PenLineCap.Round,
            };
            dc.DrawLine(pen, p1, p2);
        }
    }

    private static void DibujarPatronLosa(DrawingContext dc, Rect rect, Losa losa, Pen pen)
    {
        string dir = losa.DireccionTrabajo;          // "2D" | "1D-V" | "1D-H"
        bool lineasV = dir is "1D-V" or "2D";
        bool lineasH = dir is "1D-H" or "2D";
        const int divisiones = 4;

        if (lineasV && rect.Width > 10)
            for (int i = 1; i < divisiones; i++)
            {
                double lx = rect.X + rect.Width * i / divisiones;
                dc.DrawLine(pen, new Point(lx, rect.Y), new Point(lx, rect.Bottom));
            }
        if (lineasH && rect.Height > 10)
            for (int i = 1; i < divisiones; i++)
            {
                double ly = rect.Y + rect.Height * i / divisiones;
                dc.DrawLine(pen, new Point(rect.X, ly), new Point(rect.Right, ly));
            }
    }

    private void DibujarRotuloLosa(DrawingContext dc, Rect rect, LayoutSolver.Placement p, IBrush brush)
    {
        var losa = p.Losa;
        double fsId  = Math.Clamp(rect.Height * 0.16, 9.0, 22.0);
        double fsSub = fsId * 0.72;
        var negrita = new Typeface(new FontFamily("Segoe UI"), FontStyle.Normal, FontWeight.Bold, FontStretch.Normal);
        var normal  = new Typeface(new FontFamily("Segoe UI"), FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);

        FormattedText Texto(string s, Typeface tf, double fs) => new(
            s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, fs, brush);

        var l1 = Texto(p.Id.ToString(CultureInfo.InvariantCulture), negrita, fsId);
        var l2 = Texto($"{losa.Lx:0.00} × {losa.Ly:0.00}", normal, fsSub);
        var l3 = Texto($"Tipo: {losa.Tipo}", normal, fsSub);

        double anchoMax = Math.Max(l1.Width, Math.Max(l2.Width, l3.Width));
        if (l1.Height + l2.Height + l3.Height + 6 > rect.Height || anchoMax > rect.Width)
        {
            dc.DrawText(l1, new Point(rect.X + (rect.Width - l1.Width) / 2,
                                      rect.Y + (rect.Height - l1.Height) / 2));
            return;
        }
        double cy = rect.Y + 4;
        dc.DrawText(l1, new Point(rect.X + (rect.Width - l1.Width) / 2, cy)); cy += l1.Height;
        dc.DrawText(l2, new Point(rect.X + (rect.Width - l2.Width) / 2, cy)); cy += l2.Height;
        dc.DrawText(l3, new Point(rect.X + (rect.Width - l3.Width) / 2, cy));
    }

    private static void DibujarMarcasAcero(DrawingContext dc, IReadOnlyList<LayoutSolver.Placement> placements,
                                           double offsetX, Sistema sistema, Pen pen)
    {
        var centro = new Dictionary<int, Point>();
        foreach (var p in placements)
            centro[p.Id] = new Point(
                offsetX + (p.X + p.Width / 2) * PxPorMetro,
                (p.Y + p.Height / 2) * PxPorMetro);

        void Marcar(IEnumerable<BordeAdic> bordes)
        {
            foreach (var b in bordes)
                if (centro.TryGetValue(b.BI, out var ci) && centro.TryGetValue(b.BJ, out var cj))
                    DibujarIconoAcero(dc, new Point((ci.X + cj.X) / 2, (ci.Y + cj.Y) / 2), pen);
        }
        Marcar(sistema.BordesX);
        Marcar(sistema.BordesY);
    }

    private static void DibujarIconoAcero(DrawingContext dc, Point c, Pen pen)
    {
        const double r = 11.0;
        const double g = 7.0;
        dc.DrawLine(pen, new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
        dc.DrawLine(pen, new Point(c.X - r, c.Y), new Point(c.X - r + g, c.Y - g));
        dc.DrawLine(pen, new Point(c.X + r, c.Y), new Point(c.X + r - g, c.Y + g));
    }

    // =====================================================================
    // Capa 3 — Overlay efímero (selección, tiradores, fantasma, chips, cotas)
    // =====================================================================

    /// <summary>
    /// Dibuja el overlay de interacción dentro del transform de zoom/pan. Los
    /// grosores y tamaños se dividen por la escala → constantes EN PANTALLA.
    /// Es estado puramente visual — jamás toca el SSOT.
    /// </summary>
    private void DibujarOverlay(DrawingContext dc)
    {
        double inv = _scaleX > 0 ? 1.0 / _scaleX : 1.0;

        // ---- Calibración del PDF — capa interactiva sobre todo lo demás ----
        if (ModoCalibrarPdf && _calibrarP1Pre is { } p1Cal && _calibrarP2Pre is { } p2Cal)
        {
            var pincelCal = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
            var penCal = new Pen(pincelCal, 2.0 * inv) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            dc.DrawLine(penCal, p1Cal, p2Cal);
            dc.DrawEllipse(pincelCal, null, p1Cal, 4 * inv, 4 * inv);
            dc.DrawEllipse(pincelCal, null, p2Cal, 4 * inv, 4 * inv);

            double dxM = (p2Cal.X - p1Cal.X) / PxPorMetro;
            double dyM = (p2Cal.Y - p1Cal.Y) / PxPorMetro;
            double distM = Math.Sqrt(dxM * dxM + dyM * dyM);
            var ft = new FormattedText(
                $"Línea de calibración: {distM:0.000} m",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 13 * inv, pincelCal);
            dc.DrawText(ft, new Point(p2Cal.X + 10 * inv, p2Cal.Y + 6 * inv));
        }

        // Los chips de adyacencia y el resalte de muro se dibujan SIEMPRE.
        DibujarChips(dc, inv);
        DibujarMuroSeleccionado(dc, inv);

        // Rectángulo fantasma de «Dibujar Losa».
        if (_modo == ModoArrastre.DibujarLosa)
        {
            if (_rectFantasmaPx.Width > 0 && _rectFantasmaPx.Height > 0)
            {
                var penDibujo = new Pen(PincelOverlay, 1.6 * inv) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
                dc.DrawRectangle(PincelOverlayRelleno, penDibujo, _rectFantasmaPx);
            }
            return;
        }

        // Línea fantasma de «Dibujar Muro».
        if (_modo == ModoArrastre.DibujarMuro)
        {
            var penMuro = new Pen(PincelOverlay, 1.8 * inv) { DashStyle = new DashStyle(new double[] { 5, 3 }, 0) };
            dc.DrawLine(penMuro, _muroAnclaPre, _muroFinPre);
            dc.DrawEllipse(PincelOverlay, null, _muroAnclaPre, 3 * inv, 3 * inv);
            dc.DrawEllipse(PincelOverlay, null, _muroFinPre, 3 * inv, 3 * inv);
            return;
        }

        if (_losaSeleccionada is null) return;

        if (_modo is ModoArrastre.Mover or ModoArrastre.Redimensionar)
        {
            var penFantasma = new Pen(PincelOverlay, 1.6 * inv) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            dc.DrawRectangle(PincelOverlayRelleno, penFantasma, _rectFantasmaPx);
            if (_grupoMoverActivo)
                foreach (var r in _grupoMoverFantasmasPx)
                    dc.DrawRectangle(PincelOverlayRelleno, penFantasma, r);
            DibujarCotas(dc, inv);
            return;
        }

        var rectPx = RectPxDeLosaSeleccionada();
        if (rectPx.Width <= 0 || rectPx.Height <= 0) return;

        dc.DrawRectangle(null, new Pen(PincelOverlay, 2.0 * inv), rectPx);

        // 8 tiradores cuadrados de tamaño constante en pantalla.
        double lado = HandleLadoPx * inv;
        var penHandle = new Pen(PincelOverlay, 1.4 * inv);
        foreach (var c in HandleCentros(rectPx))
            dc.DrawRectangle(Brushes.White, penHandle, new Rect(c.X - lado / 2, c.Y - lado / 2, lado, lado));
    }

    /// <summary>Resalta el muro cuyo Id coincide con <see cref="MuroSeleccionadoId"/> (−1 = ninguno).</summary>
    private void DibujarMuroSeleccionado(DrawingContext dc, double inv)
    {
        if (MuroSeleccionadoId < 0 || Sistema is not { } sistema) return;

        Muro? sel = null;
        foreach (var m in sistema.Muros)
            if (m.Id == MuroSeleccionadoId) { sel = m; break; }
        if (sel is null) return;

        var p1 = new Point(sel.PuntoInicio.X * PxPorMetro, sel.PuntoInicio.Y * PxPorMetro);
        var p2 = new Point(sel.PuntoFin.X    * PxPorMetro, sel.PuntoFin.Y    * PxPorMetro);

        double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return;

        double nx = -dy / len, ny = dx / len;        // normal unitaria al eje
        double ht = Math.Max(sel.Espesor * PxPorMetro, 1.0) / 2.0;

        var e0 = new Point(p1.X + nx * ht, p1.Y + ny * ht);
        var e1 = new Point(p2.X + nx * ht, p2.Y + ny * ht);
        var e2 = new Point(p2.X - nx * ht, p2.Y - ny * ht);
        var e3 = new Point(p1.X - nx * ht, p1.Y - ny * ht);

        var penContorno = new Pen(PincelOverlay, 2.0 * inv);
        dc.DrawLine(penContorno, e0, e1);
        dc.DrawLine(penContorno, e1, e2);
        dc.DrawLine(penContorno, e2, e3);
        dc.DrawLine(penContorno, e3, e0);

        double lado = HandleLadoPx * inv;
        var penHandle = new Pen(PincelOverlay, 1.4 * inv);
        foreach (var c in new[] { p1, p2 })
            dc.DrawRectangle(Brushes.White, penHandle, new Rect(c.X - lado / 2, c.Y - lado / 2, lado, lado));
    }

    private Rect RectPxDeLosaSeleccionada()
    {
        if (_losaSeleccionada is null) return default;
        foreach (var p in _placements)
            if (p.Losa.Id == _losaSeleccionada.Id)
                return PlacementARectPx(p);
        return default;
    }

    private Rect PlacementARectPx(LayoutSolver.Placement p) => new(
        _offsetXPx + p.X * PxPorMetro, p.Y * PxPorMetro,
        p.Width * PxPorMetro, p.Height * PxPorMetro);

    /// <summary>Pantalla → espacio pre-transform (deshace zoom y pan).</summary>
    private Point PantallaAPre(Point pantalla) => new(
        (pantalla.X - _tx) / _scaleX,
        (pantalla.Y - _ty) / _scaleY);

    /// <summary>Centros de los 8 tiradores (0..7 horario desde sup-izq).</summary>
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

    private LayoutSolver.Placement? HitTestLosa(Point pantalla)
    {
        if (_scaleX <= 0 || _placements.Count == 0) return null;
        var pre = PantallaAPre(pantalla);
        for (int i = _placements.Count - 1; i >= 0; i--)
            if (PlacementARectPx(_placements[i]).Contains(pre))
                return _placements[i];
        return null;
    }

    private int HandleBajoCursor(Point pantalla)
    {
        if (_losaSeleccionada is null || _scaleX <= 0) return -1;
        var rectPx = RectPxDeLosaSeleccionada();
        if (rectPx.Width <= 0 || rectPx.Height <= 0) return -1;

        var pre = PantallaAPre(pantalla);
        double radio = HandleLadoPx / _scaleX;
        var centros = HandleCentros(rectPx);
        for (int i = 0; i < centros.Length; i++)
            if (Math.Abs(pre.X - centros[i].X) <= radio &&
                Math.Abs(pre.Y - centros[i].Y) <= radio)
                return i;
        return -1;
    }

    // =====================================================================
    // Capa 3 — Chips de adyacencia
    // =====================================================================

    private void RecomputarChips()
        => _chips = Sistema is { } s ? AdyacenciaDetector.Detectar(s) : Array.Empty<AdyacenciaCandidata>();

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

    private int HitTestChip(Point pantalla)
    {
        if (_chips.Count == 0 || _scaleX <= 0) return -1;
        var pre = PantallaAPre(pantalla);
        double r = ChipRadioPx / _scaleX;
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
    // Capa 3 — Cotas dinámicas
    // =====================================================================

    private void DibujarCotas(DrawingContext dc, double inv)
    {
        if (_placements.Count == 0) return;

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
        double tick = 5.0 * inv;

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

    private FormattedText CrearTextoCota(double valorM, double inv)
        => new(valorM.ToString("0.00", CultureInfo.InvariantCulture) + " m",
               CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
               new Typeface("Segoe UI"), 11.0 * inv, PincelCota);

    // =====================================================================
    // Cálculo geométrico del arrastre (con snapping) — nunca muta el SSOT
    // =====================================================================

    private (List<double> X, List<double> Y) CandidatosSnap()
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var p in _placements)
        {
            if (_idsExcluidosSnap.Contains(p.Losa.Id)) continue;
            var r = PlacementARectPx(p);
            xs.Add(r.Left  / PxPorMetro);
            xs.Add(r.Right / PxPorMetro);
            ys.Add(r.Top    / PxPorMetro);
            ys.Add(r.Bottom / PxPorMetro);
        }
        return (xs, ys);
    }

    private Rect CalcularRectMovido(Point pantalla)
    {
        double dxPx = (pantalla.X - _dragStart.X) / _scaleX;
        double dyPx = (pantalla.Y - _dragStart.Y) / _scaleY;

        double xM = (_rectOriginalPx.X + dxPx) / PxPorMetro;
        double yM = (_rectOriginalPx.Y + dyPx) / PxPorMetro;
        double wM = _rectOriginalPx.Width  / PxPorMetro;
        double hM = _rectOriginalPx.Height / PxPorMetro;

        double snapX, snapY;
        if (SnapActivo)
        {
            var (candsX, candsY) = CandidatosSnap();
            snapX = SnappingEngine.SnapRango(xM, wM, candsX, PasoSnapGrilla, UmbralSnapM).Valor;
            snapY = SnappingEngine.SnapRango(yM, hM, candsY, PasoSnapGrilla, UmbralSnapM).Valor;
        }
        else { snapX = xM; snapY = yM; }

        return new Rect(snapX * PxPorMetro, snapY * PxPorMetro,
                        _rectOriginalPx.Width, _rectOriginalPx.Height);
    }

    private Rect CalcularRectRedimensionado(Point pantalla)
    {
        var pre = PantallaAPre(pantalla);
        double left = _rectOriginalPx.Left, right  = _rectOriginalPx.Right;
        double top  = _rectOriginalPx.Top,  bottom = _rectOriginalPx.Bottom;

        bool mueveL = _handleActivo is 0 or 6 or 7;
        bool mueveR = _handleActivo is 2 or 3 or 4;
        bool mueveT = _handleActivo is 0 or 1 or 2;
        bool mueveB = _handleActivo is 4 or 5 or 6;

        if (SnapActivo)
        {
            var (candsX, candsY) = CandidatosSnap();
            if (mueveL) left   = SnappingEngine.SnapCombinado(pre.X / PxPorMetro, candsX, PasoSnapGrilla, UmbralSnapM).Valor * PxPorMetro;
            if (mueveR) right  = SnappingEngine.SnapCombinado(pre.X / PxPorMetro, candsX, PasoSnapGrilla, UmbralSnapM).Valor * PxPorMetro;
            if (mueveT) top    = SnappingEngine.SnapCombinado(pre.Y / PxPorMetro, candsY, PasoSnapGrilla, UmbralSnapM).Valor * PxPorMetro;
            if (mueveB) bottom = SnappingEngine.SnapCombinado(pre.Y / PxPorMetro, candsY, PasoSnapGrilla, UmbralSnapM).Valor * PxPorMetro;
        }
        else
        {
            if (mueveL) left   = pre.X;
            if (mueveR) right  = pre.X;
            if (mueveT) top    = pre.Y;
            if (mueveB) bottom = pre.Y;
        }

        double minPx = MinLadoM * PxPorMetro;
        if (mueveL && left   > right  - minPx) left   = right  - minPx;
        if (mueveR && right  < left   + minPx) right  = left   + minPx;
        if (mueveT && top    > bottom - minPx) top    = bottom - minPx;
        if (mueveB && bottom < top    + minPx) bottom = top    + minPx;

        return new Rect(left, top, Math.Max(minPx, right - left), Math.Max(minPx, bottom - top));
    }

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

            if (cambio)
            {
                if (_grupoMoverActivo && MoverGrupoCommand is { } cmdGrupo)
                {
                    double dxM = (_rectFantasmaPx.X - _rectOriginalPx.X) / PxPorMetro;
                    double dyM = (_rectFantasmaPx.Y - _rectOriginalPx.Y) / PxPorMetro;
                    var movs = new List<MovimientoLosaEntry>(_grupoMoverOrigPx.Count);
                    foreach (var kv in _grupoMoverOrigPx)
                        movs.Add(new MovimientoLosaEntry(kv.Key,
                            kv.Value.X / PxPorMetro + dxM,
                            kv.Value.Y / PxPorMetro + dyM));
                    var args = new MovimientoGrupoArgs(movs);
                    if (cmdGrupo.CanExecute(args)) cmdGrupo.Execute(args);
                }
                else if (ActualizarLosaCommand is { } cmd)
                {
                    var dto = new ActualizacionLosaArgs(
                        _losaSeleccionada,
                        _rectFantasmaPx.X      / PxPorMetro,
                        _rectFantasmaPx.Y      / PxPorMetro,
                        _rectFantasmaPx.Width  / PxPorMetro,
                        _rectFantasmaPx.Height / PxPorMetro,
                        _losaSeleccionada.Tipo);
                    if (cmd.CanExecute(dto)) cmd.Execute(dto);
                }
            }
        }

        ResetGrupoMover();
        RecomputarChips();
        InvalidateVisual();
    }

    private void PropagarGrupoMover()
    {
        double dxPx = _rectFantasmaPx.X - _rectOriginalPx.X;
        double dyPx = _rectFantasmaPx.Y - _rectOriginalPx.Y;
        _grupoMoverFantasmasPx.Clear();
        foreach (var kv in _grupoMoverOrigPx)
        {
            if (_losaSeleccionada is not null && kv.Key == _losaSeleccionada.Id) continue;
            var r = kv.Value;
            _grupoMoverFantasmasPx.Add(new Rect(r.X + dxPx, r.Y + dyPx, r.Width, r.Height));
        }
    }

    private void ResetGrupoMover()
    {
        _grupoMoverActivo = false;
        _grupoMoverOrigPx.Clear();
        _grupoMoverFantasmasPx.Clear();
        _idsExcluidosSnap.Clear();
    }

    private void ConfirmarDibujoLosa()
    {
        double posX = _rectFantasmaPx.X      / PxPorMetro;
        double posY = _rectFantasmaPx.Y      / PxPorMetro;
        double lx   = _rectFantasmaPx.Width  / PxPorMetro;
        double ly   = _rectFantasmaPx.Height / PxPorMetro;
        _rectFantasmaPx = default;

        if (lx < MinLadoDibujoM || ly < MinLadoDibujoM)
        {
            InvalidateVisual();   // aborto silencioso — ni snapshot ni losa
            return;
        }

        if (CrearLosaCommand is { } cmd)
        {
            var dto = new CrearLosaArgs(posX, posY, lx, ly);
            if (cmd.CanExecute(dto)) cmd.Execute(dto);
        }
        RecomputarChips();
        InvalidateVisual();
    }

    private void ConfirmarDibujoMuro()
    {
        var iniPx = _muroAnclaPre;
        var finPx = _muroFinPre;

        double dxM = (finPx.X - iniPx.X) / PxPorMetro;
        double dyM = (finPx.Y - iniPx.Y) / PxPorMetro;
        double longitud = Math.Sqrt(dxM * dxM + dyM * dyM);

        if (longitud < MinLongitudMuroM) { InvalidateVisual(); return; }

        if (CrearMuroCommand is { } cmd)
        {
            var dto = new CrearMuroArgs(
                new PuntoCad(iniPx.X / PxPorMetro, iniPx.Y / PxPorMetro),
                new PuntoCad(finPx.X / PxPorMetro, finPx.Y / PxPorMetro));
            if (cmd.CanExecute(dto)) cmd.Execute(dto);
        }
        InvalidateVisual();
    }

    // =====================================================================
    // Calibración del PDF — helpers
    // =====================================================================

    private void AbortarCalibracion()
    {
        LimpiarCalibracion();
        ModoCalibrarPdf = false;
        if (CancelarCalibrarPdfCommand is { } cmd && cmd.CanExecute(null)) cmd.Execute(null);
        InvalidateVisual();
    }

    private void LimpiarCalibracion()
    {
        _calibrarP1Pre = null;
        _calibrarP2Pre = null;
        _calibrarConfirmando = false;
        Cursor = CursorArrow;
    }

    /// <summary>Snap ortogonal del cursor al eje cardinal más cercano si Shift está presionado.</summary>
    private static Point AplicarSnapOrtoCalibracion(Point pivote, Point cursor, KeyModifiers mods)
    {
        if (!mods.HasFlag(KeyModifiers.Shift)) return cursor;
        double dx = Math.Abs(cursor.X - pivote.X);
        double dy = Math.Abs(cursor.Y - pivote.Y);
        return dx >= dy ? new Point(cursor.X, pivote.Y) : new Point(pivote.X, cursor.Y);
    }

    // =====================================================================
    // Cursores
    // =====================================================================

    private void ActualizarCursorHover(Point pantalla)
    {
        int h = HandleBajoCursor(pantalla);
        if (h >= 0) { Cursor = CursorDeHandle(h); return; }
        Cursor = HitTestLosa(pantalla) != null ? CursorSizeAll : CursorArrow;
    }

    private static Cursor CursorDeHandle(int h) => h switch
    {
        0 or 4 => CursorNWSE,   // esquinas ╲
        2 or 6 => CursorNESW,   // esquinas ╱
        1 or 5 => CursorNS,     // bordes horizontales
        3 or 7 => CursorWE,     // bordes verticales
        _      => CursorArrow,
    };

    // =====================================================================
    // Captura por-puntero (reemplaza CaptureMouse/ReleaseMouseCapture WPF)
    // =====================================================================

    private void Capturar(IPointer p) { p.Capture(this); _punteroCapturado = p; }
    private void LiberarCaptura() { _punteroCapturado?.Capture(null); _punteroCapturado = null; }

    // =====================================================================
    // Interacción — zoom (rueda), pan, selección y arrastre de losas
    // =====================================================================

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double factor = e.Delta.Y > 0 ? ZoomStep : 1.0 / ZoomStep;
        double nuevo = Math.Clamp(_scaleX * factor, MinScale, MaxScale);
        if (Math.Abs(nuevo - _scaleX) < 1e-9) return;

        var pt = e.GetPosition(this);
        double k = nuevo / _scaleX;
        _tx = pt.X - k * (pt.X - _tx);
        _ty = pt.Y - k * (pt.Y - _ty);
        _scaleX = _scaleY = nuevo;

        InvalidateVisual();              // los tiradores mantienen su tamaño en pantalla
        ReiniciarDebounceReraster();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pp = e.GetCurrentPoint(this);
        if (!pp.Properties.IsLeftButtonPressed) return;   // sólo botón izquierdo

        var pt = pp.Position;
        _dragStart = pt;
        Focus();

        // Calibración del PDF: prioridad absoluta sobre cualquier herramienta.
        if (ModoCalibrarPdf)
        {
            if (_calibrarConfirmando) { e.Handled = true; return; }
            var ptPre = PantallaAPre(pt);
            if (_calibrarP1Pre is null)
            {
                _calibrarP1Pre = ptPre;
                _calibrarP2Pre = ptPre;
                InvalidateVisual();
            }
            else
            {
                var p2 = AplicarSnapOrtoCalibracion(_calibrarP1Pre.Value, ptPre, e.KeyModifiers);
                _calibrarP2Pre = p2;
                _calibrarConfirmando = true;
                InvalidateVisual();

                double pivX = _calibrarP1Pre.Value.X / PxPorMetro;
                double pivY = _calibrarP1Pre.Value.Y / PxPorMetro;
                double dxM = (p2.X - _calibrarP1Pre.Value.X) / PxPorMetro;
                double dyM = (p2.Y - _calibrarP1Pre.Value.Y) / PxPorMetro;
                double distM = Math.Sqrt(dxM * dxM + dyM * dyM);

                var midPre = new Point(
                    (_calibrarP1Pre.Value.X + p2.X) / 2.0,
                    (_calibrarP1Pre.Value.Y + p2.Y) / 2.0);
                var midPant = new Point(midPre.X * _scaleX + _tx, midPre.Y * _scaleY + _ty);

                CalibrarPdfPuntosListos?.Invoke(this,
                    new CalibrarPdfPuntosListosEventArgs(pivX, pivY, distM, midPant));
            }
            e.Handled = true;
            return;
        }

        // Doble clic sobre una losa (modo Puntero) → abrir el editor flotante.
        if (e.ClickCount == 2 && ModoInteraccion == ModoInteraccionCad.Puntero)
        {
            var hitDoble = HitTestLosa(pt);
            if (hitDoble != null)
            {
                var rp = PlacementARectPx(hitDoble);
                var rectPant = new Rect(
                    rp.X * _scaleX + _tx, rp.Y * _scaleY + _ty,
                    rp.Width * _scaleX, rp.Height * _scaleY);
                LosaDobleClicada?.Invoke(this,
                    new LosaDobleClicEventArgs(hitDoble.Losa, rectPant, hitDoble.X, hitDoble.Y));
                return;
            }
        }

        // Herramienta «Mano» → siempre pan, sin tocar losas.
        if (ModoInteraccion == ModoInteraccionCad.Mano)
        {
            _modo = ModoArrastre.Pan;
            _panStartTx = _tx; _panStartTy = _ty;
            Capturar(e.Pointer);
            Cursor = CursorHand;
            return;
        }

        // Herramienta «Dibujar Losa» → iniciar el trazado de un rectángulo.
        if (ModoInteraccion == ModoInteraccionCad.DibujarLosa)
        {
            _chips = Array.Empty<AdyacenciaCandidata>();
            _modo = ModoArrastre.DibujarLosa;
            _dibujarAnclaPre = PantallaAPre(pt);
            _rectFantasmaPx = new Rect(_dibujarAnclaPre, _dibujarAnclaPre);
            Capturar(e.Pointer);
            Cursor = CursorCross;
            return;
        }

        // Herramienta «Dibujar Muro» → iniciar el trazado de un segmento.
        if (ModoInteraccion == ModoInteraccionCad.DibujarMuro)
        {
            _chips = Array.Empty<AdyacenciaCandidata>();
            _modo = ModoArrastre.DibujarMuro;
            _muroAnclaPre = PantallaAPre(pt);
            _muroFinPre = _muroAnclaPre;
            Capturar(e.Pointer);
            Cursor = CursorCross;
            return;
        }

        // 1) ¿Sobre un tirador de la losa seleccionada? → redimensionar.
        int handle = HandleBajoCursor(pt);
        if (handle >= 0)
        {
            _handleActivo = handle;
            _rectOriginalPx = RectPxDeLosaSeleccionada();
            _rectFantasmaPx = _rectOriginalPx;
            _chips = Array.Empty<AdyacenciaCandidata>();
            InvalidateVisual();
            _modo = ModoArrastre.Redimensionar;
            _idsExcluidosSnap.Clear();
            if (_losaSeleccionada is not null) _idsExcluidosSnap.Add(_losaSeleccionada.Id);
            Capturar(e.Pointer);
            return;
        }

        // 2) ¿Sobre un chip de adyacencia? → crear el BordeAdic (acción instantánea).
        int chip = HitTestChip(pt);
        if (chip >= 0)
        {
            var candidato = _chips[chip];
            if (CrearBordeAdicCommand is { } cmd && cmd.CanExecute(candidato)) cmd.Execute(candidato);
            RecomputarChips();
            InvalidateVisual();
            return;
        }

        // 3) ¿Sobre una losa? → seleccionarla y armar un posible movimiento.
        var hit = HitTestLosa(pt);
        if (hit != null)
        {
            _losaSeleccionada = hit.Losa;
            _handleActivo = -1;
            _rectOriginalPx = PlacementARectPx(hit);
            _rectFantasmaPx = _rectOriginalPx;
            _chips = Array.Empty<AdyacenciaCandidata>();
            InvalidateVisual();
            _modo = ModoArrastre.Mover;

            _idsExcluidosSnap.Clear();
            _grupoMoverOrigPx.Clear();
            _grupoMoverFantasmasPx.Clear();
            _grupoMoverActivo = false;
            if (MoverConectadas && Sistema is { } sis)
            {
                var componente = GrafoAdyacencia.ComponenteConexa(sis, hit.Losa.Id);
                if (componente.Count > 1)
                {
                    _grupoMoverActivo = true;
                    foreach (var p in _placements)
                        if (componente.Contains(p.Losa.Id))
                            _grupoMoverOrigPx[p.Losa.Id] = PlacementARectPx(p);
                    foreach (var id in componente) _idsExcluidosSnap.Add(id);
                }
            }
            if (!_grupoMoverActivo) _idsExcluidosSnap.Add(hit.Losa.Id);

            Capturar(e.Pointer);
            Cursor = CursorSizeAll;
            return;
        }

        // 4) Espacio vacío → pan, y se deselecciona lo que hubiera.
        if (_losaSeleccionada != null) { _losaSeleccionada = null; InvalidateVisual(); }
        _modo = ModoArrastre.Pan;
        _panStartTx = _tx; _panStartTy = _ty;
        Capturar(e.Pointer);
        Cursor = CursorSizeAll;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var modo = _modo;
        _modo = ModoArrastre.Ninguno;
        _handleActivo = -1;
        LiberarCaptura();
        Cursor = CursorArrow;
        var pt = e.GetPosition(this);

        switch (modo)
        {
            case ModoArrastre.Pan:
                // Un clic (no un pan real) sobre espacio vacío → mapear polígono.
                double dx = pt.X - _dragStart.X, dy = pt.Y - _dragStart.Y;
                if (ModoInteraccion != ModoInteraccionCad.Mano
                    && Math.Sqrt(dx * dx + dy * dy) < ClicMaxPx)
                {
                    var poligono = HitTestPoligono(pt);
                    if (poligono != null && PoligonoClickCommand is { } cmd && cmd.CanExecute(poligono))
                    {
                        cmd.Execute(poligono);
                        RecomputarChips();
                        InvalidateVisual();
                    }
                }
                break;

            case ModoArrastre.Mover:
            case ModoArrastre.Redimensionar:
                ConfirmarArrastre();
                break;

            case ModoArrastre.DibujarLosa:
                ConfirmarDibujoLosa();
                break;

            case ModoArrastre.DibujarMuro:
                ConfirmarDibujoMuro();
                break;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pt = e.GetPosition(this);

        // Modo Calibrar PDF — la línea se anima hasta el cursor tras fijar P₁.
        if (ModoCalibrarPdf)
        {
            Cursor = CursorCross;
            if (_calibrarP1Pre is { } p1 && !_calibrarConfirmando)
            {
                _calibrarP2Pre = AplicarSnapOrtoCalibracion(p1, PantallaAPre(pt), e.KeyModifiers);
                InvalidateVisual();
            }
            return;
        }

        switch (_modo)
        {
            case ModoArrastre.Ninguno:
                if (ModoInteraccion is ModoInteraccionCad.DibujarLosa or ModoInteraccionCad.DibujarMuro)
                    Cursor = CursorCross;
                else if (ModoInteraccion == ModoInteraccionCad.Mano)
                    Cursor = CursorHand;
                else
                    ActualizarCursorHover(pt);
                break;

            case ModoArrastre.DibujarLosa:
                _rectFantasmaPx = new Rect(_dibujarAnclaPre, PantallaAPre(pt));
                InvalidateVisual();
                break;

            case ModoArrastre.DibujarMuro:
                _muroFinPre = PantallaAPre(pt);
                InvalidateVisual();
                break;

            case ModoArrastre.Pan:
                // Inmediato: cambiar el transform exige repintar (WPF no lo necesitaba).
                _tx = _panStartTx + (pt.X - _dragStart.X);
                _ty = _panStartTy + (pt.Y - _dragStart.Y);
                InvalidateVisual();
                break;

            case ModoArrastre.Mover:
                _rectFantasmaPx = CalcularRectMovido(pt);
                if (_grupoMoverActivo) PropagarGrupoMover();
                InvalidateVisual();
                break;

            case ModoArrastre.Redimensionar:
                _rectFantasmaPx = CalcularRectRedimensionado(pt);
                InvalidateVisual();
                break;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        // Captura perdida a mitad de un gesto: descartar el arrastre (el SSOT
        // nunca se tocó) y volver al overlay de selección estática.
        bool arrastrando = _modo is ModoArrastre.Mover or ModoArrastre.Redimensionar
                                 or ModoArrastre.DibujarLosa or ModoArrastre.DibujarMuro;
        if (_modo == ModoArrastre.DibujarLosa) _rectFantasmaPx = default;
        ResetGrupoMover();
        _modo = ModoArrastre.Ninguno;
        _handleActivo = -1;
        _punteroCapturado = null;
        if (arrastrando) InvalidateVisual();
    }

    /// <summary>
    /// Escape: durante un trazo lo cancela; con herramienta de dibujo activa sin
    /// trazo, vuelve a «Puntero»; en modo Calibrar PDF, aborta la calibración.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape) return;

        if (ModoCalibrarPdf) { AbortarCalibracion(); e.Handled = true; return; }

        if (_modo == ModoArrastre.DibujarLosa)
        {
            _modo = ModoArrastre.Ninguno;
            _rectFantasmaPx = default;
            if (EstaCapturado) LiberarCaptura();
            InvalidateVisual();
            e.Handled = true;
        }
        else if (_modo == ModoArrastre.DibujarMuro)
        {
            _modo = ModoArrastre.Ninguno;
            if (EstaCapturado) LiberarCaptura();
            InvalidateVisual();
            e.Handled = true;
        }
        else if (ModoInteraccion is ModoInteraccionCad.DibujarLosa or ModoInteraccionCad.DibujarMuro)
        {
            ModoInteraccion = ModoInteraccionCad.Puntero;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Sobre qué polígono cerrado del DXF (si alguno) cae el punto de pantalla.
    /// Deshace zoom/pan y el flip-Y de la capa del plano antes del test punto-en-polígono.
    /// </summary>
    private PolilineaCad? HitTestPoligono(Point pantalla)
    {
        var plano = Plano;
        if (plano is null || plano.EstaVacio) return null;
        if (_scaleX <= 0) return null;

        // Inverso EXACTO del render del plano (DibujarPlano): deshace zoom/pan,
        // Px, el flip-Y de la capa y —lo crítico— la escala/offset del ajuste
        // espacial. Antes sólo deshacía /Px y el flip-Y → con Escala≠1 u
        // Offset≠0 el clic caía en el punto-mundo equivocado.
        var punto = TransformPlano(incluirZoomPan: true).PantallaAMundo(pantalla);

        foreach (var ent in plano.Entidades)
            if (ent is PolilineaCad poli && poli.Cerrada && PoligonoLosaMapper.ContienePunto(poli, punto))
                return poli;
        return null;
    }

    // =====================================================================
    // Re-rasterización del PDF — debounce tras un zoom
    // =====================================================================

    private void ReiniciarDebounceReraster()
    {
        _debounceReraster.Stop();
        _debounceReraster.Start();
    }

    /// <summary>
    /// Tras el debounce de zoom, si el PDF en pantalla supera 1.5× los píxeles del
    /// bitmap actual, pide al VM uno más nítido. El zoom-out nunca dispara nada.
    /// </summary>
    private void EvaluarReRasterizado()
    {
        _debounceReraster.Stop();
        if (Pdf is not { EstaVacio: false } pdf || FondoPdf is not { } img) return;

        double anchoEnPantalla = pdf.Ancho * pdf.Escala * PxPorMetro * _scaleX;
        if (anchoEnPantalla <= img.PixelSize.Width * 1.5) return;   // el bitmap aún alcanza

        int objetivo = (int)Math.Clamp(anchoEnPantalla, 1500, 6000);
        if (ReRasterizarPdfCommand is { } cmd && cmd.CanExecute(objetivo)) cmd.Execute(objetivo);
        else ReiniciarDebounceReraster();   // ocupado → reintentar en 350 ms
    }
}
