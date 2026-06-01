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

    // ---- Pan (E.1: arrastre con botón izquierdo; E.2 lo reemplaza por la máquina de modos) ----
    private bool _panning;
    private Point _panStartScreen;
    private double _panStartTx, _panStartTy;

    // ---- Debounce de re-rasterización del PDF (E.2) — declarado para no perder el cableado ----
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

    // CS0067: en E.1 estos eventos aún no se disparan (los levanta la interacción
    // de mouse de la Fase E.2). Se declaran ya para no romper los bindings/handlers.
#pragma warning disable CS0067
    /// <summary>Doble clic sobre una losa (modo Puntero) — lo escucha CadView (E.2).</summary>
    public event EventHandler<LosaDobleClicEventArgs>? LosaDobleClicada;

    /// <summary>Segundo punto de calibración del PDF fijado — lo escucha CadView (E.2).</summary>
    public event EventHandler<CalibrarPdfPuntosListosEventArgs>? CalibrarPdfPuntosListos;
#pragma warning restore CS0067

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
            DibujarGrilla(context);
            DibujarPlano(context);
            DibujarLosas(context);
            // Overlay (selección, tiradores, fantasma, chips, calibración) → Fase E.2.
        }
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

        double esc = plano.Escala;
        double bx = (plano.MinX * esc + plano.OffsetX) * PxPorMetro;
        double bw = plano.Ancho * esc * PxPorMetro;
        double bh = plano.Alto  * esc * PxPorMetro;
        if (bw < 1e-6 || bh < 1e-6) return;

        const double margen = 0.92;
        double fit = Math.Clamp(Math.Min(w / bw, h / bh) * margen, MinScale, MaxScale);

        double cx = bx + bw / 2.0;
        double cy = bh / 2.0;
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
    /// confirmar la edición in-canvas. Port a Avalonia: InvalidateVisual().
    /// </summary>
    public void RefrescarLosas() => InvalidateVisual();

    // =====================================================================
    // Capa 0 — Grilla métrica
    // =====================================================================

    private void DibujarGrilla(DrawingContext dc)
    {
        double w = Math.Max(Bounds.Width, 200);
        double h = Math.Max(Bounds.Height, 200);
        double extra = 2000;
        var penFino   = new Pen(new SolidColorBrush(Color.FromArgb(40, 120, 120, 120)), 0.5);
        var penMetro5 = new Pen(new SolidColorBrush(Color.FromArgb(80, 120, 120, 120)), 0.8);

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

        // Ejes del origen (0,0).
        var penEjeX = new Pen(new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)), 1.6);
        var penEjeY = new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), 1.6);
        dc.DrawLine(penEjeX, new Point(-extra, 0), new Point(w + extra, 0));
        dc.DrawLine(penEjeY, new Point(0, -extra), new Point(0, h + extra));

        var pincelOrigen = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        dc.DrawEllipse(pincelOrigen, null, new Point(0, 0), 3.0, 3.0);

        // Bounding box del plano DXF.
        if (Plano is { EstaVacio: false } plano)
        {
            var penBBox = new Pen(new SolidColorBrush(Color.FromArgb(120, 0x33, 0x66, 0x99)), 1.0)
            {
                DashStyle = new DashStyle(new double[] { 6, 4 }, 0),
            };
            double esc = plano.Escala;
            var bbox = new Rect((plano.MinX * esc + plano.OffsetX) * PxPorMetro, 0,
                                plano.Ancho * esc * PxPorMetro, plano.Alto * esc * PxPorMetro);
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
        double maxYT = plano.MaxY * esc + plano.OffsetY;
        Point ToPx(PuntoCad p) => new(
            (p.X * esc + plano.OffsetX) * PxPorMetro,
            (maxYT - (p.Y * esc + plano.OffsetY)) * PxPorMetro);

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

        LayoutSolver.LayoutResult layout;
        try { layout = LayoutSolver.Solve(sistema); }
        catch { DibujarMuros(dc, sistema); return; }

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
    // Zoom (rueda, hacia el cursor) y pan (arrastre) — E.1
    // =====================================================================

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pos = e.GetPosition(this);
        double factor = e.Delta.Y > 0 ? ZoomStep : 1.0 / ZoomStep;
        double nuevo = Math.Clamp(_scaleX * factor, MinScale, MaxScale);
        factor = nuevo / _scaleX;

        // Mantener fijo el punto bajo el cursor: t' = pos - factor·(pos − t).
        _tx = pos.X - factor * (pos.X - _tx);
        _ty = pos.Y - factor * (pos.Y - _ty);
        _scaleX = _scaleY = nuevo;

        InvalidateVisual();
        ReiniciarDebounceReraster();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pp = e.GetCurrentPoint(this);
        if (pp.Properties.IsLeftButtonPressed)
        {
            _panning = true;
            _panStartScreen = pp.Position;
            _panStartTx = _tx;
            _panStartTy = _ty;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_panning) return;
        var pos = e.GetPosition(this);
        _tx = _panStartTx + (pos.X - _panStartScreen.X);
        _ty = _panStartTy + (pos.Y - _panStartScreen.Y);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _panning = false;
        e.Pointer.Capture(null);
    }

    // =====================================================================
    // Re-rasterización del PDF — cableado declarado, lógica en Fase E.2
    // =====================================================================

    private void ReiniciarDebounceReraster()
    {
        _debounceReraster.Stop();
        _debounceReraster.Start();
    }

    /// <summary>Evalúa si conviene pedir un PDF más nítido tras un zoom (Fase E.2).</summary>
    private void EvaluarReRasterizado()
    {
        _debounceReraster.Stop();
        // La heurística de nitidez y el disparo de ReRasterizarPdfCommand se
        // portan en la Fase E.2 junto con la interacción del PDF.
    }
}
