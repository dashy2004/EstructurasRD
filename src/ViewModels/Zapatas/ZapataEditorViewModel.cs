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
using LosasPlus.Cargas;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.Zapatas;

namespace LosasPlus.ViewModels.Zapatas;

/// <summary>
/// ViewModel del editor de zapatas aisladas (Fase 6, Iteración 2). Cuelga de
/// <c>MainViewModel</c> y administra la <see cref="ZapataActiva"/> del nivel,
/// la edición de sus dimensiones/suelo/cargas y el recálculo reactivo del
/// motor <see cref="ZapataDesignEngine"/>.
///
/// <para>
/// Cada mutación estructural toma un snapshot de Undo y dispara un recálculo
/// en hilo secundario; el resultado regenera las series de los dos
/// <see cref="PlotModel"/> de OxyPlot (bar chart de presiones de esquina por
/// combinación de servicio activa + croquis 1:1 de la planta de la zapata).
/// La lógica gráfica vive sólo en esta capa de presentación; <c>src.Core</c>
/// permanece libre de dependencias gráficas.
/// </para>
/// </summary>
public sealed class ZapataEditorViewModel : INotifyPropertyChanged
{
    private static readonly OxyColor ColorEje              = OxyColor.FromRgb(0x6B, 0x72, 0x80);
    private static readonly OxyColor ColorBarraPositiva    = OxyColor.FromRgb(0x0E, 0x7D, 0xB8);
    private static readonly OxyColor ColorBarraTraccion    = OxyColor.FromRgb(0xBA, 0x1A, 0x1A);
    private static readonly OxyColor ColorPresionAdmisible = OxyColor.FromRgb(0xD3, 0x2F, 0x2F);
    private static readonly OxyColor ColorBandaTraccion    = OxyColor.FromRgb(0xBA, 0x1A, 0x1A);
    private static readonly OxyColor ColorConcreto         = OxyColor.FromRgb(0xBD, 0xBD, 0xBD);
    private static readonly OxyColor ColorPedestal         = OxyColor.FromRgb(0xC2, 0x8A, 0x1E);

    private readonly Proyecto _proyecto;
    private readonly Action _pushUndoSnapshot;
    private readonly Nivel _nivel;

    // Ítems del grafo de la zapata activa con suscripción a PropertyChanged.
    private readonly List<CargaZapata> _cargasEnganchadas = new();
    private ZapataAislada? _zapataEnganchada;

    private ResultadoPresionesZapata _resultado = ResultadoPresionesZapata.Vacio;
    private CancellationTokenSource? _ctsRecalculo;
    private Task _recalculoActual = Task.CompletedTask;

    public ZapataEditorViewModel(Proyecto proyecto, Action pushUndoSnapshot)
    {
        _proyecto = proyecto ?? throw new ArgumentNullException(nameof(proyecto));
        _pushUndoSnapshot = pushUndoSnapshot ?? throw new ArgumentNullException(nameof(pushUndoSnapshot));

        _proyecto.AsegurarEstructura();
        _nivel = _proyecto.Edificios[0].Niveles[0];

        ModeloPresionesTerreno = CrearModeloBase("Presiones de contacto en las esquinas");
        ModeloSeccionZapata    = CrearModeloBase("Planta de la zapata");
        // Aspecto 1:1 en el croquis para que el rectángulo no se deforme.
        ModeloSeccionZapata.PlotType = PlotType.Cartesian;

        NuevaZapataCommand    = new RelayCommand(_ => NuevaZapata());
        EliminarZapataCommand = new RelayCommand(_ => EliminarZapata(), _ => _zapataActiva is not null);
        AgregarCargaCommand   = new RelayCommand(_ => AgregarCarga(),   _ => _zapataActiva is not null);
        EliminarCargaCommand  = new RelayCommand(_ => EliminarCarga(),  _ => _cargaSeleccionada is not null);

        _proyecto.Combinaciones.Combinaciones.CollectionChanged += OnCombinacionesCambiaron;

        ZapataActiva = _nivel.Zapatas.FirstOrDefault();
    }

    // ---- Colecciones pass-through ----

    /// <summary>Zapatas del nivel por defecto del proyecto.</summary>
    public ObservableCollection<ZapataAislada> Zapatas => _nivel.Zapatas;

    /// <summary>Cargas de la zapata activa — pass-through null-safe para el DataGrid.</summary>
    public ObservableCollection<CargaZapata>? Cargas => _zapataActiva?.Cargas;

    /// <summary>
    /// Nombres de las combinaciones de servicio del proyecto — alimenta el
    /// ComboBox de combinación activa. Se refresca en cada recálculo y en
    /// cada cambio de <c>Proyecto.Combinaciones</c>.
    /// </summary>
    public ObservableCollection<string> CombinacionesServicio { get; } = new();

    // ---- Selección ----

    private ZapataAislada? _zapataActiva;
    /// <summary>Zapata en edición. Cambiarla reengancha el grafo y recalcula.</summary>
    public ZapataAislada? ZapataActiva
    {
        get => _zapataActiva;
        set
        {
            if (ReferenceEquals(_zapataActiva, value)) return;
            _zapataActiva = value;
            RehookZapata(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HayZapataActiva));
            OnPropertyChanged(nameof(Cargas));
            CargaSeleccionada = value?.Cargas.FirstOrDefault();
            SolicitarRecalculo();
        }
    }

    /// <summary><c>true</c> si hay una zapata en edición.</summary>
    public bool HayZapataActiva => _zapataActiva is not null;

    private CargaZapata? _cargaSeleccionada;
    /// <summary>Carga seleccionada en el DataGrid (origen de Eliminar Carga).</summary>
    public CargaZapata? CargaSeleccionada
    {
        get => _cargaSeleccionada;
        set { _cargaSeleccionada = value; OnPropertyChanged(); }
    }

    private string? _combinacionActiva;
    /// <summary>
    /// Combinación de servicio cuyas presiones se grafican en
    /// <see cref="ModeloPresionesTerreno"/>. Cambiarla sólo redibuja el bar
    /// chart desde el resultado en caché, sin re-ejecutar el motor.
    /// </summary>
    public string? CombinacionActiva
    {
        get => _combinacionActiva;
        set
        {
            if (_combinacionActiva == value) return;
            _combinacionActiva = value;
            OnPropertyChanged();
            ConstruirModeloPresionesTerreno();
        }
    }

    // ---- Diagramas OxyPlot ----

    /// <summary>Bar chart de las presiones en las cuatro esquinas (q1..q4).</summary>
    public PlotModel ModeloPresionesTerreno { get; }

    /// <summary>Croquis 1:1 de la planta de la zapata y el pedestal.</summary>
    public PlotModel ModeloSeccionZapata { get; }

    /// <summary>Mayor presión de contacto sobre todas las combinaciones de servicio, en kN/m².</summary>
    public double PresionMaximaGlobal => _resultado.PresionMaximaGlobal;

    /// <summary>Menor presión de contacto sobre todas las combinaciones (puede ser negativa).</summary>
    public double PresionMinimaGlobal => _resultado.PresionMinimaGlobal;

    /// <summary><c>true</c> si todas las combinaciones de servicio satisfacen q_max ≤ q_adm.</summary>
    public bool EsEstable => _resultado.EsEstable;

    // ---- Comandos ----

    public ICommand NuevaZapataCommand { get; }
    public ICommand EliminarZapataCommand { get; }
    public ICommand AgregarCargaCommand { get; }
    public ICommand EliminarCargaCommand { get; }

    /// <summary>
    /// Lo invoca el code-behind desde <c>DataGrid.BeginningEdit</c> y desde
    /// <c>TextBox.GotFocus</c>: toma un snapshot ANTES de que la celda o el
    /// valor cambien para que Ctrl+Z lo revierta.
    /// </summary>
    public void SnapshotAntesDeEditar() => _pushUndoSnapshot();

    /// <summary>
    /// Lo invoca <c>MainViewModel</c> tras un Undo/Redo: la colección
    /// <c>Nivel.Zapatas</c> se reemplaza en bloque, así que se reengancha,
    /// se revalida la selección y se recalcula.
    /// </summary>
    public void NotificarRestauracion()
    {
        _zapataActiva = _nivel.Zapatas.FirstOrDefault();
        OnPropertyChanged(nameof(ZapataActiva));
        OnPropertyChanged(nameof(HayZapataActiva));
        OnPropertyChanged(nameof(Cargas));
        RehookZapata(_zapataActiva);
        CargaSeleccionada = _zapataActiva?.Cargas.FirstOrDefault();
        SolicitarRecalculo();
    }

    // ---- Mutaciones (cada una toma un snapshot de Undo) ----

    private void NuevaZapata()
    {
        _pushUndoSnapshot();
        var zapata = new ZapataAislada
        {
            Id = _nivel.Zapatas.Count == 0 ? 1 : _nivel.Zapatas.Max(z => z.Id) + 1,
            Nombre = $"Zapata {_nivel.Zapatas.Count + 1}",
        };
        // Geometría estándar 1.50×1.50×0.40 m, Df = 1.50 m, columna 0.30×0.30.
        zapata.Dimensiones.LargoB = 1.50;
        zapata.Dimensiones.AnchoL = 1.50;
        zapata.Dimensiones.EspesorH = 0.40;
        zapata.Dimensiones.ProfundidadDesplante = 1.50;
        zapata.Dimensiones.PeralteColumna = 0.30;
        zapata.Dimensiones.AnchoColumna = 0.30;
        // Suelo de control.
        zapata.Suelo.PresionAdmisible = 200.0;
        zapata.Suelo.PesoEspecificoSuelo = 18.0;
        _nivel.Zapatas.Add(zapata);
        ZapataActiva = zapata;
    }

    private void EliminarZapata()
    {
        if (_zapataActiva is null) return;
        _pushUndoSnapshot();
        int idx = _nivel.Zapatas.IndexOf(_zapataActiva);
        _nivel.Zapatas.Remove(_zapataActiva);
        ZapataActiva = _nivel.Zapatas.Count > 0
            ? _nivel.Zapatas[Math.Min(idx, _nivel.Zapatas.Count - 1)]
            : null;
    }

    private void AgregarCarga()
    {
        if (_zapataActiva is null) return;
        _pushUndoSnapshot();
        var carga = new CargaZapata
        {
            CodigoCaso = _proyecto.Combinaciones.Casos.FirstOrDefault()?.Codigo ?? "D",
            P = 0.0, Mx = 0.0, My = 0.0,
        };
        _zapataActiva.Cargas.Add(carga);
        CargaSeleccionada = carga;
    }

    private void EliminarCarga()
    {
        if (_zapataActiva is null || _cargaSeleccionada is null) return;
        _pushUndoSnapshot();
        _zapataActiva.Cargas.Remove(_cargaSeleccionada);
        CargaSeleccionada = _zapataActiva.Cargas.FirstOrDefault();
    }

    // ---- Reactividad: enganche del grafo de la zapata ----

    private void RehookZapata(ZapataAislada? zapata)
    {
        if (_zapataEnganchada is not null)
        {
            _zapataEnganchada.PropertyChanged -= OnElementoCambiado;
            _zapataEnganchada.Dimensiones.PropertyChanged -= OnElementoCambiado;
            _zapataEnganchada.Suelo.PropertyChanged -= OnElementoCambiado;
            _zapataEnganchada.Cargas.CollectionChanged -= OnCargasCambiaron;
        }
        foreach (var c in _cargasEnganchadas) c.PropertyChanged -= OnElementoCambiado;
        _cargasEnganchadas.Clear();

        _zapataEnganchada = zapata;
        if (zapata is null) return;

        zapata.PropertyChanged += OnElementoCambiado;
        zapata.Dimensiones.PropertyChanged += OnElementoCambiado;
        zapata.Suelo.PropertyChanged += OnElementoCambiado;
        zapata.Cargas.CollectionChanged += OnCargasCambiaron;
        foreach (var c in zapata.Cargas)
        {
            c.PropertyChanged += OnElementoCambiado;
            _cargasEnganchadas.Add(c);
        }
    }

    private void OnCargasCambiaron(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RehookZapata(_zapataActiva);
        if (_cargaSeleccionada is not null && _zapataActiva is not null
            && !_zapataActiva.Cargas.Contains(_cargaSeleccionada))
            CargaSeleccionada = _zapataActiva.Cargas.FirstOrDefault();
        SolicitarRecalculo();
    }

    private void OnElementoCambiado(object? sender, PropertyChangedEventArgs e) => SolicitarRecalculo();

    private void OnCombinacionesCambiaron(object? sender, NotifyCollectionChangedEventArgs e)
        => SolicitarRecalculo();

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
    /// determinista.
    /// </summary>
    public Task RecalcularAsync()
    {
        SolicitarRecalculo();
        return _recalculoActual;
    }

    private async Task RecalcularNucleoAsync(CancellationToken token)
    {
        var zapata = _zapataActiva;
        if (zapata is null)
        {
            LimpiarDiagramas();
            return;
        }
        var combinaciones = _proyecto.Combinaciones;
        try
        {
            var resultado = await Task.Run(
                () => ZapataDesignEngine.AnalizarPresionesDeContacto(zapata, combinaciones),
                token);
            if (token.IsCancellationRequested) return;

            _resultado = resultado;
            RefrescarCombinacionesServicio();
            NotificarDiagnostico();
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

    private void RefrescarCombinacionesServicio()
    {
        var nombresActuales = _proyecto.Combinaciones.Combinaciones
            .Where(c => c.Tipo == TipoCombinacion.Servicio)
            .Select(c => c.Nombre)
            .ToList();

        CombinacionesServicio.Clear();
        foreach (var n in nombresActuales) CombinacionesServicio.Add(n);

        if (_combinacionActiva is null || !CombinacionesServicio.Contains(_combinacionActiva))
        {
            _combinacionActiva = CombinacionesServicio.FirstOrDefault();
            OnPropertyChanged(nameof(CombinacionActiva));
        }
    }

    private void NotificarDiagnostico()
    {
        OnPropertyChanged(nameof(PresionMaximaGlobal));
        OnPropertyChanged(nameof(PresionMinimaGlobal));
        OnPropertyChanged(nameof(EsEstable));
    }

    // ---- Construcción de las series OxyPlot ----

    private void ConstruirSeries()
    {
        ConstruirModeloPresionesTerreno();
        ConstruirModeloSeccionZapata();
    }

    private void LimpiarDiagramas()
    {
        _resultado = ResultadoPresionesZapata.Vacio;
        CombinacionesServicio.Clear();
        _combinacionActiva = null;
        OnPropertyChanged(nameof(CombinacionActiva));
        foreach (var m in new[] { ModeloPresionesTerreno, ModeloSeccionZapata })
        {
            m.Series.Clear();
            m.Annotations.Clear();
            m.Axes.Clear();
            m.InvalidatePlot(true);
        }
        NotificarDiagnostico();
    }

    private void ConstruirModeloPresionesTerreno()
    {
        var m = ModeloPresionesTerreno;
        m.Series.Clear();
        m.Axes.Clear();
        m.Annotations.Clear();

        if (_combinacionActiva is null || _resultado.PorCombinacion.Count == 0)
        {
            m.InvalidatePlot(true);
            return;
        }

        var p = _resultado.PorCombinacion.FirstOrDefault(
            c => c.NombreCombinacion == _combinacionActiva);
        if (p is null)
        {
            m.InvalidatePlot(true);
            return;
        }

        double qAdm = _zapataActiva?.Suelo.PresionAdmisible ?? 0.0;

        // OxyPlot 2.2.0 sólo provee `BarSeries` (barras horizontales): la
        // CategoryAxis va en el eje vertical (Left) y la LinearAxis en el
        // horizontal (Bottom). Los valores de las 4 presiones se grafican
        // como barras que se extienden a lo largo del eje X.
        var ejeCategorias = new CategoryAxis
        {
            Position = AxisPosition.Left,
            TitleColor = ColorEje,
            TextColor = ColorEje,
            AxislineColor = ColorEje,
            TicklineColor = ColorEje,
            GapWidth = 0.5,
        };
        ejeCategorias.Labels.Add("Q1 (NE)");
        ejeCategorias.Labels.Add("Q2 (NW)");
        ejeCategorias.Labels.Add("Q3 (SW)");
        ejeCategorias.Labels.Add("Q4 (SE)");
        m.Axes.Add(ejeCategorias);

        // Eje X lineal — rango ajustado para mostrar q_adm con holgura y
        // permitir barras negativas si hay despegue.
        double xMin = Math.Min(0.0, p.Minima) * 1.10;
        double xMax = Math.Max(qAdm, p.Maxima) * 1.15;
        if (xMax <= xMin) xMax = xMin + 1.0;
        var ejeValores = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "q (kN/m²)",
            Minimum = xMin,
            Maximum = xMax,
            TitleColor = ColorEje,
            TextColor = ColorEje,
            AxislineColor = ColorEje,
            TicklineColor = ColorEje,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromAColor(40, ColorEje),
        };
        m.Axes.Add(ejeValores);

        // Banda de tracción (q < 0) si hay despegue — franja vertical sobre
        // la zona X negativa, abarcando todas las categorías Y.
        if (p.HayDespegue)
        {
            m.Annotations.Add(new RectangleAnnotation
            {
                MinimumX = xMin,
                MaximumX = 0.0,
                Fill = OxyColor.FromAColor(38, ColorBandaTraccion),
                StrokeThickness = 0.0,
                Layer = AnnotationLayer.BelowSeries,
            });
        }

        // BarSeries con las 4 presiones — color por signo.
        var serie = new BarSeries
        {
            Title = "q (kN/m²)",
            StrokeColor = ColorEje,
            StrokeThickness = 0.5,
        };
        serie.Items.Add(new BarItem(p.Q1) { Color = p.Q1 >= 0 ? ColorBarraPositiva : ColorBarraTraccion });
        serie.Items.Add(new BarItem(p.Q2) { Color = p.Q2 >= 0 ? ColorBarraPositiva : ColorBarraTraccion });
        serie.Items.Add(new BarItem(p.Q3) { Color = p.Q3 >= 0 ? ColorBarraPositiva : ColorBarraTraccion });
        serie.Items.Add(new BarItem(p.Q4) { Color = p.Q4 >= 0 ? ColorBarraPositiva : ColorBarraTraccion });
        m.Series.Add(serie);

        // Línea vertical en q_adm (porque las barras se extienden horizontalmente).
        if (qAdm > 0.0)
        {
            m.Annotations.Add(new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                X = qAdm,
                Color = ColorPresionAdmisible,
                LineStyle = LineStyle.Dash,
                StrokeThickness = 1.4,
                Text = $"q_adm = {qAdm:0} kN/m²",
                TextColor = ColorPresionAdmisible,
            });
        }

        m.InvalidatePlot(true);
    }

    private void ConstruirModeloSeccionZapata()
    {
        var m = ModeloSeccionZapata;
        m.Series.Clear();
        m.Axes.Clear();
        m.Annotations.Clear();

        var zapata = _zapataActiva;
        if (zapata is null)
        {
            m.InvalidatePlot(true);
            return;
        }

        double B = zapata.Dimensiones.LargoB;
        double L = zapata.Dimensiones.AnchoL;
        double bCol = zapata.Dimensiones.AnchoColumna;
        double lCol = zapata.Dimensiones.PeralteColumna;

        double rangoX = Math.Max(B, 0.001) * 0.575;
        double rangoY = Math.Max(L, 0.001) * 0.575;
        m.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "X (m)",
            Minimum = -rangoX,
            Maximum = +rangoX,
            TitleColor = ColorEje, TextColor = ColorEje,
            AxislineColor = ColorEje, TicklineColor = ColorEje,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromAColor(40, ColorEje),
        });
        m.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Y (m)",
            Minimum = -rangoY,
            Maximum = +rangoY,
            TitleColor = ColorEje, TextColor = ColorEje,
            AxislineColor = ColorEje, TicklineColor = ColorEje,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromAColor(40, ColorEje),
        });

        // Contorno de la zapata.
        var contornoZapata = new PolygonAnnotation
        {
            Fill = OxyColor.FromAColor(60, ColorConcreto),
            Stroke = ColorEje,
            StrokeThickness = 1.5,
        };
        contornoZapata.Points.Add(new DataPoint(-B / 2.0, -L / 2.0));
        contornoZapata.Points.Add(new DataPoint(+B / 2.0, -L / 2.0));
        contornoZapata.Points.Add(new DataPoint(+B / 2.0, +L / 2.0));
        contornoZapata.Points.Add(new DataPoint(-B / 2.0, +L / 2.0));
        m.Annotations.Add(contornoZapata);

        // Pedestal de la columna (centrado).
        var contornoPedestal = new PolygonAnnotation
        {
            Fill = OxyColor.FromAColor(80, ColorPedestal),
            Stroke = ColorEje,
            StrokeThickness = 1.5,
        };
        contornoPedestal.Points.Add(new DataPoint(-bCol / 2.0, -lCol / 2.0));
        contornoPedestal.Points.Add(new DataPoint(+bCol / 2.0, -lCol / 2.0));
        contornoPedestal.Points.Add(new DataPoint(+bCol / 2.0, +lCol / 2.0));
        contornoPedestal.Points.Add(new DataPoint(-bCol / 2.0, +lCol / 2.0));
        m.Annotations.Add(contornoPedestal);

        // Marcador de centroide.
        var centroide = new ScatterSeries
        {
            Title = "Centroide",
            MarkerType = MarkerType.Plus,
            MarkerSize = 6.0,
            MarkerStroke = ColorEje,
            MarkerStrokeThickness = 1.5,
        };
        centroide.Points.Add(new ScatterPoint(0.0, 0.0));
        m.Series.Add(centroide);

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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
