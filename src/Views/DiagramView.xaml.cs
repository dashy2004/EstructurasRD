using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.ViewModels;

namespace LosasPlus.Views;

/// <summary>
/// Vista de esquema 2D del sistema de losas. Usa <see cref="LayoutSolver"/> para
/// reconstruir las posiciones topológicas a partir de las adyacencias I-J declaradas
/// en BordesX/BordesY, y renderiza:
/// <list type="bullet">
///   <item>Cada losa como rectángulo escalado a sus dimensiones reales (m → px).</item>
///   <item>Bordes compartidos remarcados (verde) con la etiqueta del momento de
///         diseño y la disposición cuando hay datos del .TXT importado.</item>
///   <item>Bordes con balanceo "N" en naranja punteado (voladizos / nudos 3 lados).</item>
///   <item>Mfx / Mfy del vano sobre el centro de cada losa cuando estén parseados.</item>
/// </list>
///
/// Soporta interacción:
/// <list type="bullet">
///   <item>Rueda del mouse: zoom in/out centrado en el cursor.</item>
///   <item>Click izquierdo + drag: pan.</item>
///   <item>Botón "Ajustar": encuadra todo el sistema en la vista actual.</item>
/// </list>
/// </summary>
public partial class DiagramView : UserControl
{
    private const double PxPerMeter = 60.0;
    private const double MinScale = 0.1;
    private const double MaxScale = 10.0;

    private bool _isPanning;
    private Point _panStart;
    private double _panStartTx, _panStartTy;

    /// <summary>Modo conexión: click 1 = primera losa, click 2 = segunda → crea BordeAdic.</summary>
    private bool _connectMode;
    private LayoutSolver.Placement? _firstSelected;
    /// <summary>Mantenemos un mapa de placement → rectángulo dibujado para el highlight visual.</summary>
    private readonly Dictionary<int, Rectangle> _losaRects = new();

    public DiagramView()
    {
        InitializeComponent();
        Loaded += (_, __) => Redraw();
        DataContextChanged += (_, __) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Sistema.Losas.CollectionChanged += OnLosasChanged;
                vm.Sistema.BordesX.CollectionChanged += (_, __) => Redraw();
                vm.Sistema.BordesY.CollectionChanged += (_, __) => Redraw();
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(MainViewModel.Sistema)
                                       or nameof(MainViewModel.LosasFiltradas)
                                       or nameof(MainViewModel.TxtContent))
                    {
                        AttachLosaListeners(vm.Sistema.Losas);
                        Redraw();
                    }
                };
                AttachLosaListeners(vm.Sistema.Losas);
                Redraw();
            }
        };
    }

    /// <summary>
    /// Suscribe el listener de redibujo en vivo a cada <see cref="Losa"/> de
    /// la colección. Re-suscribe al cambiar el Sistema activo. Sin esto, el
    /// esquema solo se redibujaba al agregar/quitar losas, no al editar
    /// Lx/Ly/Tipo de una losa existente — ahora sí refleja el cambio mientras
    /// el usuario tipea (split-view del Editor con EsquemaEnVivo).
    /// </summary>
    private void AttachLosaListeners(System.Collections.ObjectModel.ObservableCollection<Losa> losas)
    {
        foreach (var l in losas)
        {
            l.PropertyChanged -= OnLosaPropertyChanged;
            l.PropertyChanged += OnLosaPropertyChanged;
        }
    }

    private void OnLosaPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Losa.Lx) or nameof(Losa.Ly)
                           or nameof(Losa.Tipo) or nameof(Losa.Espesor)
                           or nameof(Losa.Id))
            Redraw();
    }

    private void OnLosasChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (Losa l in e.NewItems) l.PropertyChanged += OnLosaPropertyChanged;
        if (e.OldItems is not null)
            foreach (Losa l in e.OldItems) l.PropertyChanged -= OnLosaPropertyChanged;
        Redraw();
    }

    private void OnRedraw(object sender, RoutedEventArgs e) => Redraw();
    private void OnResetZoom(object sender, RoutedEventArgs e) { Scale.ScaleX = Scale.ScaleY = 1; Translate.X = Translate.Y = 0; }
    private void OnFitToView(object sender, RoutedEventArgs e) => FitToView();

    public void Redraw()
    {
        Canvas.Children.Clear();
        _losaRects.Clear();
        _firstSelected = null;
        if (DataContext is not MainViewModel vm) { StatusText.Text = "Sin sistema"; return; }
        var sistema = vm.Sistema;
        if (sistema.Losas.Count == 0) { StatusText.Text = "Sin losas para dibujar"; return; }

        var layout = LayoutSolver.Solve(sistema);
        var pById = layout.Placements.ToDictionary(p => p.Id);

        // Tamaño del canvas según bbox + margen
        double margenM = 1.0;
        Canvas.Width = (layout.Width + 2 * margenM) * PxPerMeter;
        Canvas.Height = (layout.Height + 2 * margenM) * PxPerMeter;

        // Ejes globales (esquina superior-izquierda con un offset)
        DrawAxes(margenM);

        // Render: bordes externos primero (gris), luego losas (rectángulos), luego bordes compartidos (encima)
        foreach (var p in layout.Placements)
            DrawLosa(p, margenM, sistema);

        // Bordes compartidos según BordesX / BordesY: dibujar línea verde gruesa entre cada par
        foreach (var b in sistema.BordesX) DrawSharedBorder(pById, b, margenM, dirX: true);
        foreach (var b in sistema.BordesY) DrawSharedBorder(pById, b, margenM, dirX: false);

        var nConectadas = layout.Placements.Count(p => !p.Huerfana);
        var nHuerfanas = layout.Placements.Count(p => p.Huerfana);
        StatusText.Text =
            $"{layout.Placements.Count} losas ({nConectadas} conectadas, {nHuerfanas} huérfanas)\n" +
            $"BBox: {layout.Width:0.0} × {layout.Height:0.0} m\n" +
            $"Bordes: {sistema.BordesX.Count} en X, {sistema.BordesY.Count} en Y";

        // Si la primera vez que dibujamos, hacer fit-to-view
        if (Scale.ScaleX == 1 && Translate.X == 0 && Translate.Y == 0)
            Dispatcher.BeginInvoke(new Action(FitToView), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void DrawAxes(double margenM)
    {
        double ox = 0.4 * PxPerMeter, oy = 0.4 * PxPerMeter;
        DrawArrow(new Point(ox, oy), new Point(ox + 60, oy), Brushes.Goldenrod, "X");
        DrawArrow(new Point(ox, oy), new Point(ox, oy + 60), Brushes.DeepSkyBlue, "Y");
    }

    private void DrawLosa(LayoutSolver.Placement p, double margenM, Sistema sistema)
    {
        double x = (p.X + margenM) * PxPerMeter;
        double y = (p.Y + margenM) * PxPerMeter;
        double w = p.Width * PxPerMeter;
        double h = p.Height * PxPerMeter;

        bool dibujadoConSvg = false;

        // Intento 1: si hay SVG embebido para este tipo, lo usamos como fondo escalado
        // a las dimensiones reales de la losa (Stretch=Fill: el cuadrado de catálogo se
        // estira al rectángulo real preservando la semántica visual de los bordes).
        if (TipoLosa.Catalogo.ContainsKey(p.Losa.Tipo))
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/Resources/icons/tipo_{p.Losa.Tipo}.svg",
                                  UriKind.Absolute);
                var info = System.Windows.Application.GetResourceStream(uri);
                if (info != null)
                {
                    info.Stream.Close();
                    var svg = new SharpVectors.Converters.SvgViewbox
                    {
                        Source = uri,
                        Width = w, Height = h,
                        Stretch = Stretch.Fill,
                        IsHitTestVisible = false,
                        Opacity = p.Huerfana ? 0.5 : 1.0,
                    };
                    Canvas.SetLeft(svg, x);
                    Canvas.SetTop(svg, y);
                    Canvas.Children.Add(svg);
                    dibujadoConSvg = true;
                }
            }
            catch { /* fallback abajo */ }
        }

        // Fallback: dibujo programático (fondo + bordes según patrón Apoyado/Empotrado/Libre/Vuelo)
        if (!dibujadoConSvg)
        {
            var box = new Rectangle
            {
                Width = w, Height = h,
                Fill = p.Huerfana
                    ? new SolidColorBrush(Color.FromArgb(140, 0x3D, 0x2D, 0x2D))
                    : new SolidColorBrush(Color.FromRgb(0x25, 0x32, 0x3D)),
                Stroke = null,
            };
            Canvas.SetLeft(box, x);
            Canvas.SetTop(box, y);
            Canvas.Children.Add(box);

            if (TipoLosa.Catalogo.TryGetValue(p.Losa.Tipo, out var tipo))
            {
                DrawTipoBorder(tipo.Bordes[0], x,     y,     x + w, y,     perpInside: new Vector(0, 1));
                DrawTipoBorder(tipo.Bordes[1], x + w, y,     x + w, y + h, perpInside: new Vector(-1, 0));
                DrawTipoBorder(tipo.Bordes[2], x,     y + h, x + w, y + h, perpInside: new Vector(0, -1));
                DrawTipoBorder(tipo.Bordes[3], x,     y,     x,     y + h, perpInside: new Vector(1, 0));
            }
            else
            {
                var fallback = new Rectangle
                {
                    Width = w, Height = h,
                    Fill = Brushes.Transparent,
                    Stroke = Brushes.LightGray, StrokeThickness = 1,
                };
                Canvas.SetLeft(fallback, x);
                Canvas.SetTop(fallback, y);
                Canvas.Children.Add(fallback);
            }
        }

        // Etiqueta principal: ID + TIPO
        var encabezado = $"#{p.Id} · TIPO {p.Losa.Tipo}";
        if (p.Huerfana) encabezado += " · (huérfana)";
        AddLabel(encabezado, x + 6, y + 4, 12, p.Huerfana ? Brushes.LightSalmon : Brushes.White, FontWeights.Bold);

        // Dimensiones
        AddLabel($"Lx={p.Losa.Lx:0.00}", x + 6, y + h - 16, 9.5, Brushes.Goldenrod);
        AddLabel($"Ly={p.Losa.Ly:0.00}", x + w - 60, y + 4, 9.5, Brushes.DeepSkyBlue);

        // Flechas X/Y dentro de la losa: marcan la dirección de las armaduras.
        // CONVENCIÓN F. Perdomo: la armadura "según X" va paralela a la dirección X
        // (resiste Mfx). Por eso la flecha X la dibujamos horizontal y la Y vertical.
        // Importante para el usuario: el acero NO debe ser perpendicular al esfuerzo,
        // tiene que ir paralelo a la luz que está flexando.
        DrawDireccionArrow(new Point(x + w - 36, y + h - 22), 26, 0, Brushes.Goldenrod, "X");      // → horizontal
        DrawDireccionArrow(new Point(x + w - 12, y + h - 36), 0, 26, Brushes.DeepSkyBlue, "Y");    // ↓ vertical

        // Rectángulo transparente clickable encima — para el modo conexión.
        // Lo guardamos en _losaRects para poder resaltarlo al seleccionar.
        var hitbox = new Rectangle
        {
            Width = w, Height = h,
            Fill = Brushes.Transparent,
            Stroke = Brushes.Transparent,
            StrokeThickness = 3,
            Tag = p,
        };
        Canvas.SetLeft(hitbox, x);
        Canvas.SetTop(hitbox, y);
        hitbox.MouseLeftButtonDown += OnLosaClick;
        Canvas.Children.Add(hitbox);
        _losaRects[p.Id] = hitbox;

        // Momentos del vano si están parseados
        double cy = y + h / 2;
        if (p.Losa.Mfx.HasValue) AddLabel($"Mfx={p.Losa.Mfx:0.000}", x + 6, cy - 18, 10.5, Brushes.LightGreen);
        if (p.Losa.Mfy.HasValue) AddLabel($"Mfy={p.Losa.Mfy:0.000}", x + 6, cy - 4, 10.5, Brushes.LightGreen);
        if (p.Losa.MSx.HasValue) AddLabel($"MSx={p.Losa.MSx:0.000}", x + 6, cy + 10, 10.5, Brushes.Salmon);
        if (p.Losa.MSy.HasValue) AddLabel($"MSy={p.Losa.MSy:0.000}", x + 6, cy + 24, 10.5, Brushes.Salmon);
    }

    /// <summary>Dibuja un borde de losa con la convención visual de F. Perdomo:
    /// línea fina = apoyado, gruesa con rayado interior = empotrado, punteada = libre/vuelo.</summary>
    private void DrawTipoBorder(BorderKind kind, double x1, double y1, double x2, double y2, Vector perpInside)
    {
        switch (kind)
        {
            case BorderKind.Apoyado:
                Canvas.Children.Add(new Line
                {
                    X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                    Stroke = Brushes.LightGray, StrokeThickness = 1.2,
                });
                break;
            case BorderKind.Empotrado:
                Canvas.Children.Add(new Line
                {
                    X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                    Stroke = Brushes.MediumSpringGreen, StrokeThickness = 3.0,
                });
                AddDiagramHatchTicks(x1, y1, x2, y2, perpInside);
                break;
            case BorderKind.Libre:
            case BorderKind.Vuelo:
                Canvas.Children.Add(new Line
                {
                    X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                    Stroke = Brushes.Orange, StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection(new[] { 4.0, 3.0 }),
                });
                break;
        }
    }

    private void AddDiagramHatchTicks(double x1, double y1, double x2, double y2, Vector perpInside)
    {
        double dx = x2 - x1, dy = y2 - y1;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 12) return;

        double tickLen = 7.0;
        double spacing = 14.0;
        int n = (int)(length / spacing);
        if (n < 2) return;

        for (int i = 1; i < n; i++)
        {
            double t = (double)i / n;
            double bx = x1 + dx * t;
            double by = y1 + dy * t;
            double ex = bx + perpInside.X * tickLen + (dx / length) * tickLen * 0.6;
            double ey = by + perpInside.Y * tickLen + (dy / length) * tickLen * 0.6;
            Canvas.Children.Add(new Line
            {
                X1 = bx, Y1 = by, X2 = ex, Y2 = ey,
                Stroke = Brushes.MediumSpringGreen, StrokeThickness = 1.2,
            });
        }
    }

    private void DrawSharedBorder(Dictionary<int, LayoutSolver.Placement> pById, BordeAdic b, double margenM, bool dirX)
    {
        if (!pById.TryGetValue(b.BI, out var pi) || !pById.TryGetValue(b.BJ, out var pj)) return;

        // Geometría del segmento compartido (en metros, luego se convierte a px)
        double sx1, sy1, sx2, sy2;
        if (dirX)
        {
            // Borde paralelo a Y: x compartido = max(pi.X+pi.W, pj.X+pj.W) si están alineados,
            // pero más correcto es: el segmento donde se solapan en Y entre los dos rectángulos,
            // sobre la x del borde común. Tomamos el mid entre los dos cuando hay diferencia.
            double xShared = Math.Abs((pi.X + pi.Width) - pj.X) < Math.Abs((pj.X + pj.Width) - pi.X)
                ? (pi.X + pi.Width + pj.X) / 2
                : (pj.X + pj.Width + pi.X) / 2;
            double yTop = Math.Max(pi.Y, pj.Y);
            double yBot = Math.Min(pi.Y + pi.Height, pj.Y + pj.Height);
            sx1 = sx2 = xShared;
            sy1 = yTop; sy2 = yBot;
        }
        else
        {
            // Borde paralelo a X
            double yShared = Math.Abs((pi.Y + pi.Height) - pj.Y) < Math.Abs((pj.Y + pj.Height) - pi.Y)
                ? (pi.Y + pi.Height + pj.Y) / 2
                : (pj.Y + pj.Height + pi.Y) / 2;
            double xLeft = Math.Max(pi.X, pj.X);
            double xRight = Math.Min(pi.X + pi.Width, pj.X + pj.Width);
            sy1 = sy2 = yShared;
            sx1 = xLeft; sx2 = xRight;
        }

        double px1 = (sx1 + margenM) * PxPerMeter;
        double py1 = (sy1 + margenM) * PxPerMeter;
        double px2 = (sx2 + margenM) * PxPerMeter;
        double py2 = (sy2 + margenM) * PxPerMeter;

        // Color según balanceo
        Brush stroke = b.Balanceo == "N" ? Brushes.Orange : Brushes.MediumSpringGreen;
        var line = new Line
        {
            X1 = px1, Y1 = py1, X2 = px2, Y2 = py2,
            Stroke = stroke,
            StrokeThickness = 4.0,
        };
        if (b.Balanceo == "N") line.StrokeDashArray = new DoubleCollection(new[] { 4.0, 3.0 });
        Canvas.Children.Add(line);

        // Etiqueta: Mu I-J + Disponer si está disponible
        double midX = (px1 + px2) / 2;
        double midY = (py1 + py2) / 2;
        var dirLabel = dirX ? "X" : "Y";
        var head = b.MuIJ.HasValue
            ? $"{dirLabel}·{b.BI}-{b.BJ}: Mu={b.MuIJ:0.000}"
            : $"{dirLabel}·{b.BI}-{b.BJ} ({b.Balanceo})";
        AddLabel(head, midX + 4, midY - 14, 10.5, Brushes.Salmon, FontWeights.SemiBold);
        if (!string.IsNullOrEmpty(b.Disponer))
            AddLabel(b.Disponer, midX + 4, midY + 2, 9.5, Brushes.LightGray);
    }

    // ---------- Pan + Zoom ----------

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        const double step = 1.15;
        double factor = e.Delta > 0 ? step : 1 / step;
        double newScale = Math.Clamp(Scale.ScaleX * factor, MinScale, MaxScale);
        if (Math.Abs(newScale - Scale.ScaleX) < 1e-6) return;

        // Zoom centrado en el cursor: ajustar Translate para que el punto bajo el cursor no se mueva.
        var pt = e.GetPosition(Viewport);
        double k = newScale / Scale.ScaleX;
        Translate.X = pt.X - k * (pt.X - Translate.X);
        Translate.Y = pt.Y - k * (pt.Y - Translate.Y);
        Scale.ScaleX = Scale.ScaleY = newScale;

        e.Handled = true;
    }

    private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _panStart = e.GetPosition(Viewport);
        _panStartTx = Translate.X;
        _panStartTy = Translate.Y;
        Viewport.CaptureMouse();
        Viewport.Cursor = Cursors.SizeAll;
    }

    private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        Viewport.ReleaseMouseCapture();
        Viewport.Cursor = Cursors.Cross;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var pt = e.GetPosition(Viewport);
        Translate.X = _panStartTx + (pt.X - _panStart.X);
        Translate.Y = _panStartTy + (pt.Y - _panStart.Y);
    }

    /// <summary>Encuadra todo el contenido del Canvas dentro del Viewport.</summary>
    private void FitToView()
    {
        if (Canvas.Width <= 0 || Canvas.Height <= 0) return;
        double vw = Viewport.ActualWidth;
        double vh = Viewport.ActualHeight;
        if (vw <= 0 || vh <= 0) return;

        double sx = vw / Canvas.Width;
        double sy = vh / Canvas.Height;
        double s = Math.Min(sx, sy) * 0.95;  // 5% de margen
        s = Math.Clamp(s, MinScale, MaxScale);

        Scale.ScaleX = Scale.ScaleY = s;
        // Centrar
        Translate.X = (vw - Canvas.Width * s) / 2;
        Translate.Y = (vh - Canvas.Height * s) / 2;
    }

    // ---------- Helpers de dibujo ----------

    private void DrawArrow(Point from, Point to, Brush brush, string label)
    {
        Canvas.Children.Add(new Line
        {
            X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y,
            Stroke = brush, StrokeThickness = 2,
        });
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double ang = Math.Atan2(dy, dx);
        double cs = Math.Cos(ang), sn = Math.Sin(ang);
        const double headLen = 10.0, headHalfW = 5.0;
        Canvas.Children.Add(new Polygon
        {
            Fill = brush,
            Points = new PointCollection
            {
                to,
                new Point(to.X - headLen * cs - headHalfW * sn, to.Y - headLen * sn + headHalfW * cs),
                new Point(to.X - headLen * cs + headHalfW * sn, to.Y - headLen * sn - headHalfW * cs),
            },
        });
        AddLabel(label, to.X + 6, to.Y - 8, 12, brush, FontWeights.Bold);
    }

    /// <summary>Dibuja una flecha pequeña con etiqueta — usada para indicar la dirección de las armaduras.</summary>
    private void DrawDireccionArrow(Point from, double dx, double dy, Brush brush, string label)
    {
        var to = new Point(from.X + dx, from.Y + dy);
        Canvas.Children.Add(new Line
        {
            X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y,
            Stroke = brush, StrokeThickness = 1.6,
            IsHitTestVisible = false,
        });
        // Punta de flecha
        double ang = Math.Atan2(dy, dx);
        double headLen = 5, headHalfW = 3;
        double cs = Math.Cos(ang), sn = Math.Sin(ang);
        Canvas.Children.Add(new Polygon
        {
            Fill = brush,
            IsHitTestVisible = false,
            Points = new PointCollection
            {
                to,
                new Point(to.X - headLen * cs - headHalfW * sn, to.Y - headLen * sn + headHalfW * cs),
                new Point(to.X - headLen * cs + headHalfW * sn, to.Y - headLen * sn - headHalfW * cs),
            },
        });
        AddLabel(label, to.X + 3, to.Y - 8, 9.5, brush, FontWeights.Bold);
    }

    // ----------------- Modo conexión: click 1 + click 2 → BordeAdic -----------------

    private void OnToggleConectar(object sender, RoutedEventArgs e)
    {
        _connectMode = ConectarModoToggle.IsChecked == true;
        _firstSelected = null;
        Viewport.Cursor = _connectMode ? Cursors.Hand : Cursors.Cross;
        ClearSelectionHighlight();
        StatusText.Text = _connectMode
            ? "MODO CONEXIÓN ACTIVO\nClick en una losa, luego click en otra adyacente."
            : "Click+drag = pan · scroll = zoom";
    }

    private void OnLosaClick(object sender, MouseButtonEventArgs e)
    {
        if (!_connectMode) return;  // sólo procesar si el modo está activo
        if (sender is not Rectangle rect || rect.Tag is not LayoutSolver.Placement p) return;
        if (DataContext is not MainViewModel vm) return;

        if (_firstSelected is null)
        {
            _firstSelected = p;
            HighlightLosa(p.Id, isFirst: true);
            StatusText.Text = $"Primera losa: #{p.Id}\nAhora hacé click en una losa adyacente.";
            e.Handled = true;
            return;
        }

        // Segunda losa: validar adyacencia y crear BordeAdic
        var a = _firstSelected;
        var b = p;
        if (a.Id == b.Id)
        {
            // Toggle: re-click en la misma → cancelar selección
            _firstSelected = null;
            ClearSelectionHighlight();
            StatusText.Text = "Selección cancelada (mismo losa).";
            e.Handled = true;
            return;
        }

        var dir = ResolveAdyacencia(a, b);
        if (dir is null)
        {
            StatusText.Text = $"Las losas #{a.Id} y #{b.Id} no comparten un borde.\nClick en otra para reintentar.";
            e.Handled = true;
            return;
        }

        // Evitar duplicados
        var coll = dir == 'X' ? vm.Sistema.BordesX : vm.Sistema.BordesY;
        bool yaExiste = coll.Any(bb =>
            (bb.BI == a.Id && bb.BJ == b.Id) ||
            (bb.BI == b.Id && bb.BJ == a.Id));
        if (yaExiste)
        {
            StatusText.Text = $"Ya existe el borde #{a.Id}-#{b.Id} según {dir}.";
            vm.Log($"[esquema] borde duplicado #{a.Id}-#{b.Id} (dir {dir}) — ignorado");
        }
        else
        {
            // Asegurar I < J por convención
            int bi = Math.Min(a.Id, b.Id);
            int bj = Math.Max(a.Id, b.Id);
            coll.Add(new BordeAdic { BI = bi, BJ = bj, Balanceo = "S" });
            vm.Log($"[esquema] borde creado: #{bi}-#{bj} según {dir} (balanceo S por defecto)");
            StatusText.Text = $"Borde #{bi}-#{bj} creado según {dir}.";
            vm.RefreshDLContent();
        }

        // Reset para próxima conexión
        _firstSelected = null;
        ClearSelectionHighlight();
        Redraw();  // re-dibujar para mostrar el nuevo borde resaltado en verde
        e.Handled = true;
    }

    /// <summary>Determina si las dos losas son adyacentes y si comparten un borde según X o Y.</summary>
    private static char? ResolveAdyacencia(LayoutSolver.Placement a, LayoutSolver.Placement b)
    {
        const double tol = 0.05;  // tolerancia 5 cm para considerar bordes coincidentes
        bool ladoXAdyacente = Math.Abs((a.X + a.Width) - b.X) < tol || Math.Abs((b.X + b.Width) - a.X) < tol;
        bool ladoYAdyacente = Math.Abs((a.Y + a.Height) - b.Y) < tol || Math.Abs((b.Y + b.Height) - a.Y) < tol;

        // Solapamiento real en el otro eje
        bool solapaY = Math.Min(a.Y + a.Height, b.Y + b.Height) > Math.Max(a.Y, b.Y) + tol;
        bool solapaX = Math.Min(a.X + a.Width,  b.X + b.Width)  > Math.Max(a.X, b.X) + tol;

        if (ladoXAdyacente && solapaY) return 'X';  // borde paralelo a Y, comparten una franja vertical
        if (ladoYAdyacente && solapaX) return 'Y';  // borde paralelo a X, comparten una franja horizontal
        return null;
    }

    private void HighlightLosa(int id, bool isFirst)
    {
        if (_losaRects.TryGetValue(id, out var rect))
        {
            rect.Stroke = isFirst ? Brushes.Yellow : Brushes.MediumSpringGreen;
            rect.StrokeThickness = 4;
        }
    }

    private void ClearSelectionHighlight()
    {
        foreach (var r in _losaRects.Values)
        {
            r.Stroke = Brushes.Transparent;
            r.StrokeThickness = 3;
        }
    }

    /// <summary>
    /// Captura el contenido del Canvas como PNG en su tamaño natural (sin pan/zoom aplicado).
    /// Usado por el exportador XLSX. Hace flicker breve porque resetea el RenderTransform.
    /// Devuelve null si el canvas aún no se midió/dibujó.
    /// </summary>
    public byte[]? CaptureCanvasPng()
    {
        if (Canvas.Width <= 0 || Canvas.Height <= 0) return null;
        // Forzar redibujo para asegurar que el contenido refleje el modelo actual.
        Redraw();

        var origTransform = Canvas.RenderTransform;
        Canvas.RenderTransform = Transform.Identity;
        Canvas.UpdateLayout();

        try
        {
            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(Canvas.Width),
                (int)Math.Ceiling(Canvas.Height),
                96, 96, PixelFormats.Pbgra32);
            rtb.Render(Canvas);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        finally
        {
            Canvas.RenderTransform = origTransform;
        }
    }

    private void AddLabel(string text, double x, double y, double size, Brush brush, FontWeight? weight = null)
    {
        var t = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = size,
            Foreground = brush,
            FontWeight = weight ?? FontWeights.Normal,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
        Canvas.Children.Add(t);
    }
}
