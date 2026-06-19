using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Vigas;
using LosasPlus.Transmision;

namespace LosasPlus.Views;

public class PlantaCanvas : Control
{
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

    // Selección por caja (rubber-band)
    private bool _isRubberBand;
    private Point _rubberStartM;
    private Point _rubberEndM;

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
            if (_nivel != null)
            {
                if (_nivel is IModeloObservable obs) obs.ModeloCambiado -= OnModeloCambiado;
            }
            
            if (SetAndRaise(NivelProperty, ref _nivel, value))
            {
                if (_nivel is IModeloObservable obs) obs.ModeloCambiado += OnModeloCambiado;
                SelectedElement = null;
                InvalidateVisual();
            }
        }
    }

    private void OnModeloCambiado(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);
    }

    public event Action<object?>? SelectionChanged;

    // Multi-selección. La lista es la fuente única de verdad; SelectedElement expone el
    // elemento "primario" (el último seleccionado) para el panel de propiedades y el
    // arrastre, manteniendo compatible todo el código que ya hacía SelectedElement = x / null.
    private readonly List<object> _seleccion = new();
    public IReadOnlyList<object> Seleccion => _seleccion;

    public object? SelectedElement
    {
        get => _seleccion.Count > 0 ? _seleccion[_seleccion.Count - 1] : null;
        set
        {
            bool yaIgual = _seleccion.Count == (value == null ? 0 : 1)
                           && (value == null || ReferenceEquals(_seleccion[0], value));
            if (yaIgual) return;

            _seleccion.Clear();
            if (value != null) _seleccion.Add(value);
            SelectionChanged?.Invoke(SelectedElement);
            InvalidateVisual();
        }
    }

    private bool EstaSeleccionado(object elemento) =>
        _seleccion.Any(o => ReferenceEquals(o, elemento));

    /// <summary>Agrega o quita un elemento de la selección múltiple (Ctrl+Click).</summary>
    public void AlternarEnSeleccion(object elemento)
    {
        int idx = _seleccion.FindIndex(o => ReferenceEquals(o, elemento));
        if (idx >= 0) _seleccion.RemoveAt(idx);
        else _seleccion.Add(elemento);
        SelectionChanged?.Invoke(SelectedElement);
        InvalidateVisual();
    }

    /// <summary>Reemplaza la selección completa (selección por caja / rubber-band).</summary>
    public void EstablecerSeleccion(IEnumerable<object> elementos)
    {
        _seleccion.Clear();
        _seleccion.AddRange(elementos);
        SelectionChanged?.Invoke(SelectedElement);
        InvalidateVisual();
    }

    public string ActiveTool { get; set; } = "Puntero";

    public PlantaCanvas()
    {
        ClipToBounds = true;
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
            _isPanning = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (pointer.Properties.IsLeftButtonPressed)
        {
            var pM = PixelAMetros(pointer.Position);

            if (ActiveTool == "Puntero")
            {
                // Hit test using raw mouse coords
                object? hit = HitTest(pM);
                bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;

                if (ctrl)
                {
                    // Ctrl+Click: agrega o quita el elemento de la selección múltiple.
                    if (hit != null) AlternarEnSeleccion(hit);
                    e.Handled = true;
                    return;
                }

                if (hit != null)
                {
                    // Click simple: selecciona sólo este elemento y permite arrastrarlo.
                    SelectedElement = hit;
                    _isDragging = true;
                    _dragStartPos = pM;
                    if (hit is Losa l)
                    {
                        _dragStartElementX = l.CoordenadaX;
                        _dragStartElementY = l.CoordenadaY;
                    }
                    else if (hit is Viga v)
                    {
                        _dragStartElementX = v.OrigenX;
                        _dragStartElementY = v.OrigenY;
                    }
                    else if (hit is Columna c)
                    {
                        _dragStartElementX = c.CoordenadaX;
                        _dragStartElementY = c.CoordenadaY;
                    }
                    e.Pointer.Capture(this);
                }
                else
                {
                    // Click en vacío: inicia selección por caja (rubber-band) de losas.
                    SelectedElement = null;
                    _isRubberBand = true;
                    _rubberStartM = pM;
                    _rubberEndM = pM;
                    e.Pointer.Capture(this);
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

        if (_isPanning)
        {
            _tx += mpos.X - _lastMousePos.X;
            _ty += mpos.Y - _lastMousePos.Y;
            _lastMousePos = mpos;
            InvalidateVisual();
        }
        else if (_isRubberBand)
        {
            _rubberEndM = pM;
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
            }
            else if (SelectedElement is Viga v)
            {
                v.OrigenX = snapResult.X;
                v.OrigenY = snapResult.Y;
            }
            else if (SelectedElement is Columna c)
            {
                c.CoordenadaX = snapResult.X;
                c.CoordenadaY = snapResult.Y;
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

        if (_isRubberBand)
        {
            FinalizarSeleccionPorCaja();
            _isRubberBand = false;
        }

        _isPanning = false;
        _isDragging = false;
        e.Pointer.Capture(null);
    }

    // Selecciona todas las losas que la caja (rubber-band) toca. Un arrastre mínimo
    // equivale a un click en vacío y limpia la selección.
    private void FinalizarSeleccionPorCaja()
    {
        if (Nivel == null) return;

        double anchoPx = Math.Abs(_rubberEndM.X - _rubberStartM.X) * _scale;
        double altoPx = Math.Abs(_rubberEndM.Y - _rubberStartM.Y) * _scale;
        if (anchoPx < 4 && altoPx < 4)
        {
            EstablecerSeleccion(Array.Empty<object>());
            return;
        }

        double rx0 = Math.Min(_rubberStartM.X, _rubberEndM.X);
        double ry0 = Math.Min(_rubberStartM.Y, _rubberEndM.Y);
        double rx1 = Math.Max(_rubberStartM.X, _rubberEndM.X);
        double ry1 = Math.Max(_rubberStartM.Y, _rubberEndM.Y);

        var seleccionados = new List<object>();
        foreach (var sistema in Nivel.Sistemas)
        {
            foreach (var losa in sistema.Losas)
            {
                double lx0 = losa.CoordenadaX;
                double ly0 = losa.CoordenadaY;
                double lx1 = lx0 + losa.Lx;
                double ly1 = ly0 + losa.Ly;
                bool intersecta = lx0 <= rx1 && lx1 >= rx0 && ly0 <= ry1 && ly1 >= ry0;
                if (intersecta) seleccionados.Add(losa);
            }
        }
        EstablecerSeleccion(seleccionados);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _mousePosForSnap = null;
        InvalidateVisual();
    }

    private object? HitTest(Point p)
    {
        if (Nivel == null) return null;

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
            double dist = DistanceToSegment(p, a, b);
            if (dist <= 0.3) // tolerance in meters
            {
                return viga;
            }
        }

        // 3. Slabs (filled rectangles)
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

        return null;
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

                bool isSelected = EstaSeleccionado(losa);
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

        // 2. Draw Beams
        var beamBrush = new SolidColorBrush(Color.FromRgb(0x19, 0x76, 0xD2));
        var beamPen = new Pen(beamBrush, 4.0);
        var beamSelectPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)), 6.0);

        foreach (var viga in Nivel.Vigas)
        {
            var pStart = MetrosAPixel(viga.OrigenX, viga.OrigenY);
            var pEnd = MetrosAPixel(viga.ExtremoX, viga.ExtremoY);

            bool isSelected = EstaSeleccionado(viga);
            context.DrawLine(isSelected ? beamSelectPen : beamPen, pStart, pEnd);

            // Label
            var ft = new FormattedText(viga.Nombre, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, boldTypeface, 11.0, Brushes.DarkBlue);
            var mid = new Point((pStart.X + pEnd.X) / 2.0, (pStart.Y + pEnd.Y) / 2.0 - 15.0);
            context.DrawText(ft, mid);
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

            bool isSelected = EstaSeleccionado(col);
            context.DrawRectangle(colFill, isSelected ? selectPen : colPen, rect);

            // Label
            var ft = new FormattedText(col.Nombre, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, boldTypeface, 11.0, Brushes.Black);
            context.DrawText(ft, new Point(rect.Right + 4.0, rect.Y - 4.0));
        }

        // 4. Draw active snap indicator
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

        // 5. Draw rubber-band selection rectangle
        if (_isRubberBand)
        {
            var bp0 = MetrosAPixel(Math.Min(_rubberStartM.X, _rubberEndM.X), Math.Min(_rubberStartM.Y, _rubberEndM.Y));
            var bp1 = MetrosAPixel(Math.Max(_rubberStartM.X, _rubberEndM.X), Math.Max(_rubberStartM.Y, _rubberEndM.Y));
            var bandRect = new Rect(bp0.X, bp0.Y, bp1.X - bp0.X, bp1.Y - bp0.Y);
            var bandFill = new SolidColorBrush(Color.FromArgb(40, 0xFF, 0x98, 0x00));
            var bandPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)), 1.5)
            {
                DashStyle = DashStyle.Dash
            };
            context.DrawRectangle(bandFill, bandPen, bandRect);
        }
    }
}
