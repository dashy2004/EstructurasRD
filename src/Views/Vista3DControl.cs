using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LosasPlus.Models;
using LosasPlus.Render3D;

namespace LosasPlus.Views;

/// <summary>
/// Viewport 3D alámbrico del edificio (Fase I — reemplazo multiplataforma del
/// visor SharpDX/Direct3D). Es un <see cref="Control"/> de render inmediato
/// (mismo patrón que el lienzo CAD): no usa OpenGL, sino que proyecta la escena
/// 3D a 2D por software (<see cref="Proyector3D"/>) y la dibuja con el
/// <see cref="DrawingContext"/> de Avalonia.
///
/// <para>
/// Compone <see cref="CamaraOrbital"/> + <see cref="EscenaEdificio"/> +
/// <see cref="Proyector3D"/> (todos núcleos puros de src.Core ya testeados).
/// Interacción: arrastrar = orbitar, rueda = acercar/alejar, doble-clic =
/// reencuadrar. El <see cref="Edificio"/> se enlaza desde el MainViewModel.
/// </para>
/// </summary>
public class Vista3DControl : Control
{
    /// <summary>Edificio cuyo massing 3D se dibuja. Nulo → sólo ejes y rejilla.</summary>
    public static readonly StyledProperty<Edificio?> EdificioProperty =
        AvaloniaProperty.Register<Vista3DControl, Edificio?>(nameof(Edificio));

    public Edificio? Edificio
    {
        get => GetValue(EdificioProperty);
        set => SetValue(EdificioProperty, value);
    }

    private const float EjeLongitud = 3f;
    private const float RejillaExtension = 20f;
    private const float RejillaPaso = 1f;

    private static readonly IBrush Fondo = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x1C));
    private static readonly IPen PenRejilla = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x42)), 1);
    private static readonly IPen PenModelo = new Pen(new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xE3)), 1.6);
    private static readonly IPen PenEjeX = new Pen(Brushes.IndianRed, 2);
    private static readonly IPen PenEjeY = new Pen(Brushes.MediumSeaGreen, 2);
    private static readonly IPen PenEjeZ = new Pen(Brushes.CornflowerBlue, 2);
    private static readonly IBrush PincelTexto = new SolidColorBrush(Color.FromRgb(0x8A, 0x97, 0xA8));

    private readonly CamaraOrbital _camara = new();
    private Escena3D _escena = Escena3D.Vacia;
    private bool _encuadrado;
    private bool _arrastrando;
    private Point _ultimoPunto;

    public Vista3DControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EdificioProperty)
        {
            // Refresco en vivo: el binding XAML escribe la StyledProperty vía SetValue
            // y NO pasa por el setter CLR, así que la suscripción a ModeloCambiado debe
            // vivir acá (acá sí corre con binding). Sin esto el 3D no reflejaba los
            // cambios del modelo hasta volver a entrar a la pestaña.
            if (change.OldValue is LosasPlus.Models.IModeloObservable oldObs)
                oldObs.ModeloCambiado -= OnEdificioCambiado;
            if (change.NewValue is LosasPlus.Models.IModeloObservable newObs)
                newObs.ModeloCambiado += OnEdificioCambiado;
            ReconstruirEscena();
        }
        // Al volver visible (entrar a la pestaña Vista 3D), reconstruir la escena
        // para reflejar la geometría recién sincronizada (CoordenadaX/Y de las
        // losas, columnas, vigas y zapatas) sin depender de un cambio de instancia.
        else if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
            ReconstruirEscena();
    }

    /// <summary>Reconstruye la escena desde el <see cref="Edificio"/> y reencuadra en el próximo render.</summary>
    public void ReconstruirEscena()
    {
        _escena = EscenaEdificio.Construir(Edificio);
        _encuadrado = false;
        InvalidateVisual();
    }

    private void OnEdificioCambiado(object? sender, System.EventArgs e)
        => Avalonia.Threading.Dispatcher.UIThread.Post(ReconstruirEscena);

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.FillRectangle(Fondo, new Rect(size)); // fondo + superficie de hit-test
        float w = (float)size.Width, h = (float)size.Height;
        if (w < 1f || h < 1f) return;

        if (_escena.Segmentos.Count > 0 && !_encuadrado)
        {
            _camara.Encuadrar(_escena.Min, _escena.Max);
            _encuadrado = true;
        }

        var mvp = _camara.MatrizVista * _camara.MatrizProyeccion(w / h);

        DibujarSegmentos(context, PrimitivasEscena.RejillaSuelo(RejillaExtension, RejillaPaso), mvp, w, h, PenRejilla);
        DibujarSegmentos(context, _escena.Segmentos, mvp, w, h, PenModelo);

        var ejes = PrimitivasEscena.Ejes(EjeLongitud);
        DibujarSegmento(context, ejes[0], mvp, w, h, PenEjeX);
        DibujarSegmento(context, ejes[1], mvp, w, h, PenEjeY);
        DibujarSegmento(context, ejes[2], mvp, w, h, PenEjeZ);

        if (Edificio != null)
        {
            var penEjeEstructural = new Pen(new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)), 1.5)
            {
                DashStyle = DashStyle.DashDot
            };
            foreach (var eje in Edificio.Ejes)
            {
                var pA = new Vector3((float)eje.PuntoInicio.X, (float)eje.PuntoInicio.Y, 0f);
                var pB = new Vector3((float)eje.PuntoFin.X, (float)eje.PuntoFin.Y, 0f);
                if (Proyector3D.ProyectarSegmento(pA, pB, mvp, w, h, out var pa, out var pb))
                {
                    context.DrawLine(penEjeEstructural, new Point(pa.X, pa.Y), new Point(pb.X, pb.Y));
                    var ftEje = new FormattedText(eje.Etiqueta, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 12, PincelTexto);
                    context.DrawText(ftEje, new Point(pb.X, pb.Y));
                }
            }
        }

        var hint = new FormattedText(
            "Vista 3D (esquemática) — arrastrar: orbitar · rueda: zoom · doble-clic: reencuadrar",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, PincelTexto);
        context.DrawText(hint, new Point(10, h - 22));
    }

    private static void DibujarSegmentos(
        DrawingContext context, IReadOnlyList<Segmento3D> segmentos,
        Matrix4x4 mvp, float w, float h, IPen pen)
    {
        for (int i = 0; i < segmentos.Count; i++)
            DibujarSegmento(context, segmentos[i], mvp, w, h, pen);
    }

    private static void DibujarSegmento(
        DrawingContext context, Segmento3D s, Matrix4x4 mvp, float w, float h, IPen pen)
    {
        if (Proyector3D.ProyectarSegmento(s.A, s.B, mvp, w, h, out var pa, out var pb))
            context.DrawLine(pen, new Point(pa.X, pa.Y), new Point(pb.X, pb.Y));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2) // doble-clic → reencuadrar
        {
            _encuadrado = false;
            InvalidateVisual();
            return;
        }

        _arrastrando = true;
        _ultimoPunto = e.GetPosition(this);
        e.Pointer.Capture(this);
        Focus();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_arrastrando) return;
        var p = e.GetPosition(this);
        float dx = (float)(p.X - _ultimoPunto.X);
        float dy = (float)(p.Y - _ultimoPunto.Y);
        _ultimoPunto = p;
        _camara.Orbitar(dx * 0.5f, -dy * 0.5f);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _arrastrando = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _camara.AcercarAlejar(e.Delta.Y > 0 ? 0.9f : 1.1f);
        InvalidateVisual();
        e.Handled = true;
    }
}
