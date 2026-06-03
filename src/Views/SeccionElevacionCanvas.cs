using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LosasPlus.Models;
using LosasPlus.Models.Cad;

namespace LosasPlus.Views;

public class SeccionElevacionCanvas : Control
{
    public EjeEstructural? Eje { get; set; }
    public Edificio? Edificio { get; set; }

    private static readonly IPen PenLosa = new Pen(new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)), 2);
    private static readonly IPen PenColumna = new Pen(new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)), 2);
    private static readonly IBrush BrText = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));

    // Pan and zoom
    private double _zoom = 20; // px/m
    private double _panX = 0;
    private double _panY = 0;
    private bool _isDragging = false;
    private Point _lastMousePos;

    public SeccionElevacionCanvas()
    {
        ClipToBounds = true;
    }

    protected override void OnPointerWheelChanged(Avalonia.Input.PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double zoomFactor = e.Delta.Y > 0 ? 1.1 : 0.9;
        var p = e.GetPosition(this);
        _panX = p.X - (p.X - _panX) * zoomFactor;
        _panY = p.Y - (p.Y - _panY) * zoomFactor;
        _zoom *= zoomFactor;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed || e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
        {
            _isDragging = true;
            _lastMousePos = e.GetPosition(this);
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isDragging)
        {
            var p = e.GetPosition(this);
            _panX += p.X - _lastMousePos.X;
            _panY += p.Y - _lastMousePos.Y;
            _lastMousePos = p;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isDragging = false;
        e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Eje == null || Edificio == null) return;

        double w = Bounds.Width;
        double h = Bounds.Height;
        
        // Auto center at first render
        if (_panX == 0 && _panY == 0)
        {
            _panX = 50;
            _panY = h / 2;
        }

        double dx_e = Eje.PuntoFin.X - Eje.PuntoInicio.X;
        double dy_e = Eje.PuntoFin.Y - Eje.PuntoInicio.Y;
        double lenEje = Math.Sqrt(dx_e * dx_e + dy_e * dy_e);
        context.DrawLine(new Pen(Brushes.Gray, 1), 
            new Point(_panX, _panY), 
            new Point(_panX + lenEje * _zoom, _panY));

        double tol = 0.5; // 0.5m tolerance
        foreach (var nivel in Edificio.Niveles)
        {
            foreach (var sistema in nivel.Sistemas)
            {
                double z = sistema.Elevacion;
                
                // Draw Columns
                var cols = SeccionPorEje.Columnas(Eje, nivel.Columnas, tol);
                foreach (var col in cols)
                {
                    double projX = ProyectarEnEje(new PuntoCad(col.CoordenadaX, col.CoordenadaY));
                    if (projX >= 0 && projX <= lenEje)
                    {
                        var p1 = MetrosAPixel(projX, z);
                        var p2 = MetrosAPixel(projX, z - col.Altura);
                        context.DrawLine(PenColumna, p1, p2);
                    }
                }

                // Draw Losas
                var losas = SeccionPorEje.Losas(Eje, sistema.Losas, tol);
                foreach (var losa in losas)
                {
                    double projX = ProyectarEnEje(new PuntoCad(losa.CoordenadaX + losa.Lx/2, losa.CoordenadaY + losa.Ly/2));
                    if (projX >= 0 && projX <= lenEje)
                    {
                        // Draw horizontal line for losa width (approx along axis)
                        var p1 = MetrosAPixel(projX - losa.Lx/2, z);
                        var p2 = MetrosAPixel(projX + losa.Lx/2, z);
                        context.DrawLine(PenLosa, p1, p2);
                    }
                }
            }
        }
    }

    private Point MetrosAPixel(double x, double z)
    {
        return new Point(_panX + x * _zoom, _panY - z * _zoom);
    }

    private double ProyectarEnEje(PuntoCad p)
    {
        var dx = Eje!.PuntoFin.X - Eje.PuntoInicio.X;
        var dy = Eje.PuntoFin.Y - Eje.PuntoInicio.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 == 0) return 0;
        
        var dot = (p.X - Eje.PuntoInicio.X) * dx + (p.Y - Eje.PuntoInicio.Y) * dy;
        var t = dot / len2;
        var projP = new PuntoCad(Eje.PuntoInicio.X + t * dx, Eje.PuntoInicio.Y + t * dy);
        var dxP = projP.X - Eje.PuntoInicio.X;
        var dyP = projP.Y - Eje.PuntoInicio.Y;
        var dist = Math.Sqrt(dxP * dxP + dyP * dyP);
        return dist * Math.Sign(t);
    }
}
