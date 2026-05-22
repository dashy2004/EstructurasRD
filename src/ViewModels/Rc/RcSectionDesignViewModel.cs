using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using LosasPlus.Rc;
using LosasPlus.Services;
using LosasPlus.Vigas;

namespace LosasPlus.ViewModels.Rc;

/// <summary>
/// ViewModel del editor de diseño de sección de concreto reforzado (Fase 4,
/// Iteración 2). Es un <b>VM hijo</b> de <c>VigaEditorViewModel</c>: opera sobre
/// la <see cref="SeccionRC"/> y el <see cref="RefuerzoLongitudinal"/> del tramo
/// de viga seleccionado.
///
/// <para>
/// Cualquier edición de un parámetro toma un snapshot de Undo (en el setter,
/// antes de mutar) y recalcula la capacidad con <see cref="RcDesignEngine"/> —
/// un cómputo de forma cerrada O(1), por lo que el recálculo es síncrono e
/// instantáneo. El resultado regenera el diagrama OxyPlot del perfil de
/// deformaciones y el bloque equivalente de Whitney.
/// </para>
/// </summary>
public sealed class RcSectionDesignViewModel : INotifyPropertyChanged
{
    private const double DeformacionUltima = 0.003;   // εcu

    private static readonly OxyColor ColorConcreto   = OxyColor.FromRgb(0xD8, 0xDC, 0xE2);
    private static readonly OxyColor ColorBorde      = OxyColor.FromRgb(0x6B, 0x72, 0x80);
    private static readonly OxyColor ColorAcero      = OxyColor.FromRgb(0xC2, 0x41, 0x1E);
    private static readonly OxyColor ColorEjeNeutro  = OxyColor.FromRgb(0xC9, 0x9A, 0x00);
    private static readonly OxyColor ColorDeformacion = OxyColor.FromRgb(0x0E, 0x7D, 0xB8);
    private static readonly OxyColor ColorCompresion = OxyColor.FromAColor(150, OxyColor.FromRgb(0x0E, 0x7D, 0xB8));
    private static readonly OxyColor ColorTexto      = OxyColor.FromRgb(0x33, 0x3A, 0x45);

    private readonly Action _pushUndoSnapshot;
    private TramoViga? _tramo;
    private ResultadoCapacidadRC? _resultado;
    private bool _momentoPositivo = true;

    public RcSectionDesignViewModel(Action pushUndoSnapshot)
    {
        _pushUndoSnapshot = pushUndoSnapshot ?? throw new ArgumentNullException(nameof(pushUndoSnapshot));
        ModeloSeccion = new PlotModel
        {
            Background = OxyColors.Transparent,
            PlotAreaBorderColor = OxyColors.Transparent,
        };
        Recalcular();
    }

    /// <summary><c>true</c> si hay un tramo en edición.</summary>
    public bool HayTramo => _tramo is not null;

    /// <summary>
    /// Re-apunta el editor al <paramref name="tramo"/> indicado — lo invoca el
    /// setter de <c>VigaEditorViewModel.TramoSeleccionado</c> de forma
    /// incondicional (incluso con <c>null</c>), de modo que el VM nunca quede
    /// con un <see cref="TramoViga"/> huérfano tras un Undo.
    /// </summary>
    public void Retarget(TramoViga? tramo)
    {
        _tramo = tramo;
        OnPropertyChanged(nameof(HayTramo));
        OnPropertyChanged(nameof(Base));
        OnPropertyChanged(nameof(Peralte));
        OnPropertyChanged(nameof(Recubrimiento));
        OnPropertyChanged(nameof(Fc));
        OnPropertyChanged(nameof(Fy));
        OnPropertyChanged(nameof(AreaAceroTop));
        OnPropertyChanged(nameof(AreaAceroBot));
        Recalcular();
    }

    // ---- Parámetros editables (pass-through con snapshot de Undo) ----

    /// <summary>Ancho de la sección, en metros.</summary>
    public double Base
    {
        get => _tramo?.Seccion.Base ?? 0.0;
        set
        {
            if (_tramo is null || value == _tramo.Seccion.Base) return;
            _pushUndoSnapshot();
            _tramo.Seccion.Base = value;
            OnPropertyChanged();
            Recalcular();
        }
    }

    /// <summary>Peralte (altura total) de la sección, en metros.</summary>
    public double Peralte
    {
        get => _tramo?.Seccion.Peralte ?? 0.0;
        set
        {
            if (_tramo is null || value == _tramo.Seccion.Peralte) return;
            _pushUndoSnapshot();
            _tramo.Seccion.Peralte = value;
            OnPropertyChanged();
            Recalcular();
        }
    }

    /// <summary>Recubrimiento al centroide del acero de tracción, en metros.</summary>
    public double Recubrimiento
    {
        get => _tramo?.Seccion.Recubrimiento ?? 0.0;
        set
        {
            if (_tramo is null || value == _tramo.Seccion.Recubrimiento) return;
            _pushUndoSnapshot();
            _tramo.Seccion.Recubrimiento = value;
            OnPropertyChanged();
            Recalcular();
        }
    }

    /// <summary>Resistencia del concreto f'c, en MPa.</summary>
    public double Fc
    {
        get => _tramo?.Seccion.Fc ?? 0.0;
        set
        {
            if (_tramo is null || value == _tramo.Seccion.Fc) return;
            _pushUndoSnapshot();
            _tramo.Seccion.Fc = value;
            OnPropertyChanged();
            Recalcular();
        }
    }

    /// <summary>Esfuerzo de fluencia del acero fy, en MPa.</summary>
    public double Fy
    {
        get => _tramo?.Seccion.Fy ?? 0.0;
        set
        {
            if (_tramo is null || value == _tramo.Seccion.Fy) return;
            _pushUndoSnapshot();
            _tramo.Seccion.Fy = value;
            OnPropertyChanged();
            Recalcular();
        }
    }

    /// <summary>Área de acero longitudinal superior, en m².</summary>
    public double AreaAceroTop
    {
        get => _tramo?.Refuerzo.AreaAceroTop ?? 0.0;
        set
        {
            if (_tramo is null || value == _tramo.Refuerzo.AreaAceroTop) return;
            _pushUndoSnapshot();
            _tramo.Refuerzo.AreaAceroTop = value;
            OnPropertyChanged();
            Recalcular();
        }
    }

    /// <summary>Área de acero longitudinal inferior, en m².</summary>
    public double AreaAceroBot
    {
        get => _tramo?.Refuerzo.AreaAceroBot ?? 0.0;
        set
        {
            if (_tramo is null || value == _tramo.Refuerzo.AreaAceroBot) return;
            _pushUndoSnapshot();
            _tramo.Refuerzo.AreaAceroBot = value;
            OnPropertyChanged();
            Recalcular();
        }
    }

    /// <summary>
    /// <c>true</c> diseña para momento positivo (acero inferior a tracción);
    /// <c>false</c>, momento negativo (acero superior). No muta el modelo, así
    /// que no toma snapshot de Undo.
    /// </summary>
    public bool MomentoPositivo
    {
        get => _momentoPositivo;
        set
        {
            if (_momentoPositivo == value) return;
            _momentoPositivo = value;
            OnPropertyChanged();
            Recalcular();
        }
    }

    // ---- Salidas calculadas (del ResultadoCapacidadRC en caché) ----

    /// <summary><c>true</c> si hay un resultado de capacidad calculado.</summary>
    public bool HayResultado => _resultado is not null;

    /// <summary>Momento nominal Mn, en kN·m.</summary>
    public double MomentoNominal => _resultado?.MomentoNominal ?? 0.0;

    /// <summary>Momento de diseño φMn, en kN·m.</summary>
    public double MomentoDiseno => _resultado?.MomentoDiseno ?? 0.0;

    /// <summary>Profundidad del eje neutro c, en m.</summary>
    public double EjeNeutro => _resultado?.EjeNeutro ?? 0.0;

    /// <summary>Profundidad del bloque de compresión a, en m.</summary>
    public double BloqueCompresion => _resultado?.BloqueCompresion ?? 0.0;

    /// <summary>Factor de reducción de resistencia φ.</summary>
    public double FactorPhi => _resultado?.FactorPhi ?? 0.0;

    /// <summary>Clasificación de la sección según εt.</summary>
    public ClasificacionSeccion Clasificacion
        => _resultado?.Clasificacion ?? ClasificacionSeccion.CompresionControlada;

    /// <summary>Cuantía de acero de tracción ρ.</summary>
    public double Cuantia => _resultado?.Cuantia ?? 0.0;

    /// <summary>Cuantía mínima ρ_min.</summary>
    public double CuantiaMinima => _resultado?.CuantiaMinima ?? 0.0;

    /// <summary>Deformación neta del acero de tracción εt.</summary>
    public double DeformacionNetaTraccion => _resultado?.DeformacionNetaTraccion ?? 0.0;

    /// <summary><c>true</c> si ρ ≥ ρ_min.</summary>
    public bool CumpleCuantiaMinima => _resultado?.CumpleCuantiaMinima ?? true;

    /// <summary><c>true</c> si εt ≥ 0.004 (límite de ductilidad).</summary>
    public bool CumpleLimiteDeformacion => _resultado?.CumpleLimiteDeformacion ?? true;

    /// <summary>Advertencias normativas del cálculo.</summary>
    public IReadOnlyList<string> Advertencias
        => _resultado?.Advertencias ?? Array.Empty<string>();

    /// <summary>Diagrama OxyPlot: sección, perfil de deformaciones y bloque de Whitney.</summary>
    public PlotModel ModeloSeccion { get; }

    // ---- Recálculo ----

    private void Recalcular()
    {
        _resultado = _tramo is null
            ? null
            : RcDesignEngine.CalcularCapacidad(_tramo.Seccion, _tramo.Refuerzo, _momentoPositivo);

        OnPropertyChanged(nameof(HayResultado));
        OnPropertyChanged(nameof(MomentoNominal));
        OnPropertyChanged(nameof(MomentoDiseno));
        OnPropertyChanged(nameof(EjeNeutro));
        OnPropertyChanged(nameof(BloqueCompresion));
        OnPropertyChanged(nameof(FactorPhi));
        OnPropertyChanged(nameof(Clasificacion));
        OnPropertyChanged(nameof(Cuantia));
        OnPropertyChanged(nameof(CuantiaMinima));
        OnPropertyChanged(nameof(DeformacionNetaTraccion));
        OnPropertyChanged(nameof(CumpleCuantiaMinima));
        OnPropertyChanged(nameof(CumpleLimiteDeformacion));
        OnPropertyChanged(nameof(Advertencias));
        ReconstruirDiagrama();
    }

    private void ReconstruirDiagrama()
    {
        var m = ModeloSeccion;
        m.Series.Clear();
        m.Annotations.Clear();
        m.Axes.Clear();

        if (_tramo is null || _resultado is null || _tramo.Seccion.Peralte <= 0.0)
        {
            m.InvalidatePlot(true);
            return;
        }

        double h = _tramo.Seccion.Peralte;
        double d = h - _tramo.Seccion.Recubrimiento;
        double c = _resultado.EjeNeutro;
        double a = _resultado.BloqueCompresion;
        double et = _resultado.DeformacionNetaTraccion;

        // Ejes ocultos de rango fijo (las anotaciones necesitan ejes presentes).
        m.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, IsAxisVisible = false,
            Minimum = -0.35, Maximum = 4.15 });
        m.Axes.Add(new LinearAxis { Position = AxisPosition.Left, IsAxisVisible = false,
            StartPosition = 1.0, EndPosition = 0.0,       // profundidad: 0 arriba, h abajo
            Minimum = -0.16 * h, Maximum = 1.16 * h });

        // --- Carril 1: la sección ---
        m.Annotations.Add(Rect(0.0, 1.0, 0.0, h, ColorConcreto, ColorBorde));
        double banda = 0.04 * h;
        m.Annotations.Add(Rect(0.06, 0.94, d - banda / 2.0, d + banda / 2.0, ColorAcero, ColorAcero));
        m.Annotations.Add(Texto("Sección", 0.5, -0.09 * h));

        // --- Eje neutro: línea horizontal punteada a profundidad c ---
        m.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Horizontal, Y = c,
            Color = ColorEjeNeutro, LineStyle = LineStyle.Dash, StrokeThickness = 1.5,
            Text = $"c = {c:0.###} m", TextColor = ColorEjeNeutro,
            TextHorizontalAlignment = HorizontalAlignment.Left,
        });

        // --- Carril 2: perfil de deformaciones ---
        const double centro2 = 1.9, semi = 0.5;
        double etInferior = c > 1e-9 ? DeformacionUltima * (h - c) / c : 0.0;
        double escala = Math.Max(DeformacionUltima, Math.Abs(etInferior));
        if (escala <= 0.0) escala = DeformacionUltima;
        double X(double deformacion) => centro2 + deformacion / escala * semi;

        var lineaDeformacion = new LineSeries { Color = ColorDeformacion, StrokeThickness = 2.0 };
        lineaDeformacion.Points.Add(new DataPoint(X(-DeformacionUltima), 0.0));
        lineaDeformacion.Points.Add(new DataPoint(X(etInferior), h));
        m.Series.Add(lineaDeformacion);
        m.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Vertical, X = centro2,
            Color = ColorBorde, LineStyle = LineStyle.Dot, StrokeThickness = 1.0,
        });
        m.Annotations.Add(Texto($"εt = {et:0.####}", X(et) + 0.08, d));
        m.Annotations.Add(Texto("Deformaciones", centro2, -0.09 * h));

        // --- Carril 3: bloque equivalente de Whitney ---
        m.Annotations.Add(Rect(2.8, 3.8, 0.0, a, ColorCompresion, ColorBorde));
        m.Annotations.Add(Texto($"a = {a:0.###} m", 3.3, a + 0.07 * h));
        m.Annotations.Add(Texto("0.85 f'c", 3.3, a / 2.0));
        m.Annotations.Add(Texto("Bloque de Whitney", 3.3, -0.09 * h));

        m.InvalidatePlot(true);
    }

    private static RectangleAnnotation Rect(
        double x0, double x1, double y0, double y1, OxyColor relleno, OxyColor borde)
        => new()
        {
            MinimumX = x0, MaximumX = x1, MinimumY = y0, MaximumY = y1,
            Fill = relleno, Stroke = borde, StrokeThickness = 1.0,
        };

    private static TextAnnotation Texto(string texto, double x, double y)
        => new()
        {
            Text = texto, TextPosition = new DataPoint(x, y),
            Stroke = OxyColors.Undefined, TextColor = ColorTexto, FontSize = 11.0,
        };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
