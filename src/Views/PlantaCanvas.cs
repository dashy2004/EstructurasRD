using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Services;
using LosasPlus.Vigas;
using LosasPlus.Transmision;

namespace LosasPlus.Views;

public class PlantaCanvas : Control
{
    private int _dragEndpoint = 2; // 0 = start, 1 = end, 2 = whole body
    private double _scale = 40.0;
    private double _tx = 100.0;
    private double _ty = 100.0;
    private Point _lastMousePos;
    private bool _isPanning;
    private bool _isDragging;
    private Point _dragStartPos;
    private double _dragStartElementX;
    private double _dragStartElementY;
    private Point? _mousePosForSnap;

    // Snapping configuration
    public bool IsSnappingEnabled { get; set; } = true;
    public double StepGrid { get; set; } = 0.5;

    public static readonly DirectProperty<PlantaCanvas, Nivel?> NivelProperty =
        AvaloniaProperty.RegisterDirect<PlantaCanvas, Nivel?>(
            nameof(Nivel),
            o => o.Nivel,
            (o, v) => o.Nivel = v);

    private Nivel? _nivel;
    public Nivel? Nivel
    {
        get => _nivel;
        set
        {
            if (_nivel is LosasPlus.Models.IModeloObservable viejo)
                viejo.ModeloCambiado -= OnModeloCambiado;
            if (SetAndRaise(NivelProperty, ref _nivel, value))
            {
                if (_nivel is LosasPlus.Models.IModeloObservable nuevo)
                    nuevo.ModeloCambiado += OnModeloCambiado;
                SelectedElement = null;
                InvalidateVisual();
            }
        }
    }

    private void OnModeloCambiado(object? sender, System.EventArgs e)
        => Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);

    // ---- Underlay PDF (Paso 2 unificación): capa de referencia read-only ----
    // El plano importado se dibuja DEBAJO de la estructura para calcar sobre él.
    private PdfReferencia? _pdfRef;
    public static readonly DirectProperty<PlantaCanvas, PdfReferencia?> PdfRefProperty =
        AvaloniaProperty.RegisterDirect<PlantaCanvas, PdfReferencia?>(
            nameof(PdfRef), o => o.PdfRef, (o, v) => o.PdfRef = v);
    public PdfReferencia? PdfRef
    {
        get => _pdfRef;
        set
        {
            bool esNuevo = _pdfRef is null && value is not null;
            if (SetAndRaise(PdfRefProperty, ref _pdfRef, value))
            {
                InvalidateVisual();
                if (esNuevo) EncuadrarTrasImport();   // UI1.7: auto-encuadre al importar
            }
        }
    }

    private Avalonia.Media.Imaging.Bitmap? _fondoPdf;
    public static readonly DirectProperty<PlantaCanvas, Avalonia.Media.Imaging.Bitmap?> FondoPdfProperty =
        AvaloniaProperty.RegisterDirect<PlantaCanvas, Avalonia.Media.Imaging.Bitmap?>(
            nameof(FondoPdf), o => o.FondoPdf, (o, v) => o.FondoPdf = v);
    public Avalonia.Media.Imaging.Bitmap? FondoPdf
    {
        get => _fondoPdf;
        set { if (SetAndRaise(FondoPdfProperty, ref _fondoPdf, value)) InvalidateVisual(); }
    }

    private double _opacidadPdf = 0.6;
    public static readonly DirectProperty<PlantaCanvas, double> OpacidadPdfProperty =
        AvaloniaProperty.RegisterDirect<PlantaCanvas, double>(
            nameof(OpacidadPdf), o => o.OpacidadPdf, (o, v) => o.OpacidadPdf = v);
    public double OpacidadPdf
    {
        get => _opacidadPdf;
        set { if (SetAndRaise(OpacidadPdfProperty, ref _opacidadPdf, value)) InvalidateVisual(); }
    }

    private PlanoReferencia? _planoDxf;
    public static readonly DirectProperty<PlantaCanvas, PlanoReferencia?> PlanoDxfProperty =
        AvaloniaProperty.RegisterDirect<PlantaCanvas, PlanoReferencia?>(
            nameof(PlanoDxf), o => o.PlanoDxf, (o, v) => o.PlanoDxf = v);
    public PlanoReferencia? PlanoDxf
    {
        get => _planoDxf;
        set
        {
            bool esNuevo = _planoDxf is null && value is not null;
            if (SetAndRaise(PlanoDxfProperty, ref _planoDxf, value))
            {
                InvalidateVisual();
                if (esNuevo) EncuadrarTrasImport();   // UI1.7: auto-encuadre al importar
            }
        }
    }

    // ---- Tokens de revisión (UI1.6): Plano/Pdf son objetos mutables; el VM
    // bumpea RevisionPlano/RevisionPdf al editar Escala/Offset y este canvas
    // redibuja al observarlos (mismo patrón que usaba el host CAD, retirado en UI1.6). ----
    private int _revisionPlano;
    public static readonly DirectProperty<PlantaCanvas, int> RevisionPlanoProperty =
        AvaloniaProperty.RegisterDirect<PlantaCanvas, int>(
            nameof(RevisionPlano), o => o.RevisionPlano, (o, v) => o.RevisionPlano = v);
    public int RevisionPlano
    {
        get => _revisionPlano;
        set { if (SetAndRaise(RevisionPlanoProperty, ref _revisionPlano, value)) InvalidateVisual(); }
    }

    private int _revisionPdf;
    public static readonly DirectProperty<PlantaCanvas, int> RevisionPdfProperty =
        AvaloniaProperty.RegisterDirect<PlantaCanvas, int>(
            nameof(RevisionPdf), o => o.RevisionPdf, (o, v) => o.RevisionPdf = v);
    public int RevisionPdf
    {
        get => _revisionPdf;
        set { if (SetAndRaise(RevisionPdfProperty, ref _revisionPdf, value)) InvalidateVisual(); }
    }

    // ---- Defaults de muros nuevos (UI1.6): compartidos con el panel PLANO/PDF
    // vía binding a CadEditor.EspesorMuroNuevo/AlturaMuroNueva (paridad CAD).
    // Defaults alineados con los del VM (0.15 / 2.80) — el binding los pisa al cargar la vista. ----
    private double _espesorMuroNuevo = 0.15;
    public static readonly DirectProperty<PlantaCanvas, double> EspesorMuroNuevoProperty =
        AvaloniaProperty.RegisterDirect<PlantaCanvas, double>(
            nameof(EspesorMuroNuevo), o => o.EspesorMuroNuevo, (o, v) => o.EspesorMuroNuevo = v);
    public double EspesorMuroNuevo
    {
        get => _espesorMuroNuevo;
        set => SetAndRaise(EspesorMuroNuevoProperty, ref _espesorMuroNuevo, value);
    }

    private double _alturaMuroNueva = 2.80;
    public static readonly DirectProperty<PlantaCanvas, double> AlturaMuroNuevaProperty =
        AvaloniaProperty.RegisterDirect<PlantaCanvas, double>(
            nameof(AlturaMuroNueva), o => o.AlturaMuroNueva, (o, v) => o.AlturaMuroNueva = v);
    public double AlturaMuroNueva
    {
        get => _alturaMuroNueva;
        set => SetAndRaise(AlturaMuroNuevaProperty, ref _alturaMuroNueva, value);
    }

    public static readonly DirectProperty<PlantaCanvas, Edificio?> EdificioProperty =
        AvaloniaProperty.RegisterDirect<PlantaCanvas, Edificio?>(
            nameof(Edificio),
            o => o.Edificio,
            (o, v) => o.Edificio = v);

    private Edificio? _edificio;
    public Edificio? Edificio
    {
        get => _edificio;
        set
        {
            if (_edificio is LosasPlus.Models.IModeloObservable viejo)
                viejo.ModeloCambiado -= OnModeloCambiado;
            if (SetAndRaise(EdificioProperty, ref _edificio, value))
            {
                if (_edificio is LosasPlus.Models.IModeloObservable nuevo)
                    nuevo.ModeloCambiado += OnModeloCambiado;
                InvalidateVisual();
            }
        }
    }

    public event Action<object?>? SelectionChanged;

    private object? _selectedElement;
    public object? SelectedElement
    {
        get => _selectedElement;
        set
        {
            if (_selectedElement != value)
            {
                _selectedElement = value;
                SelectionChanged?.Invoke(value);
                InvalidateVisual();
            }
        }
    }

    private string _activeTool = "Puntero";
    public string ActiveTool
    {
        get => _activeTool;
        set { _activeTool = value; if (value != "ConectarBordes") _primerLosaIdBorde = null; }
    }

    public PlantaCanvas()
    {
        ClipToBounds = true;
    }

    // ---- Calibración del PDF (UI1.2): gesto de 2 puntos, en metros ----

    private Point? _calP1M;
    private Point? _calP2M;

    /// <summary>
    /// Segundo punto de calibración fijado: (pivote P₁ en metros, distancia
    /// trazada en metros). La vista pide la distancia real y ejecuta
    /// <c>CadEditor.AplicarCalibrarPdfCommand</c> (homotecia compartida de
    /// <c>CalibradorPdf</c>) — un solo camino de aplicación para ambos lienzos.
    /// </summary>
    public event Action<Point, double>? CalibracionPdfLista;

    /// <summary>Limpia el gesto de calibración en curso (Esc / cambio de herramienta).</summary>
    public void CancelarCalibracionPdf()
    {
        _calP1M = null;
        _calP2M = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Con Shift, fuerza la línea P₁→P₂ a horizontal o vertical según el eje
    /// del delta dominante — el mismo gesto que la calibración del lienzo CAD.
    /// </summary>
    public static Point AplicarOrtoCalibracion(Point p1, Point p2, KeyModifiers mods)
    {
        if (!mods.HasFlag(KeyModifiers.Shift)) return p2;
        return Math.Abs(p2.X - p1.X) >= Math.Abs(p2.Y - p1.Y)
            ? new Point(p2.X, p1.Y)
            : new Point(p1.X, p2.Y);
    }

    // ---- Underlay DXF: mapeo mundo (Y-up) ↔ planta (Y-down) (UI1.3) ----

    /// <summary>
    /// Punto mundo del DXF → planta: <c>yPlanta = OffsetY + (MaxY − yMundo)·Escala</c>
    /// — la MISMA convención que <c>DxfEstructuraMapper.CrearLosaBatch</c> y el
    /// flip-Y del lienzo CAD. (Antes el underlay se dibujaba sin el flip y el
    /// plano quedaba espejado verticalmente respecto a la estructura.)
    /// </summary>
    public static Point PlanoAPlanta(PlanoReferencia plano, PuntoCad pt) =>
        new(plano.OffsetX + pt.X * plano.Escala,
            plano.OffsetY + (plano.MaxY - pt.Y) * plano.Escala);

    /// <summary>Inverso exacto de <see cref="PlanoAPlanta"/> — para el hit-test de «Calcar losa».</summary>
    public static PuntoCad PlantaAPlano(PlanoReferencia plano, Point pPlanta) =>
        new((pPlanta.X - plano.OffsetX) / plano.Escala,
            plano.MaxY - (pPlanta.Y - plano.OffsetY) / plano.Escala);

    /// <summary>
    /// Polilínea cerrada del DXF clickeada con la herramienta «Calcar losa»
    /// (<c>null</c> = el click no cayó dentro de ninguna). La vista ejecuta
    /// <c>CadEditor.MapearPoligonoCommand</c>, que valida el rectángulo y crea
    /// la losa anclada.
    /// </summary>
    public event Action<PolilineaCad?>? PoligonoMapeado;

    // ---- Resize de losa con asas (UI1.5) ----

    private Losa? _resizeLosa;
    private AsaRedim _resizeAsa;
    private RectM _resizeRectOriginal;

    /// <summary>
    /// Disparado al INICIAR un gesto que muta el modelo (drag o resize) — la
    /// vista toma ahí el snapshot de Undo (un Ctrl+Z revierte el gesto entero).
    /// </summary>
    public event Action? GestoEdicionIniciado;

    // ---- Conectar bordes desde el lienzo (UI1.8) ----

    private int? _primerLosaIdBorde;
    /// <summary>Se dispara con (idA, idB) cuando el usuario elige 2 losas en modo «Conectar bordes».</summary>
    public event Action<int, int>? BordeConexionSolicitada;

    public event Action<LosasPlus.Services.BordeLocalizado>? AlternarBalanceoSolicitado;
    public event Action<LosasPlus.Services.BordeLocalizado>? EliminarBordeSolicitado;

    /// <summary>Centros de las 8 asas (orden de <see cref="AsaRedim"/>), en metros.</summary>
    private static (double X, double Y)[] CentrosAsas(Losa losa)
    {
        double izq = losa.CoordenadaX, der = izq + losa.Lx;
        double sup = losa.CoordenadaY, inf = sup + losa.Ly;
        double mx = (izq + der) / 2.0, my = (sup + inf) / 2.0;
        return new[]
        {
            (izq, sup), (mx, sup), (der, sup), (der, my),
            (der, inf), (mx, inf), (izq, inf), (izq, my),
        };
    }

    /// <summary>
    /// Asa de redimensionado de la losa bajo el punto (metros), o <c>null</c>.
    /// Tolerancia cuadrada de <paramref name="tolM"/> por eje — patrón del
    /// <c>HandleBajoCursor</c> del lienzo CAD, en el dominio de planta.
    /// </summary>
    public static AsaRedim? AsaEnPunto(Losa losa, Point pM, double tolM)
    {
        var centros = CentrosAsas(losa);
        for (int i = 0; i < centros.Length; i++)
            if (Math.Abs(pM.X - centros[i].X) <= tolM && Math.Abs(pM.Y - centros[i].Y) <= tolM)
                return (AsaRedim)i;
        return null;
    }

    private Point MetrosAPixel(double mx, double my)
    {
        return new Point(mx * _scale + _tx, my * _scale + _ty);
    }

    private Point PixelAMetros(Point p)
    {
        return new Point((p.X - _tx) / _scale, (p.Y - _ty) / _scale);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var mpos = e.GetPosition(this);
        var beforeZoom = PixelAMetros(mpos);

        double factor = e.Delta.Y > 0 ? 1.15 : 0.85;
        _scale = Math.Clamp(_scale * factor, 5.0, 300.0);

        _tx = mpos.X - beforeZoom.X * _scale;
        _ty = mpos.Y - beforeZoom.Y * _scale;

        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pointer = e.GetCurrentPoint(this);
        _lastMousePos = pointer.Position;

        if (pointer.Properties.IsMiddleButtonPressed || pointer.Properties.IsRightButtonPressed)
        {
            // UI1.8: click derecho sobre un borde ⇒ menú contextual (no pan).
            if (pointer.Properties.IsRightButtonPressed && Nivel != null)
            {
                var pM = PixelAMetros(pointer.Position);
                foreach (var sistema in Nivel.Sistemas)
                {
                    var hb = LosasPlus.Services.BordesPlantaService.HitTestBorde(pM.X, pM.Y, sistema, 12.0 / _scale);
                    if (hb is { } bl)
                    {
                        AbrirMenuBorde(bl);
                        e.Handled = true;
                        return;
                    }
                }
            }
            _isPanning = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (pointer.Properties.IsLeftButtonPressed)
        {
            var pM = PixelAMetros(pointer.Position);

            if (ActiveTool == "CalibrarPdf")
            {
                if (_calP1M is null)
                {
                    _calP1M = pM;     // P₁ = pivote de la homotecia
                    _calP2M = pM;
                }
                else
                {
                    var p1 = _calP1M.Value;
                    var p2 = AplicarOrtoCalibracion(p1, pM, e.KeyModifiers);
                    double dist = Math.Sqrt((p2.X - p1.X) * (p2.X - p1.X) + (p2.Y - p1.Y) * (p2.Y - p1.Y));
                    CancelarCalibracionPdf();
                    CalibracionPdfLista?.Invoke(p1, dist);
                }
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (ActiveTool == "MapearLosa")
            {
                if (PlanoDxf is { EstaVacio: false } plano)
                {
                    var mundo = PlantaAPlano(plano, pM);
                    PolilineaCad? hit = null;
                    foreach (var ent in plano.Entidades)
                        if (ent is PolilineaCad poli && poli.Cerrada
                            && LosasPlus.Services.PoligonoLosaMapper.ContienePunto(poli, mundo))
                        {
                            hit = poli;
                            break;
                        }
                    PoligonoMapeado?.Invoke(hit);
                }
                e.Handled = true;
                return;
            }

            if (ActiveTool == "ConectarBordes")
            {
                if (HitTest(pM, out _) is Losa losaClic)
                {
                    if (_primerLosaIdBorde is null)
                        _primerLosaIdBorde = losaClic.Id;
                    else if (_primerLosaIdBorde.Value != losaClic.Id)
                    {
                        BordeConexionSolicitada?.Invoke(_primerLosaIdBorde.Value, losaClic.Id);
                        _primerLosaIdBorde = null;
                    }
                    else _primerLosaIdBorde = null;
                    InvalidateVisual();
                }
                e.Handled = true;
                return;
            }

            if (ActiveTool == "Puntero")
            {
                // Asa de resize de la losa seleccionada (UI1.5) — prioridad
                // sobre el hit-test general.
                if (SelectedElement is Losa losaSel
                    && AsaEnPunto(losaSel, pM, 10.0 / _scale) is { } asa)
                {
                    _resizeLosa = losaSel;
                    _resizeAsa = asa;
                    _resizeRectOriginal = new RectM(losaSel.CoordenadaX, losaSel.CoordenadaY, losaSel.Lx, losaSel.Ly);
                    GestoEdicionIniciado?.Invoke();
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }

                // Hit test using raw mouse coords
                object? hit = HitTest(pM, out int endpointHit);
                SelectedElement = hit;

                if (hit != null)
                {
                    _isDragging = true;
                    _dragEndpoint = endpointHit;
                    _dragStartPos = pM;
                    GestoEdicionIniciado?.Invoke();   // snapshot de Undo del gesto (UI1.5)
                    if (hit is Losa l)
                    {
                        _dragStartElementX = l.CoordenadaX;
                        _dragStartElementY = l.CoordenadaY;
                    }
                    else if (hit is Viga v)
                    {
                        _dragStartElementX = _dragEndpoint == 1 ? v.ExtremoX : v.OrigenX;
                        _dragStartElementY = _dragEndpoint == 1 ? v.ExtremoY : v.OrigenY;
                    }
                    else if (hit is Columna c)
                    {
                        _dragStartElementX = c.CoordenadaX;
                        _dragStartElementY = c.CoordenadaY;
                    }
                    else if (hit is Muro m)
                    {
                        var ext = _dragEndpoint == 1 ? m.PuntoFin : m.PuntoInicio;
                        _dragStartElementX = ext.X;   // posición original del extremo arrastrado (eje Shift)
                        _dragStartElementY = ext.Y;
                    }
                    else if (hit is EjeEstructural eje)
                    {
                        _dragStartElementX = _dragEndpoint == 1 ? eje.PuntoFin.X : eje.PuntoInicio.X;
                        _dragStartElementY = _dragEndpoint == 1 ? eje.PuntoFin.Y : eje.PuntoInicio.Y;
                    }
                    e.Pointer.Capture(this);
                }
                else
                {
                    SelectedElement = null;
                }
                e.Handled = true;
            }
            else if (Nivel != null)
            {
                // Snap coordinates on placement
                var snapResult = PlantaSnapEngine.CalculateSnap(
                    pM.X, pM.Y,
                    Nivel, IsSnappingEnabled, StepGrid, 15.0 / _scale);

                double sx = snapResult.X;
                double sy = snapResult.Y;

                if (ActiveTool == "Losa")
                {
                    if (Nivel.Sistemas.Count == 0)
                    {
                        Nivel.Sistemas.Add(new Sistema { Nombre = "Sistema 1" });
                    }
                    var sys = Nivel.Sistemas[0];
                    int newId = sys.Losas.Count > 0 ? sys.Losas.Max(l => l.Id) + 1 : 1;
                    var losa = new Losa
                    {
                        Id = newId,
                        CoordenadaX = sx,
                        CoordenadaY = sy,
                        Anclada = true,   // colocada por click: posición explícita (UI1.1)
                        Lx = 4.0,
                        Ly = 4.0,
                        Espesor = 0.12,
                        Carga = 2.0,
                        Tipo = 10
                    };
                    sys.Losas.Add(losa);
                    SelectedElement = losa;
                }
                else if (ActiveTool == "Viga")
                {
                    int newId = Nivel.Vigas.Count > 0 ? Nivel.Vigas.Max(v => v.Id) + 1 : 1;
                    var viga = new Viga
                    {
                        Id = newId,
                        Nombre = $"V-{newId}",
                        OrigenX = sx,
                        OrigenY = sy,
                        AnguloGrados = 0
                    };
                    viga.Tramos.Add(new TramoViga { Longitud = 5.0 });
                    viga.Apoyos.Add(new ApoyoViga { CoordenadaX = 0.0 });
                    viga.Apoyos.Add(new ApoyoViga { CoordenadaX = 5.0 });
                    Nivel.Vigas.Add(viga);
                    SelectedElement = viga;
                }
                else if (ActiveTool == "Columna")
                {
                    int newId = Nivel.Columnas.Count > 0 ? Nivel.Columnas.Max(c => c.Id) + 1 : 1;
                    var col = new Columna
                    {
                        Id = newId,
                        Nombre = $"C-{newId}",
                        CoordenadaX = sx,
                        CoordenadaY = sy,
                        Base = 0.30,
                        Peralte = 0.30,
                        Altura = 3.0
                    };
                    Nivel.Columnas.Add(col);
                    SelectedElement = col;
                }
                else if (ActiveTool == "Eje")
                {
                    if (Edificio != null)
                    {
                        int count = Edificio.Ejes.Count + 1;
                        var eje = new EjeEstructural
                        {
                            Etiqueta = $"E{count}",
                            PuntoInicio = new PuntoCad(sx, sy),
                            PuntoFin = new PuntoCad(sx + 5.0, sy)
                        };
                        Edificio.Ejes.Add(eje);
                        SelectedElement = eje;
                    }
                }
                else if (ActiveTool == "Muro")
                {
                    if (Nivel.Sistemas.Count == 0)
                        Nivel.Sistemas.Add(new Sistema { Nombre = "Sistema 1" });
                    var sys = Nivel.Sistemas[0];
                    int newId = sys.Muros.Count > 0 ? sys.Muros.Max(m => m.Id) + 1 : 1;
                    var muro = new Muro
                    {
                        Id = newId,
                        PuntoInicio = new PuntoCad(sx, sy),
                        PuntoFin = new PuntoCad(sx + 3.0, sy),
                        Espesor = EspesorMuroNuevo,
                        Altura = AlturaMuroNueva
                    };
                    sys.Muros.Add(muro);
                    SelectedElement = muro;
                }

                // Switch tool back to Puntero
                ActiveTool = "Puntero";
                SelectionChanged?.Invoke(SelectedElement);
                InvalidateVisual();
                e.Handled = true;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pointer = e.GetCurrentPoint(this);
        var mpos = pointer.Position;
        var pM = PixelAMetros(mpos);
        _mousePosForSnap = pM;

        if (ActiveTool == "CalibrarPdf" && _calP1M is { } calP1)
        {
            _calP2M = AplicarOrtoCalibracion(calP1, pM, e.KeyModifiers);
            InvalidateVisual();
        }

        if (_resizeLosa is { } losaResize)
        {
            var snapRes = PlantaSnapEngine.CalculateSnap(
                pM.X, pM.Y, Nivel, IsSnappingEnabled, StepGrid, 15.0 / _scale);
            var r = GeometriaEdicion.Redimensionar(
                _resizeRectOriginal, _resizeAsa, new PuntoM(snapRes.X, snapRes.Y), ladoMin: 0.10);
            losaResize.CoordenadaX = r.X;
            losaResize.CoordenadaY = r.Y;
            losaResize.Lx = r.Ancho;
            losaResize.Ly = r.Alto;
            losaResize.Anclada = true;   // redimensionada a mano: posición explícita
            InvalidateVisual();
            return;
        }

        if (_isPanning)
        {
            _tx += mpos.X - _lastMousePos.X;
            _ty += mpos.Y - _lastMousePos.Y;
            _lastMousePos = mpos;
            InvalidateVisual();
        }
        else if (_isDragging && SelectedElement != null)
        {
            double dx = pM.X - _dragStartPos.X;
            double dy = pM.Y - _dragStartPos.Y;

            double rawTargetX = _dragStartElementX + dx;
            double rawTargetY = _dragStartElementY + dy;

            // Calculate snapped coordinates based on target destination
            var snapResult = PlantaSnapEngine.CalculateSnap(
                rawTargetX, rawTargetY,
                Nivel, IsSnappingEnabled, StepGrid, 15.0 / _scale);

            if (SelectedElement is Losa l)
            {
                l.CoordenadaX = snapResult.X;
                l.CoordenadaY = snapResult.Y;
                // Arrastrada a mano ⇒ anclada: el LayoutSolver no la reubica y el
                // lienzo CAD invalida su caché (TopologiaPlanta observa Anclada).
                l.Anclada = true;
            }
            else if (SelectedElement is Viga v)
            {
                if (_dragEndpoint == 0)
                {
                    v.OrigenX = snapResult.X;
                    v.OrigenY = snapResult.Y;
                    // Recalculate length/angle
                    double nx = v.ExtremoX - v.OrigenX;
                    double ny = v.ExtremoY - v.OrigenY;
                    double antes = v.LongitudTotal;
                    v.AnguloGrados = Math.Atan2(ny, nx) * 180 / Math.PI;
                    if (v.Tramos.Count > 0) v.Tramos[0].Longitud = Math.Sqrt(nx * nx + ny * ny);
                    v.ReescalarApoyos(antes);   // los apoyos siguen a la viga
                }
                else if (_dragEndpoint == 1)
                {
                    // Update endpoint by modifying angle and length
                    double nx = snapResult.X - v.OrigenX;
                    double ny = snapResult.Y - v.OrigenY;
                    double antes = v.LongitudTotal;
                    v.AnguloGrados = Math.Atan2(ny, nx) * 180 / Math.PI;
                    if (v.Tramos.Count > 0) v.Tramos[0].Longitud = Math.Sqrt(nx * nx + ny * ny);
                    v.ReescalarApoyos(antes);   // los apoyos siguen a la viga
                }
                else
                {
                    v.OrigenX = snapResult.X;
                    v.OrigenY = snapResult.Y;
                }
            }
            else if (SelectedElement is Columna c)
            {
                c.CoordenadaX = snapResult.X;
                c.CoordenadaY = snapResult.Y;
            }
            else if (SelectedElement is Muro m)
            {
                double dxStart = snapResult.X - m.PuntoInicio.X;
                double dyStart = snapResult.Y - m.PuntoInicio.Y;
                m.PuntoInicio = new PuntoCad(m.PuntoInicio.X + dxStart, m.PuntoInicio.Y + dyStart);
                m.PuntoFin = new PuntoCad(m.PuntoFin.X + dxStart, m.PuntoFin.Y + dyStart);
            }
            else if (SelectedElement is EjeEstructural eje)
            {
                if (_dragEndpoint == 0)
                {
                    eje.PuntoInicio = new PuntoCad(snapResult.X, snapResult.Y);
                }
                else if (_dragEndpoint == 1)
                {
                    eje.PuntoFin = new PuntoCad(snapResult.X, snapResult.Y);
                }
                else
                {
                    double dxStart = snapResult.X - eje.PuntoInicio.X;
                    double dyStart = snapResult.Y - eje.PuntoInicio.Y;
                    eje.PuntoInicio = new PuntoCad(eje.PuntoInicio.X + dxStart, eje.PuntoInicio.Y + dyStart);
                    eje.PuntoFin = new PuntoCad(eje.PuntoFin.X + dxStart, eje.PuntoFin.Y + dyStart);
                }
            }

            SelectionChanged?.Invoke(SelectedElement);
            InvalidateVisual();
        }
        else
        {
            // Just hovering, invalidate to update the snap marker tooltip
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isPanning = false;
        _isDragging = false;
        _resizeLosa = null;   // fin del gesto de resize (UI1.5)
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _mousePosForSnap = null;
        InvalidateVisual();
    }

    private object? HitTest(Point p, out int endpointHit)
    {
        endpointHit = 2;
        if (Nivel == null) return null;

        double tolE = 8.0 / _scale;

        // 1. Columns (highest hit priority because they are small)
        foreach (var col in Nivel.Columnas)
        {
            double halfB = col.Base * 0.5;
            double halfP = col.Peralte * 0.5;
            if (p.X >= col.CoordenadaX - halfB && p.X <= col.CoordenadaX + halfB &&
                p.Y >= col.CoordenadaY - halfP && p.Y <= col.CoordenadaY + halfP)
            {
                return col;
            }
        }

        // 2. Beams (line segment)
        foreach (var viga in Nivel.Vigas)
        {
            var a = new Point(viga.OrigenX, viga.OrigenY);
            var b = new Point(viga.ExtremoX, viga.ExtremoY);
            if (DistancePoint(p, a) <= tolE) { endpointHit = 0; return viga; }
            if (DistancePoint(p, b) <= tolE) { endpointHit = 1; return viga; }

            double dist = DistanceToSegment(p, a, b);
            if (dist <= 0.3) // tolerance in meters
            {
                return viga;
            }
        }

        // 3. Walls (line segments with thickness) — antes que las losas: un
        // muro pisa la losa de abajo y debe ganar el click/drag (UI1.7).
        // Extremos detectados como asas (endpointHit 0/1) — patrón Viga/Eje (UI1.10).
        foreach (var sistema in Nivel.Sistemas)
        {
            foreach (var muro in sistema.Muros)
            {
                var a = new Point(muro.PuntoInicio.X, muro.PuntoInicio.Y);
                var b = new Point(muro.PuntoFin.X, muro.PuntoFin.Y);
                if (DistancePoint(p, a) <= tolE) { endpointHit = 0; return muro; }
                if (DistancePoint(p, b) <= tolE) { endpointHit = 1; return muro; }

                double dist = DistanceToSegment(p, a, b);
                if (dist <= muro.Espesor / 2.0 + 0.1) // tolerance + half thickness in meters
                {
                    return muro;
                }
            }
        }

        // 4. Slabs (filled rectangles)
        foreach (var sistema in Nivel.Sistemas)
        {
            foreach (var losa in sistema.Losas)
            {
                if (p.X >= losa.CoordenadaX && p.X <= losa.CoordenadaX + losa.Lx &&
                    p.Y >= losa.CoordenadaY && p.Y <= losa.CoordenadaY + losa.Ly)
                {
                    return losa;
                }
            }
        }

        // 5. Ejes (líneas)
        if (Edificio != null)
        {
            foreach (var eje in Edificio.Ejes)
            {
                var a = new Point(eje.PuntoInicio.X, eje.PuntoInicio.Y);
                var b = new Point(eje.PuntoFin.X, eje.PuntoFin.Y);
                if (DistancePoint(p, a) <= tolE) { endpointHit = 0; return eje; }
                if (DistancePoint(p, b) <= tolE) { endpointHit = 1; return eje; }

                double dist = DistanceToSegment(p, a, b);
                if (dist <= 0.5) // tolerance in meters
                {
                    return eje;
                }
            }
        }

        return null;
    }

    private static double DistancePoint(Point p, Point a)
    {
        return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double l2 = (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
        if (l2 == 0) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = ((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / l2;
        t = Math.Clamp(t, 0.0, 1.0);
        double prx = a.X + t * (b.X - a.X);
        double pry = a.Y + t * (b.Y - a.Y);
        return Math.Sqrt((p.X - prx) * (p.X - prx) + (p.Y - pry) * (p.Y - pry));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Draw grid lines
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)), 1.0);
        var boldGridPen = new Pen(new SolidColorBrush(Color.FromArgb(45, 128, 128, 128)), 1.5);

        double w = Bounds.Width;
        double h = Bounds.Height;

        // Draw vertical grid lines
        double minM_x = PixelAMetros(new Point(0, 0)).X;
        double maxM_x = PixelAMetros(new Point(w, 0)).X;
        int startX = (int)Math.Floor(minM_x);
        int endX = (int)Math.Ceiling(maxM_x);

        for (int x = startX; x <= endX; x++)
        {
            var p1 = MetrosAPixel(x, PixelAMetros(new Point(0, 0)).Y);
            var p2 = MetrosAPixel(x, PixelAMetros(new Point(0, h)).Y);
            context.DrawLine(x % 5 == 0 ? boldGridPen : gridPen, p1, p2);
        }

        // Draw horizontal grid lines
        double minM_y = PixelAMetros(new Point(0, 0)).Y;
        double maxM_y = PixelAMetros(new Point(0, h)).Y;
        int startY = (int)Math.Floor(minM_y);
        int endY = (int)Math.Ceiling(maxM_y);

        for (int y = startY; y <= endY; y++)
        {
            var p1 = MetrosAPixel(PixelAMetros(new Point(0, 0)).X, y);
            var p2 = MetrosAPixel(PixelAMetros(new Point(w, 0)).X, y);
            context.DrawLine(y % 5 == 0 ? boldGridPen : gridPen, p1, p2);
        }

        // Underlay PDF (capa de referencia importada) — debajo de la estructura,
        // para calcar losas/columnas/ejes sobre el plano arquitectónico.
        if (FondoPdf is { } pdfBmp && PdfRef is { } pdfRef && !pdfRef.EstaVacio)
        {
            var tl = MetrosAPixel(pdfRef.OffsetX, pdfRef.OffsetY);
            var br = MetrosAPixel(pdfRef.OffsetX + pdfRef.Ancho * pdfRef.Escala,
                                  pdfRef.OffsetY + pdfRef.Alto * pdfRef.Escala);
            var dest = new Rect(Math.Min(tl.X, br.X), Math.Min(tl.Y, br.Y),
                                Math.Abs(br.X - tl.X), Math.Abs(br.Y - tl.Y));
            using (context.PushOpacity(Math.Clamp(OpacidadPdf, 0.0, 1.0)))
                context.DrawImage(pdfBmp, dest);
        }

        // Underlay DXF (calco vectorial) — líneas y polilíneas, debajo de la estructura.
        // UI1.3: con flip-Y vía PlanoAPlanta — la misma convención que las losas
        // importadas del DXF (antes el plano se veía espejado verticalmente).
        if (PlanoDxf is { } plano && !plano.EstaVacio)
        {
            var dxfPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 90, 90, 90)), 1.0);
            Point W(PuntoCad pt)
            {
                var pl = PlanoAPlanta(plano, pt);
                return MetrosAPixel(pl.X, pl.Y);
            }
            foreach (var ent in plano.Entidades)
            {
                if (ent is LineaCad ln)
                    context.DrawLine(dxfPen, W(ln.Inicio), W(ln.Fin));
                else if (ent is PolilineaCad pl && pl.Vertices.Count > 1)
                    for (int i = 1; i < pl.Vertices.Count; i++)
                        context.DrawLine(dxfPen, W(pl.Vertices[i - 1]), W(pl.Vertices[i]));
            }
        }

        if (Nivel == null) return;

        // 1. Draw Slabs
        var slabFill = new SolidColorBrush(Color.FromArgb(45, 0x2E, 0x7D, 0x32));
        var slabPen = new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), 2.0);
        var selectPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)), 3.0);

        var normalTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);
        var boldTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyle.Normal, FontWeight.Bold, FontStretch.Normal);

        foreach (var sistema in Nivel.Sistemas)
        {
            foreach (var losa in sistema.Losas)
            {
                var pTopLeft = MetrosAPixel(losa.CoordenadaX, losa.CoordenadaY);
                double sw = losa.Lx * _scale;
                double sh = losa.Ly * _scale;
                var rect = new Rect(pTopLeft.X, pTopLeft.Y, sw, sh);

                bool isSelected = ReferenceEquals(SelectedElement, losa);
                context.DrawRectangle(slabFill, isSelected ? selectPen : slabPen, rect);

                // Label
                double fs = Math.Clamp(sh * 0.15, 10.0, 16.0);
                if (sw > 30 && sh > 30)
                {
                    var ft1 = new FormattedText($"Losa {losa.Id}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, boldTypeface, fs, Brushes.DarkGreen);
                    var ft2 = new FormattedText($"{losa.Lx:0.00}x{losa.Ly:0.00}m", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, normalTypeface, fs * 0.8, Brushes.DarkGreen);
                    context.DrawText(ft1, new Point(rect.X + (rect.Width - ft1.Width) / 2, rect.Y + rect.Height / 2 - ft1.Height));
                    context.DrawText(ft2, new Point(rect.X + (rect.Width - ft2.Width) / 2, rect.Y + rect.Height / 2));
                }
            }
        }

        // === UI1.8: capa de bordes de continuidad (sobre losas, bajo muros/columnas) ===
        var hachuraPen = new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), 1.5);
        var conectorS  = new Pen(new SolidColorBrush(Color.FromRgb(0x3F, 0x51, 0xB5)), 3.0); // índigo, sólido = Balanceo S
        var conectorN  = new Pen(new SolidColorBrush(Color.FromRgb(0x3F, 0x51, 0xB5)), 3.0)  // discontinuo = Balanceo N
            { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };

        foreach (var sistema in Nivel.Sistemas)
        {
            // 6a. Hachura por arista (solo lectura, desde losa.Tipo)
            foreach (var losa in sistema.Losas)
            {
                var centroM = new Point(losa.CoordenadaX + losa.Lx / 2, losa.CoordenadaY + losa.Ly / 2);
                foreach (var ar in LosasPlus.Services.BordesPlantaService.HachuraAristas(losa))
                    DibujarHachuraArista(context, hachuraPen, ar, centroM);
            }

            // 6b. Conectores de continuidad (uno por BordeAdic)
            foreach (var b in sistema.BordesX) DibujarConector(context, sistema, b, conectorS, conectorN);
            foreach (var b in sistema.BordesY) DibujarConector(context, sistema, b, conectorS, conectorN);
        }

        // 2. Draw Beams
        var beamBrush = new SolidColorBrush(Color.FromRgb(0x19, 0x76, 0xD2));
        var beamPen = new Pen(beamBrush, 4.0);
        var beamSelectPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)), 6.0);

        foreach (var viga in Nivel.Vigas)
        {
            var pStart = MetrosAPixel(viga.OrigenX, viga.OrigenY);
            var pEnd = MetrosAPixel(viga.ExtremoX, viga.ExtremoY);

            bool isSelected = ReferenceEquals(SelectedElement, viga);
            context.DrawLine(isSelected ? beamSelectPen : beamPen, pStart, pEnd);

            // Label
            var ft = new FormattedText(viga.Nombre, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, boldTypeface, 11.0, Brushes.DarkBlue);
            var mid = new Point((pStart.X + pEnd.X) / 2.0, (pStart.Y + pEnd.Y) / 2.0 - 15.0);
            context.DrawText(ft, mid);

            // Apoyos puntuales
            if (viga.Apoyos.Count > 0)
            {
                var apoyoBrush = Brushes.Red;
                double dx = viga.ExtremoX - viga.OrigenX;
                double dy = viga.ExtremoY - viga.OrigenY;
                double largo = Math.Sqrt(dx * dx + dy * dy);
                if (largo > 0)
                {
                    foreach (var apoyo in viga.Apoyos)
                    {
                        double t = apoyo.CoordenadaX / largo;
                        double gx = viga.OrigenX + t * dx;
                        double gy = viga.OrigenY + t * dy;
                        var pApoyo = MetrosAPixel(gx, gy);
                        context.DrawEllipse(apoyoBrush, null, pApoyo, 4, 4);
                    }
                }
            }
        }

        // 3. Draw Columns
        var colFill = new SolidColorBrush(Color.FromRgb(0x42, 0x42, 0x42));
        var colPen = new Pen(Brushes.Black, 1.5);

        foreach (var col in Nivel.Columnas)
        {
            double halfB = col.Base * 0.5;
            double halfP = col.Peralte * 0.5;
            var pTopLeft = MetrosAPixel(col.CoordenadaX - halfB, col.CoordenadaY - halfP);
            double cw = col.Base * _scale;
            double ch = col.Peralte * _scale;
            var rect = new Rect(pTopLeft.X, pTopLeft.Y, cw, ch);

            bool isSelected = ReferenceEquals(SelectedElement, col);
            context.DrawRectangle(colFill, isSelected ? selectPen : colPen, rect);

            // Label
            var ft = new FormattedText(col.Nombre, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, boldTypeface, 11.0, Brushes.Black);
            context.DrawText(ft, new Point(rect.Right + 4.0, rect.Y - 4.0));
        }

        // 4. Draw Walls (Muros) — color por espesor (PaletaMuros, UI1.4): misma
        // codificación visual que el lienzo CAD y que la leyenda «Suma de Colores».
        var muroSelectBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)); // Orange

        foreach (var sistema in Nivel.Sistemas)
        {
            foreach (var muro in sistema.Muros)
            {
                var pStart = MetrosAPixel(muro.PuntoInicio.X, muro.PuntoInicio.Y);
                var pEnd = MetrosAPixel(muro.PuntoFin.X, muro.PuntoFin.Y);

                bool isSelected = ReferenceEquals(SelectedElement, muro);
                var pen = new Pen(isSelected ? muroSelectBrush : PaletaMuros.BrushParaEspesor(muro.Espesor),
                                  muro.Espesor * _scale)
                {
                    LineCap = PenLineCap.Flat
                };
                
                context.DrawLine(pen, pStart, pEnd);

                // Label
                var ft = new FormattedText($"Muro {muro.Id}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, boldTypeface, 11.0, Brushes.SaddleBrown);
                var mid = new Point((pStart.X + pEnd.X) / 2.0, (pStart.Y + pEnd.Y) / 2.0 - (muro.Espesor * _scale) / 2.0 - 15.0);
                context.DrawText(ft, mid);
            }
        }

        // 5. Draw active snap indicator
        if (IsSnappingEnabled && _mousePosForSnap.HasValue)
        {
            var snapRes = PlantaSnapEngine.CalculateSnap(
                _mousePosForSnap.Value.X, _mousePosForSnap.Value.Y,
                Nivel, IsSnappingEnabled, StepGrid, 15.0 / _scale);

            var snapPx = MetrosAPixel(snapRes.X, snapRes.Y);

            // Snapped target marker: crosshair + square
            var snapMarkerPen = new Pen(Brushes.OrangeRed, 1.5);
            context.DrawRectangle(null, snapMarkerPen, new Rect(snapPx.X - 6, snapPx.Y - 6, 12, 12));
            context.DrawLine(snapMarkerPen, new Point(snapPx.X - 10, snapPx.Y), new Point(snapPx.X + 10, snapPx.Y));
            context.DrawLine(snapMarkerPen, new Point(snapPx.X, snapPx.Y - 10), new Point(snapPx.X, snapPx.Y + 10));

            // Tooltip box showing coordinates and origin
            var text = $"{snapRes.Description} ({snapRes.X:0.00}, {snapRes.Y:0.00})m";
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyle.Normal, FontWeight.SemiBold, FontStretch.Normal),
                10.0, Brushes.OrangeRed);

            var tooltipRect = new Rect(snapPx.X + 12, snapPx.Y - 22, ft.Width + 8, ft.Height + 4);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 255, 255, 240)), new Pen(Brushes.OrangeRed, 1.0), tooltipRect);
            context.DrawText(ft, new Point(tooltipRect.X + 4, tooltipRect.Y + 2));
        }

        // 6. Draw Ejes
        if (Edificio != null)
        {
            var ejePen = new Pen(new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)), 1.5)
            {
                DashStyle = DashStyle.DashDot
            };
            var ejeSelectPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)), 2.5)
            {
                DashStyle = DashStyle.DashDot
            };

            foreach (var eje in Edificio.Ejes)
            {
                var pStart = MetrosAPixel(eje.PuntoInicio.X, eje.PuntoInicio.Y);
                var pEnd = MetrosAPixel(eje.PuntoFin.X, eje.PuntoFin.Y);
                
                bool isSelected = ReferenceEquals(SelectedElement, eje);
                context.DrawLine(isSelected ? ejeSelectPen : ejePen, pStart, pEnd);

                // Label in circle at PuntoFin
                double r = 12.0;
                context.DrawEllipse(Brushes.White, isSelected ? ejeSelectPen : ejePen, pEnd, r, r);
                var ftEje = new FormattedText(eje.Etiqueta, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, boldTypeface, 12.0, Brushes.Black);
                context.DrawText(ftEje, new Point(pEnd.X - ftEje.Width / 2, pEnd.Y - ftEje.Height / 2));
            }
        }

        // 7. Asas de redimensionado de la losa seleccionada (UI1.5).
        if (SelectedElement is Losa losaSelAsas)
        {
            var asaBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
            var asaPen = new Pen(Brushes.White, 1.0);
            foreach (var (ax, ay) in CentrosAsas(losaSelAsas))
            {
                var c = MetrosAPixel(ax, ay);
                context.DrawRectangle(asaBrush, asaPen, new Rect(c.X - 4, c.Y - 4, 8, 8));
            }
        }

        // 8. Línea de calibración del PDF (UI1.2) — overlay superior.
        if (ActiveTool == "CalibrarPdf" && _calP1M is { } cp1 && _calP2M is { } cp2)
        {
            var calPen = new Pen(Brushes.OrangeRed, 2.0) { DashStyle = DashStyle.Dash };
            var a = MetrosAPixel(cp1.X, cp1.Y);
            var b = MetrosAPixel(cp2.X, cp2.Y);
            context.DrawLine(calPen, a, b);
            context.DrawEllipse(Brushes.OrangeRed, null, a, 4, 4);
            context.DrawEllipse(Brushes.OrangeRed, null, b, 4, 4);

            double distM = Math.Sqrt((cp2.X - cp1.X) * (cp2.X - cp1.X) + (cp2.Y - cp1.Y) * (cp2.Y - cp1.Y));
            var ftCal = new FormattedText($"{distM:0.000} m", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, boldTypeface, 13.0, Brushes.OrangeRed);
            context.DrawText(ftCal, new Point((a.X + b.X) / 2.0 + 8, (a.Y + b.Y) / 2.0 - 20));
        }
    }

    /// <summary>
    /// Encuadra el lienzo al contenido completo (estructura + underlays
    /// DXF/PDF): aplica el fit de <see cref="EncuadrePlanta"/> sobre
    /// _scale/_tx/_ty y redibuja. No-op sin contenido o sin layout (UI1.7).
    /// </summary>
    public void EncuadrarContenido()
    {
        if (Bounds.Width < 1 || Bounds.Height < 1) return;

        var rect = EncuadrePlanta.CalcularExtents(Nivel, PlanoDxf, PdfRef, incluirUnderlays: true);
        if (rect is not RectM r) return;

        (_scale, _tx, _ty) = EncuadrePlanta.CalcularEncuadre(r, Bounds.Width, Bounds.Height);
        InvalidateVisual();
    }

    /// <summary>
    /// Auto-encuadre tras importar un underlay (UI1.7). Si el binding inicial
    /// llega antes del layout (Bounds aún 0), difiere un ciclo de UI — el
    /// mismo patrón que <see cref="OnModeloCambiado"/>.
    /// </summary>
    private void EncuadrarTrasImport()
    {
        if (Bounds.Width >= 1 && Bounds.Height >= 1)
        {
            EncuadrarContenido();
            return;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(EncuadrarContenido);
    }

    private void DibujarHachuraArista(DrawingContext ctx, Pen pen,
        LosasPlus.Services.AristaHachura ar, Point centroM)
    {
        var p0 = MetrosAPixel(ar.X0, ar.Y0);
        var p1 = MetrosAPixel(ar.X1, ar.Y1);
        double largoPx = Math.Sqrt((p1.X - p0.X) * (p1.X - p0.X) + (p1.Y - p0.Y) * (p1.Y - p0.Y));
        if (largoPx < 16) return;  // zoom lejano: no saturar

        switch (ar.Kind)
        {
            case LosasPlus.Models.BorderKind.Empotrado:
            {
                // ticks ⟂ hacia adentro: la normal "hacia adentro" apunta al centro de la losa.
                double ux = (p1.X - p0.X) / largoPx, uy = (p1.Y - p0.Y) / largoPx;
                double nx = uy, ny = -ux;
                var medio  = new Point((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2);
                var centro = MetrosAPixel(centroM.X, centroM.Y);
                if ((centro.X - medio.X) * nx + (centro.Y - medio.Y) * ny < 0) { nx = -nx; ny = -ny; }
                double tick = Math.Clamp(largoPx * 0.10, 4, 10);
                for (double d = 8; d < largoPx; d += 10)
                {
                    double bx = p0.X + ux * d, by = p0.Y + uy * d;
                    ctx.DrawLine(pen, new Point(bx, by), new Point(bx + nx * tick, by + ny * tick));
                }
                break;
            }
            case LosasPlus.Models.BorderKind.Libre:
            case LosasPlus.Models.BorderKind.Vuelo:
            {
                var dotted = new Pen(pen.Brush, 1.5) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) };
                ctx.DrawLine(dotted, p0, p1);
                break;
            }
            // Apoyado: sin añadido (la arista de la losa ya es línea fina)
        }
    }

    private void DibujarConector(DrawingContext ctx, LosasPlus.Models.Sistema sistema,
        LosasPlus.Models.BordeAdic b, Pen penS, Pen penN)
    {
        var a  = sistema.Losas.FirstOrDefault(l => l.Id == b.BI);
        var bb = sistema.Losas.FirstOrDefault(l => l.Id == b.BJ);
        if (a is null || bb is null) return;

        var seg = LosasPlus.Services.BordesPlantaService.SegmentoCompartido(a, bb);
        Point q0, q1;
        if (seg is { } s) { q0 = MetrosAPixel(s.X0, s.Y0); q1 = MetrosAPixel(s.X1, s.Y1); }
        else
        {
            q0 = MetrosAPixel(a.CoordenadaX + a.Lx / 2, a.CoordenadaY + a.Ly / 2);
            q1 = MetrosAPixel(bb.CoordenadaX + bb.Lx / 2, bb.CoordenadaY + bb.Ly / 2);
        }
        ctx.DrawLine(b.Balanceo == "N" ? penN : penS, q0, q1);
    }

    /// <summary>
    /// Captura la planta como imagen para incrustarla en la exportación a
    /// Excel. UI1.7: encuadra el ENTREGABLE — la estructura manda y los
    /// underlays son fallback — mutando _scale/_tx/_ty solo durante el render
    /// al bitmap (set-render-restore; sin InvalidateVisual la vista en
    /// pantalla no se entera). Sin contenido, render del viewport tal cual.
    /// Si el control no fue realizado aún (export sin haber abierto Planta
    /// 2D), se mide/arregla a 1200×800 como mejor esfuerzo.
    /// </summary>
    public Avalonia.Media.Imaging.Bitmap CaptureCanvasPng()
    {
        double w = Bounds.Width  >= 1 ? Bounds.Width  : 1200;
        double h = Bounds.Height >= 1 ? Bounds.Height : 800;

        // Solo medir/arreglar si el control no fue realizado (export sin haber
        // abierto Planta 2D); sobre un control vivo sería mutar el layout.
        if (Bounds.Width < 1 || Bounds.Height < 1)
        {
            Measure(new Size(w, h));
            Arrange(new Rect(0, 0, w, h));
        }

        var rect = EncuadrePlanta.CalcularExtents(Nivel, PlanoDxf, PdfRef, incluirUnderlays: false)
                ?? EncuadrePlanta.CalcularExtents(Nivel, PlanoDxf, PdfRef, incluirUnderlays: true);

        double escalaAntes = _scale, txAntes = _tx, tyAntes = _ty;
        try
        {
            if (rect is RectM r)
                (_scale, _tx, _ty) = EncuadrePlanta.CalcularEncuadre(r, w, h);

            var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(
                new PixelSize((int)Math.Ceiling(w), (int)Math.Ceiling(h)), new Vector(96, 96));
            rtb.Render(this);
            return rtb;
        }
        finally
        {
            _scale = escalaAntes;
            _tx = txAntes;
            _ty = tyAntes;
        }
    }

    private void AbrirMenuBorde(LosasPlus.Services.BordeLocalizado bl)
    {
        var menu = new ContextMenu();
        var destino = bl.Borde.Balanceo == "S" ? "N" : "S";
        var toggle = new MenuItem { Header = $"Balanceo: {bl.Borde.Balanceo} → {destino}" };
        toggle.Click += (_, __) => AlternarBalanceoSolicitado?.Invoke(bl);
        var del = new MenuItem { Header = "Eliminar borde" };
        del.Click += (_, __) => EliminarBordeSolicitado?.Invoke(bl);
        menu.Items.Add(toggle);
        menu.Items.Add(del);
        menu.PlacementTarget = this;
        menu.Open(this);
    }
}
