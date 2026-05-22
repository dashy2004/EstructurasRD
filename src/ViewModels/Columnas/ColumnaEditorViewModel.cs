using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using LosasPlus.Columnas;
using LosasPlus.Models;
using LosasPlus.Services;

namespace LosasPlus.ViewModels.Columnas;

/// <summary>
/// ViewModel del editor de columnas RC (Fase 5, Iteración 2). Cuelga de
/// <c>MainViewModel</c> y administra la <see cref="ColumnaActiva"/> del nivel,
/// la edición de su sección/acero/capas y el recálculo reactivo del motor
/// <see cref="ColumnaDesignEngine"/>.
///
/// <para>
/// Cada mutación estructural toma un snapshot de Undo y dispara un recálculo
/// en hilo secundario; el resultado regenera las series de los dos
/// <see cref="PlotModel"/> de OxyPlot (diagrama de interacción P-M y croquis
/// acotado de la sección). La lógica gráfica vive sólo en esta capa de
/// presentación — <c>src.Core</c> permanece libre de dependencias gráficas.
/// </para>
/// </summary>
public sealed class ColumnaEditorViewModel : INotifyPropertyChanged
{
    private static readonly OxyColor ColorEje       = OxyColor.FromRgb(0x6B, 0x72, 0x80);
    private static readonly OxyColor ColorNominal   = OxyColor.FromRgb(0x33, 0x3A, 0x45);
    private static readonly OxyColor ColorDiseno    = OxyColor.FromRgb(0xC2, 0x41, 0x1E);
    private static readonly OxyColor ColorTopeAxial = OxyColor.FromRgb(0xB5, 0x8A, 0x00);
    private static readonly OxyColor ColorConcreto  = OxyColor.FromRgb(0xBD, 0xBD, 0xBD);
    private static readonly OxyColor ColorAcero     = OxyColor.FromRgb(0xC2, 0x8A, 0x1E);

    private readonly Proyecto _proyecto;
    private readonly Action _pushUndoSnapshot;
    private readonly Nivel _nivel;

    // Ítems del grafo de la columna activa con suscripción a PropertyChanged.
    private readonly List<CapaAcero> _capasEnganchadas = new();
    private Columna? _columnaEnganchada;

    private DiagramaInteraccion2D _diagrama = DiagramaInteraccion2D.Vacio;
    private CancellationTokenSource? _ctsRecalculo;
    private Task _recalculoActual = Task.CompletedTask;

    public ColumnaEditorViewModel(Proyecto proyecto, Action pushUndoSnapshot)
    {
        _proyecto = proyecto ?? throw new ArgumentNullException(nameof(proyecto));
        _pushUndoSnapshot = pushUndoSnapshot ?? throw new ArgumentNullException(nameof(pushUndoSnapshot));

        _proyecto.AsegurarEstructura();
        _nivel = _proyecto.Edificios[0].Niveles[0];

        ModeloDiagramaPM = CrearModeloBase("Diagrama de Interacción P-M");
        ModeloSeccionTransversal = CrearModeloBase("Sección Transversal");
        // Aspecto 1:1 en el croquis para que el rectángulo de concreto no se
        // deforme con la relación de aspecto del control.
        ModeloSeccionTransversal.PlotType = PlotType.Cartesian;

        NuevaColumnaCommand    = new RelayCommand(_ => NuevaColumna());
        EliminarColumnaCommand = new RelayCommand(_ => EliminarColumna(), _ => _columnaActiva is not null);
        AgregarCapaCommand     = new RelayCommand(_ => AgregarCapa(),     _ => _columnaActiva is not null);
        EliminarCapaCommand    = new RelayCommand(_ => EliminarCapa(),    _ => _capaSeleccionada is not null);

        ColumnaActiva = _nivel.Columnas.FirstOrDefault();
    }

    // ---- Colecciones pass-through ----

    /// <summary>Columnas del nivel por defecto del proyecto.</summary>
    public ObservableCollection<Columna> Columnas => _nivel.Columnas;

    // ---- Selección ----

    private Columna? _columnaActiva;
    /// <summary>Columna en edición. Cambiarla reengancha el grafo y recalcula.</summary>
    public Columna? ColumnaActiva
    {
        get => _columnaActiva;
        set
        {
            if (ReferenceEquals(_columnaActiva, value)) return;
            _columnaActiva = value;
            RehookColumna(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HayColumnaActiva));
            CapaSeleccionada = value?.Acero.Capas.FirstOrDefault();
            SolicitarRecalculo();
        }
    }

    /// <summary><c>true</c> si hay una columna en edición.</summary>
    public bool HayColumnaActiva => _columnaActiva is not null;

    private CapaAcero? _capaSeleccionada;
    /// <summary>Capa seleccionada en el DataGrid (origen de Eliminar Capa).</summary>
    public CapaAcero? CapaSeleccionada
    {
        get => _capaSeleccionada;
        set { _capaSeleccionada = value; OnPropertyChanged(); }
    }

    // ---- Diagramas OxyPlot ----

    /// <summary>Diagrama de interacción P-M (curva nominal + diseño + tope axial).</summary>
    public PlotModel ModeloDiagramaPM { get; }

    /// <summary>Croquis acotado de la sección transversal con sus capas de acero.</summary>
    public PlotModel ModeloSeccionTransversal { get; }

    /// <summary>
    /// Capacidad nominal a compresión pura P0, en kN — expuesta para tests y
    /// posibles bindings del lateral analítico.
    /// </summary>
    public double PnMaximoNominal => _diagrama.PnMaximoNominal;

    /// <summary>Capacidad nominal a tracción pura Tn, en kN.</summary>
    public double TraccionPuraNominal => _diagrama.TraccionPuraNominal;

    /// <summary>Conteo de puntos de la curva nominal — útil para tests.</summary>
    public int CantidadPuntosCurva => _diagrama.CurvaNominal.Count;

    // ---- Comandos ----

    public ICommand NuevaColumnaCommand { get; }
    public ICommand EliminarColumnaCommand { get; }
    public ICommand AgregarCapaCommand { get; }
    public ICommand EliminarCapaCommand { get; }

    /// <summary>
    /// Lo invoca el code-behind desde <c>DataGrid.BeginningEdit</c> y desde
    /// <c>TextBox.GotFocus</c>: toma un snapshot ANTES de que la celda o el
    /// valor cambien, para que Ctrl+Z lo revierta.
    /// </summary>
    public void SnapshotAntesDeEditar() => _pushUndoSnapshot();

    /// <summary>
    /// Lo invoca <c>MainViewModel</c> tras un Undo/Redo: la colección
    /// <c>Nivel.Columnas</c> se reemplaza en bloque, así que se reengancha,
    /// se revalida la selección y se recalcula.
    /// </summary>
    public void NotificarRestauracion()
    {
        _columnaActiva = _nivel.Columnas.FirstOrDefault();
        OnPropertyChanged(nameof(ColumnaActiva));
        OnPropertyChanged(nameof(HayColumnaActiva));
        RehookColumna(_columnaActiva);
        CapaSeleccionada = _columnaActiva?.Acero.Capas.FirstOrDefault();
        SolicitarRecalculo();
    }

    // ---- Mutaciones (cada una toma un snapshot de Undo) ----

    private void NuevaColumna()
    {
        _pushUndoSnapshot();
        var columna = new Columna
        {
            Id = _nivel.Columnas.Count == 0 ? 1 : _nivel.Columnas.Max(c => c.Id) + 1,
            Nombre = $"Columna {_nivel.Columnas.Count + 1}",
        };
        columna.Geometria.Base = 0.30;
        columna.Geometria.Peralte = 0.30;
        columna.Geometria.Fc = 28.0;
        columna.Acero.Fy = 420.0;
        columna.Acero.Capas.Add(new CapaAcero { Profundidad = 0.05, Area = 0.0002 });
        columna.Acero.Capas.Add(new CapaAcero { Profundidad = 0.25, Area = 0.0002 });
        _nivel.Columnas.Add(columna);
        ColumnaActiva = columna;
    }

    private void EliminarColumna()
    {
        if (_columnaActiva is null) return;
        _pushUndoSnapshot();
        int idx = _nivel.Columnas.IndexOf(_columnaActiva);
        _nivel.Columnas.Remove(_columnaActiva);
        ColumnaActiva = _nivel.Columnas.Count > 0
            ? _nivel.Columnas[Math.Min(idx, _nivel.Columnas.Count - 1)]
            : null;
    }

    private void AgregarCapa()
    {
        if (_columnaActiva is null) return;
        _pushUndoSnapshot();
        var capa = new CapaAcero { Profundidad = 0.05, Area = 0.0002 };
        _columnaActiva.Acero.Capas.Add(capa);
        CapaSeleccionada = capa;
    }

    private void EliminarCapa()
    {
        if (_columnaActiva is null || _capaSeleccionada is null) return;
        _pushUndoSnapshot();
        _columnaActiva.Acero.Capas.Remove(_capaSeleccionada);
        CapaSeleccionada = _columnaActiva.Acero.Capas.FirstOrDefault();
    }

    // ---- Reactividad: enganche del grafo de la columna ----

    private void RehookColumna(Columna? columna)
    {
        if (_columnaEnganchada is not null)
        {
            _columnaEnganchada.PropertyChanged -= OnElementoCambiado;
            _columnaEnganchada.Geometria.PropertyChanged -= OnElementoCambiado;
            _columnaEnganchada.Acero.PropertyChanged -= OnElementoCambiado;
            _columnaEnganchada.Acero.Capas.CollectionChanged -= OnCapasCambiaron;
        }
        foreach (var c in _capasEnganchadas) c.PropertyChanged -= OnElementoCambiado;
        _capasEnganchadas.Clear();

        _columnaEnganchada = columna;
        if (columna is null) return;

        columna.PropertyChanged += OnElementoCambiado;
        columna.Geometria.PropertyChanged += OnElementoCambiado;
        columna.Acero.PropertyChanged += OnElementoCambiado;
        columna.Acero.Capas.CollectionChanged += OnCapasCambiaron;
        foreach (var c in columna.Acero.Capas)
        {
            c.PropertyChanged += OnElementoCambiado;
            _capasEnganchadas.Add(c);
        }
    }

    private void OnCapasCambiaron(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RehookColumna(_columnaActiva);
        if (_capaSeleccionada is not null && _columnaActiva is not null
            && !_columnaActiva.Acero.Capas.Contains(_capaSeleccionada))
            CapaSeleccionada = _columnaActiva.Acero.Capas.FirstOrDefault();
        SolicitarRecalculo();
    }

    private void OnElementoCambiado(object? sender, PropertyChangedEventArgs e) => SolicitarRecalculo();

    // ---- Recálculo asíncrono ----

    private void SolicitarRecalculo()
    {
        _ctsRecalculo?.Cancel();
        var cts = new CancellationTokenSource();
        _ctsRecalculo = cts;
        _recalculoActual = RecalcularNucleoAsync(cts.Token);
    }

    /// <summary>
    /// Fuerza un recálculo y devuelve la tarea que lo representa. Las pruebas
    /// la esperan con <c>await</c> para observar el resultado de forma
    /// determinista: los recálculos anteriores en curso quedan cancelados y
    /// no llegan a tocar los diagramas.
    /// </summary>
    public Task RecalcularAsync()
    {
        SolicitarRecalculo();
        return _recalculoActual;
    }

    private async Task RecalcularNucleoAsync(CancellationToken token)
    {
        var columna = _columnaActiva;
        if (columna is null)
        {
            LimpiarDiagramas();
            return;
        }
        try
        {
            // Las referencias se capturan en el hilo de UI; el motor es puro y
            // devuelve un DTO nuevo. El ensamblado de series reanuda en el
            // contexto capturado (UI en la app).
            var seccion = columna.Geometria;
            var acero = columna.Acero;
            var diagrama = await Task.Run(
                () => ColumnaDesignEngine.ConstruirDiagramaInteraccion2D(seccion, acero), token);
            if (token.IsCancellationRequested) return;

            _diagrama = diagrama;
            NotificarPropiedadesDiagnostico();
            ConstruirSeries();
        }
        catch (OperationCanceledException)
        {
            // Recálculo superado por otro más reciente — descartar.
        }
        catch (Exception)
        {
            LimpiarDiagramas();
        }
    }

    private void NotificarPropiedadesDiagnostico()
    {
        OnPropertyChanged(nameof(PnMaximoNominal));
        OnPropertyChanged(nameof(TraccionPuraNominal));
        OnPropertyChanged(nameof(CantidadPuntosCurva));
    }

    // ---- Construcción de las series OxyPlot ----

    private void ConstruirSeries()
    {
        ConstruirModeloDiagramaPM();
        ConstruirModeloSeccionTransversal();
    }

    private void LimpiarDiagramas()
    {
        _diagrama = DiagramaInteraccion2D.Vacio;
        foreach (var m in new[] { ModeloDiagramaPM, ModeloSeccionTransversal })
        {
            m.Series.Clear();
            m.Annotations.Clear();
            m.Axes.Clear();
            m.InvalidatePlot(true);
        }
        NotificarPropiedadesDiagnostico();
    }

    private void ConstruirModeloDiagramaPM()
    {
        var m = ModeloDiagramaPM;
        m.Series.Clear();
        m.Axes.Clear();
        m.Annotations.Clear();

        if (_diagrama.CurvaNominal.Count == 0)
        {
            m.InvalidatePlot(true);
            return;
        }

        m.Axes.Add(Eje(AxisPosition.Bottom, "M (kN·m)"));
        m.Axes.Add(Eje(AxisPosition.Left, "P (kN) — compresión +"));

        // Curva nominal — perfilo el polígono completo (ya viene en orden cíclico).
        var nominal = new LineSeries
        {
            Title = "Mn-Pn (nominal)",
            Color = ColorNominal,
            StrokeThickness = 1.6,
        };
        nominal.Points.AddRange(_diagrama.CurvaNominal.Select(p => new DataPoint(p.Momento, p.Carga)));
        m.Series.Add(nominal);

        // Curva de diseño — el cap axial ya está aplicado en CurvaDiseno.
        var diseno = new LineSeries
        {
            Title = "φMn-φPn (diseño)",
            Color = ColorDiseno,
            StrokeThickness = 2.2,
        };
        diseno.Points.AddRange(_diagrama.CurvaDiseno.Select(p => new DataPoint(p.Momento, p.Carga)));
        m.Series.Add(diseno);

        // Línea de referencia del tope axial de diseño α·φ·P0.
        double topeAxial = 0.80 * 0.65 * _diagrama.PnMaximoNominal;
        m.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Horizontal,
            Y = topeAxial,
            Color = ColorTopeAxial,
            LineStyle = LineStyle.Dash,
            StrokeThickness = 1.4,
            Text = $"φPn,max = {topeAxial:0} kN",
            TextColor = ColorTopeAxial,
        });

        m.InvalidatePlot(true);
    }

    private void ConstruirModeloSeccionTransversal()
    {
        var m = ModeloSeccionTransversal;
        m.Series.Clear();
        m.Axes.Clear();
        m.Annotations.Clear();

        var columna = _columnaActiva;
        if (columna is null)
        {
            m.InvalidatePlot(true);
            return;
        }

        double b = columna.Geometria.Base;
        double h = columna.Geometria.Peralte;

        // Eje X horizontal (ancho); eje Y invertido para que la profundidad
        // crezca hacia abajo, como suele dibujarse una sección de columna.
        m.Axes.Add(Eje(AxisPosition.Bottom, "Ancho b (m)"));
        m.Axes.Add(Eje(AxisPosition.Left, "Profundidad y (m)", invertido: true));

        // Contorno del concreto: polígono rectangular relleno traslúcido.
        var contorno = new PolygonAnnotation
        {
            Fill = OxyColor.FromAColor(60, ColorConcreto),
            Stroke = ColorEje,
            StrokeThickness = 1.5,
        };
        contorno.Points.Add(new DataPoint(0.0, 0.0));
        contorno.Points.Add(new DataPoint(b, 0.0));
        contorno.Points.Add(new DataPoint(b, h));
        contorno.Points.Add(new DataPoint(0.0, h));
        m.Annotations.Add(contorno);

        // Capas de acero: dos marcadores simétricos por capa, posicionados al
        // 15 % y 85 % del ancho — esquemático, sin asumir conteo de varillas.
        var serieAcero = new ScatterSeries
        {
            Title = "Acero longitudinal",
            MarkerType = MarkerType.Circle,
            MarkerFill = ColorAcero,
            MarkerStroke = ColorEje,
            MarkerStrokeThickness = 0.5,
            MarkerSize = 6.0,
        };
        foreach (var capa in columna.Acero.Capas)
        {
            serieAcero.Points.Add(new ScatterPoint(0.15 * b, capa.Profundidad));
            serieAcero.Points.Add(new ScatterPoint(0.85 * b, capa.Profundidad));
        }
        m.Series.Add(serieAcero);

        m.InvalidatePlot(true);
    }

    // ---- Helpers de OxyPlot ----

    private static PlotModel CrearModeloBase(string titulo) => new()
    {
        Title = titulo,
        TitleFontSize = 12.0,
        TitleColor = ColorEje,
        Background = OxyColors.Transparent,
        PlotAreaBorderColor = ColorEje,
        TextColor = ColorEje,
    };

    private static LinearAxis Eje(AxisPosition posicion, string titulo, bool invertido = false)
    {
        var eje = new LinearAxis
        {
            Position = posicion,
            Title = titulo,
            TitleColor = ColorEje,
            TextColor = ColorEje,
            AxislineColor = ColorEje,
            TicklineColor = ColorEje,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromAColor(40, ColorEje),
        };
        if (invertido) { eje.StartPosition = 1.0; eje.EndPosition = 0.0; }
        return eje;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
