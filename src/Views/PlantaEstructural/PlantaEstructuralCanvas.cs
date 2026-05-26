using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LosasPlus.Columnas;
using LosasPlus.Grillas;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Servicios;
using LosasPlus.Services;
using LosasPlus.Topologia;
using LosasPlus.ViewModels;
using LosasPlus.ViewModels.PlantaEstructural;
using LosasPlus.Zapatas;

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
    private readonly DrawingVisual    _layerLosas;
    private readonly DrawingVisual    _layerMuros;
    private readonly DrawingVisual    _layerVigas;
    private readonly DrawingVisual    _layerColumnas;
    private readonly DrawingVisual    _layerZapatas;
    private readonly DrawingVisual    _layerNodos;   // Liga E E3 — indicadores de conexión nodal

    private readonly ScaleTransform     _scale     = new(1.0, -1.0, 0, 0);
    private readonly TranslateTransform _translate = new(0, 0);
    private readonly TransformGroup     _grupo     = new();

    // ===== Estado de interacción (zoom/pan) =====
    private Point? _panStart;
    private double _panInicialX, _panInicialY;

    // ===== Brushes/pens reutilizables =====
    private static readonly Brush BrushFondo       = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    // Liga F1 paso F2: ejes A/B/C × 1/2/3 más visibles sobre fondo
    // oscuro #1A1A1A. Antes: gris #808080 con opacidad 0.5 (apenas
    // perceptible). Ahora: #B0B0B0 con opacidad 0.85 + grosor 1.2.
    private static readonly Pen   PenGrilla        = new(new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)) { Opacity = 0.85 }, 1.2);
    private static readonly Brush BrushEtiquetaEje = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
    private static readonly Brush BrushColumna     = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly Pen   PenColumnaBorde  = new(new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF)), 1.5);
    private static readonly Pen   PenViga          = new(new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)), 4.0);
    private static readonly Pen   PenZapata        = new(new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)), 1.5)
    { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
    private static readonly Brush BrushEtiquetaColumna = Brushes.White;
    private static readonly Brush BrushEtiquetaViga    = new SolidColorBrush(Color.FromRgb(0xFD, 0xBA, 0x74));
    private static readonly Brush BrushLosaRelleno     = new SolidColorBrush(Color.FromArgb(0x60, 0x2E, 0x7D, 0x32));
    private static readonly Pen   PenLosaBorde         = new(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), 1.5);
    private static readonly Brush BrushEtiquetaLosa    = Brushes.White;
    private static readonly Brush BrushMuro            = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

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
        BrushLosaRelleno.Freeze();
        PenLosaBorde.Freeze();
        BrushEtiquetaLosa.Freeze();
        BrushMuro.Freeze();
    }

    public PlantaEstructuralCanvas()
    {
        // Construir las 5 capas con el TransformGroup compartido.
        _grupo.Children.Add(_scale);
        _grupo.Children.Add(_translate);

        _layerFondo    = NuevaCapa();
        _layerGrilla   = NuevaCapa(_grupo);
        _layerLosas    = NuevaCapa(_grupo);
        _layerMuros    = NuevaCapa(_grupo);
        _layerVigas    = NuevaCapa(_grupo);
        _layerColumnas = NuevaCapa(_grupo);
        _layerZapatas  = NuevaCapa(_grupo);
        _layerNodos    = NuevaCapa(_grupo);  // E3: encima de columnas

        // Orden Z (de abajo hacia arriba en la pantalla):
        //   Fondo → Grilla → Losas → Muros → Zapatas → Vigas → Columnas → Nodos
        // Los nodos van encima de todo para ser visibles como indicadores
        // de conexión (verde/naranja según incidentes en el grafo).
        _children = new VisualCollection(this)
        {
            _layerFondo,
            _layerGrilla,
            _layerLosas,
            _layerMuros,
            _layerZapatas,
            _layerVigas,
            _layerColumnas,
            _layerNodos,
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

    public static readonly DependencyProperty SistemaActivoProperty =
        DependencyProperty.Register(nameof(SistemaActivo), typeof(Sistema), typeof(PlantaEstructuralCanvas),
            new PropertyMetadata(null, (d, _) => ((PlantaEstructuralCanvas)d).Redibujar()));

    public static readonly DependencyProperty HerramientaActivaProperty =
        DependencyProperty.Register(nameof(HerramientaActiva), typeof(ModoHerramientaPlanta), typeof(PlantaEstructuralCanvas),
            new FrameworkPropertyMetadata(ModoHerramientaPlanta.Seleccion,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, _) => ((PlantaEstructuralCanvas)d).ResetEstadoEdicion()));

    public static readonly DependencyProperty MostrarIndicadoresNodalesProperty =
        DependencyProperty.Register(nameof(MostrarIndicadoresNodales), typeof(bool), typeof(PlantaEstructuralCanvas),
            new PropertyMetadata(false, (d, _) => ((PlantaEstructuralCanvas)d).Redibujar()));

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
    public Sistema? SistemaActivo
    { get => (Sistema?)GetValue(SistemaActivoProperty); set => SetValue(SistemaActivoProperty, value); }
    public ModoHerramientaPlanta HerramientaActiva
    { get => (ModoHerramientaPlanta)GetValue(HerramientaActivaProperty); set => SetValue(HerramientaActivaProperty, value); }
    public bool MostrarIndicadoresNodales
    { get => (bool)GetValue(MostrarIndicadoresNodalesProperty); set => SetValue(MostrarIndicadoresNodalesProperty, value); }

    // ===== Estado de la state machine de edición C2 =====
    private DomainKey? _dragKey;
    private Point      _dragOffset;   // offset world meters (cursor → centro elemento)
    private (double X, double Y)? _vigaP1;   // primer click memorizado para CrearViga
    private readonly IAutoria3DService _autoriaService = new GridAutoria3DService();

    private void ResetEstadoEdicion()
    {
        _dragKey  = null;
        _vigaP1   = null;
        Cursor    = Cursors.Arrow;
    }

    // ===== Hit-test internal map: rect en world coords → DomainKey =====
    private readonly List<(Rect WorldRect, DomainKey Key)> _hitRects = new();

    // ===== Render principal =====
    private void Redibujar()
    {
        RenderFondo();
        RenderGrilla();
        RenderLosas();
        RenderMuros();
        RenderZapatas();
        RenderVigas();
        RenderColumnas();
        RenderIndicadoresNodales();

        // Encuadrar al primer render si nunca lo hicimos.
        if (_translate.X == 0 && _translate.Y == 0 && ActualWidth > 0)
            EncuadrarAlContenido();
    }

    // Brushes para los indicadores de conexión (Liga E E3).
    private static readonly Brush BrushNodoConectado =
        new SolidColorBrush(Color.FromArgb(0xCC, 0x22, 0xC5, 0x5E));   // verde
    private static readonly Brush BrushNodoFlotante =
        new SolidColorBrush(Color.FromArgb(0xCC, 0xF9, 0x73, 0x16));   // naranja
    private static readonly Pen   PenNodoBorde = new(Brushes.Black, 0.8);

    /// <summary>
    /// Renderiza círculos en cada intersección del grafo proyectado del
    /// nivel activo: verde si el nodo tiene 2+ elementos incidentes
    /// (correctamente conectado), naranja si tiene exactamente 1
    /// (flotante, posible viga sin columna). Liga E paso E3.
    ///
    /// <para>
    /// El grafo se construye on-demand via
    /// <see cref="GrafoProyectadoBuilder.Construir"/>; sólo se pintan
    /// nodos cuya Z coincide con la cota del nivel activo dentro de
    /// 10 cm (suficiente para diferenciar pisos sin perder nodos por
    /// tolerancia de fusión).
    /// </para>
    /// </summary>
    private void RenderIndicadoresNodales()
    {
        using var dc = _layerNodos.RenderOpen();
        if (!MostrarIndicadoresNodales) return;
        var proy = Proyecto;
        var nivel = NivelActivo;
        if (proy is null || nivel is null) return;

        IGrafoEstructural grafo;
        try { grafo = GrafoProyectadoBuilder.Construir(proy); }
        catch { return; }

        const double radioPx = 7.0;
        double zNivel = nivel.Cota;
        foreach (var nodo in grafo.Nodos)
        {
            // Filtrar por nivel: nodo dentro de ±0.10m de la cota activa.
            if (System.Math.Abs(nodo.Posicion.Z - zNivel) > 0.10) continue;
            bool conectado = nodo.ElementosIncidentes.Count >= 2;
            var brush = conectado ? BrushNodoConectado : BrushNodoFlotante;
            // Posición en el canvas: X/Y directos (mismo sistema que columnas).
            double cx = nodo.Posicion.X * PxPorMetro;
            double cy = nodo.Posicion.Y * PxPorMetro;
            // El radio en píxeles debe ser independiente del zoom — dividir por scale.
            double rWorld = radioPx / System.Math.Abs(_scale.ScaleX);
            dc.DrawEllipse(brush, PenNodoBorde, new Point(cx, cy), rWorld, rWorld);
        }
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

    /// <summary>
    /// Renderiza las losas del <see cref="SistemaActivo"/> usando el
    /// <see cref="LayoutSolver"/> existente (mismo motor que el Lienzo
    /// CAD legacy). Y se NIEGA al colocar en el canvas porque el
    /// LayoutSolver retorna coords con Y growing-down (convención de
    /// pantalla) mientras este canvas usa Y growing-up (convención de
    /// planos estructurales) — la inversión preserva la topología
    /// relativa de las losas.
    /// </summary>
    private void RenderLosas()
    {
        _hitRects.RemoveAll(h => h.Key.Tipo == TipoElemento.Losa);
        using var dc = _layerLosas.RenderOpen();
        var sistema = SistemaActivo;
        if (sistema is null || sistema.Losas.Count == 0) return;

        var resultado = LayoutSolver.Solve(sistema);
        foreach (var p in resultado.Placements)
        {
            // p.X / p.Y son la esquina sup-izquierda en metros con
            // Y growing-down. Para nuestro canvas con Y growing-up,
            // negamos Y y reflejamos también la altura: el "top" en
            // el LayoutSolver corresponde al "bottom" en nuestro
            // canvas. Para conservar la apariencia rectangular,
            // dibujamos la losa con corner en (X, -(Y+Height)).
            double x = p.X * PxPorMetro;
            double y = -(p.Y + p.Losa.Ly) * PxPorMetro;
            double w = p.Losa.Lx * PxPorMetro;
            double h = p.Losa.Ly * PxPorMetro;
            var rect = new Rect(x, y, w, h);
            dc.DrawRectangle(BrushLosaRelleno, PenLosaBorde, rect);

            // Etiqueta centrada — Id + dimensión + tipo.
            string etiqueta = $"{p.Losa.Id}\n{p.Losa.Lx:F1}×{p.Losa.Ly:F1}\nTipo: {p.Losa.Tipo}";
            DibujarEtiqueta(dc, etiqueta, new Point(x + w / 2, y + h / 2), BrushEtiquetaLosa, 9);

            // Hit-test rect en world coords.
            _hitRects.Add((
                new Rect(p.X, -(p.Y + p.Losa.Ly), p.Losa.Lx, p.Losa.Ly),
                new DomainKey(TipoElemento.Losa, p.Losa.Id)));
        }
    }

    /// <summary>
    /// Renderiza los muros del <see cref="SistemaActivo"/> como líneas
    /// gruesas (espesor proporcional a <c>Muro.Espesor</c>). Y se
    /// niega igual que en <see cref="RenderLosas"/>.
    /// </summary>
    private void RenderMuros()
    {
        _hitRects.RemoveAll(h => h.Key.Tipo == TipoElemento.Muro);
        using var dc = _layerMuros.RenderOpen();
        var sistema = SistemaActivo;
        if (sistema is null || sistema.Muros.Count == 0) return;

        foreach (var muro in sistema.Muros)
        {
            double espesorPx = Math.Max(2.0, muro.Espesor * PxPorMetro);
            var pen = new Pen(BrushMuro, espesorPx) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            var p1 = new Point(muro.PuntoInicio.X * PxPorMetro, -muro.PuntoInicio.Y * PxPorMetro);
            var p2 = new Point(muro.PuntoFin.X    * PxPorMetro, -muro.PuntoFin.Y    * PxPorMetro);
            dc.DrawLine(pen, p1, p2);

            // Hit-test: bounding box ampliada por espesor.
            double xMin = Math.Min(muro.PuntoInicio.X, muro.PuntoFin.X) - muro.Espesor / 2;
            double xMax = Math.Max(muro.PuntoInicio.X, muro.PuntoFin.X) + muro.Espesor / 2;
            double yMin = Math.Min(-muro.PuntoInicio.Y, -muro.PuntoFin.Y) - muro.Espesor / 2;
            double yMax = Math.Max(-muro.PuntoInicio.Y, -muro.PuntoFin.Y) + muro.Espesor / 2;
            _hitRects.Add((
                new Rect(xMin, yMin, xMax - xMin, yMax - yMin),
                new DomainKey(TipoElemento.Muro, muro.Id)));
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
        // Pan: rueda media o Ctrl+arrastre — siempre disponible
        // independientemente de la herramienta activa.
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
        if (e.ChangedButton != MouseButton.Left) return;
        Focus();

        var pixelPos = e.GetPosition(this);
        var (xM, yM) = PixelToWorldMetros(pixelPos);

        switch (HerramientaActiva)
        {
            case ModoHerramientaPlanta.Seleccion:
                // Hit-test: si cae en columna/zapata, comenzar drag.
                var hit = BuscarElementoBajoMouse(xM, yM);
                if (hit is { } key)
                {
                    Seleccion?.FijarSeleccion(key, this);
                    if (key.Tipo == TipoElemento.Columna || key.Tipo == TipoElemento.Zapata)
                    {
                        // Capturar centro del elemento para calcular offset del drag.
                        var (cx, cy) = ResolverCentroElemento(key);
                        _dragKey    = key;
                        _dragOffset = new Point(xM - cx, yM - cy);
                        CaptureMouse();
                        Cursor = Cursors.SizeAll;
                    }
                }
                break;

            case ModoHerramientaPlanta.CrearColumna:
                {
                    var nivel = NivelActivo;
                    var grilla = GrillaActiva;
                    if (nivel is null || grilla is null) return;
                    var (sx, sy) = AplicarSnap(xM, yM, grilla.Value);
                    GridCreationEngine.CrearColumnaConSnap(nivel, grilla.Value, sx, sy);
                    Redibujar();
                }
                break;

            case ModoHerramientaPlanta.CrearViga:
                {
                    var nivel = NivelActivo;
                    var grilla = GrillaActiva;
                    if (nivel is null || grilla is null) return;
                    var (sx, sy) = AplicarSnap(xM, yM, grilla.Value);
                    if (_vigaP1 is null)
                    {
                        _vigaP1 = (sx, sy);
                        return;
                    }
                    var p1 = _vigaP1.Value;
                    _vigaP1 = null;
                    _autoriaService.CrearVigaConSnap(nivel, grilla.Value, p1.X, p1.Y, sx, sy);
                    Redibujar();
                }
                break;

            case ModoHerramientaPlanta.BorrarElemento:
                {
                    var hitKey = BuscarElementoBajoMouse(xM, yM);
                    if (hitKey is null) return;
                    var proy = Proyecto;
                    if (proy is null) return;
                    _autoriaService.BorrarElementoPorKey(proy, hitKey.Value);
                    // Limpiar selección si era el borrado.
                    if (Seleccion?.ElementoSeleccionado == hitKey)
                        Seleccion.LimpiarSeleccion(this);
                    Redibujar();
                }
                break;
        }
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_panStart is { } start)
        {
            var p = e.GetPosition(this);
            _translate.X = _panInicialX + (p.X - start.X);
            _translate.Y = _panInicialY + (p.Y - start.Y);
            return;
        }

        // Drag de columna/zapata en modo Selección.
        if (_dragKey is { } key && HerramientaActiva == ModoHerramientaPlanta.Seleccion)
        {
            var (xM, yM) = PixelToWorldMetros(e.GetPosition(this));
            double nuevoX = xM - _dragOffset.X;
            double nuevoY = yM - _dragOffset.Y;
            // Snap default ON, Shift override.
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0 && GrillaActiva is { } g)
            {
                var (sx, sy, _) = GridSnapEngine.CalcularSnapAGrilla(nuevoX, nuevoY, g);
                nuevoX = sx; nuevoY = sy;
            }
            AplicarMovimientoElemento(key, nuevoX, nuevoY);
            Redibujar();
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
            return;
        }
        if (_dragKey is not null)
        {
            _dragKey = null;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            e.Handled = true;
        }
    }

    // ===== Helpers de hit-test, snap y mutación de posición =====

    private (double xM, double yM) PixelToWorldMetros(Point pixelPos)
    {
        double xWorldPx = (pixelPos.X - _translate.X) / _scale.ScaleX;
        double yWorldPx = (pixelPos.Y - _translate.Y) / _scale.ScaleY;
        return (xWorldPx / PxPorMetro, yWorldPx / PxPorMetro);
    }

    private static (double X, double Y) AplicarSnap(double x, double y, GrillaEstructural grilla)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return (x, y);
        var (sx, sy, _) = GridSnapEngine.CalcularSnapAGrilla(x, y, grilla);
        return (sx, sy);
    }

    private DomainKey? BuscarElementoBajoMouse(double xM, double yM)
    {
        for (int i = _hitRects.Count - 1; i >= 0; i--)
        {
            var (rect, key) = _hitRects[i];
            if (rect.Contains(xM, yM)) return key;
        }
        return null;
    }

    private (double X, double Y) ResolverCentroElemento(DomainKey key)
    {
        var nivel = NivelActivo;
        if (nivel is null) return (0, 0);
        switch (key.Tipo)
        {
            case TipoElemento.Columna:
                foreach (var c in nivel.Columnas)
                    if (c.Id == key.Id) return ResolverPosColumna(c, nivel);
                break;
            case TipoElemento.Zapata:
                foreach (var z in nivel.Zapatas)
                    if (z.Id == key.Id) return ResolverPosZapata(z, nivel);
                break;
        }
        return (0, 0);
    }

    private void AplicarMovimientoElemento(DomainKey key, double nuevoX, double nuevoY)
    {
        var nivel = NivelActivo;
        if (nivel is null) return;
        switch (key.Tipo)
        {
            case TipoElemento.Columna:
                foreach (var c in nivel.Columnas)
                {
                    if (c.Id != key.Id) continue;
                    c.PosX = nuevoX;
                    c.PosY = nuevoY;
                    // Si hay zapata pareada (mismo Id en nivel base), arrastrarla también.
                    foreach (var z in nivel.Zapatas)
                        if (z.Id == c.Id) { z.PosX = nuevoX; z.PosY = nuevoY; }
                    return;
                }
                break;
            case TipoElemento.Zapata:
                foreach (var z in nivel.Zapatas)
                {
                    if (z.Id != key.Id) continue;
                    z.PosX = nuevoX;
                    z.PosY = nuevoY;
                    return;
                }
                break;
        }
    }

    /// <summary>
    /// Tecla Delete (cuando el canvas tiene focus) elimina el elemento
    /// seleccionado actualmente en el SeleccionService. Liga C paso C2 —
    /// shortcut clásico CAD.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Delete) return;
        var key = Seleccion?.ElementoSeleccionado;
        if (key is null) return;
        var proy = Proyecto;
        if (proy is null) return;
        _autoriaService.BorrarElementoPorKey(proy, key.Value);
        Seleccion?.LimpiarSeleccion(this);
        Redibujar();
        e.Handled = true;
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
