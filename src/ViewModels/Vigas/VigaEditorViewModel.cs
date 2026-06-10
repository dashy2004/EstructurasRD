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
using LosasPlus.Calculo;
using LosasPlus.Cargas;
using LosasPlus.Models;
using LosasPlus.Rendering;
using LosasPlus.Services;
using LosasPlus.Vigas;

namespace LosasPlus.ViewModels.Vigas;

/// <summary>
/// ViewModel del editor de vigas continuas (Fase 3, Iteración 2). Cuelga de
/// <c>MainViewModel</c> y administra la <see cref="VigaActiva"/> del nivel,
/// la edición de sus tramos/apoyos/cargas y el recálculo reactivo del motor
/// analítico <see cref="VigaContinuaEngine"/>.
///
/// <para>
/// Cada mutación estructural toma un snapshot de Undo y dispara un recálculo
/// en hilo secundario; el resultado regenera las series de los tres
/// <see cref="PlotModel"/> de OxyPlot (modelo físico, esfuerzos V/M y
/// deflexión δ). La lógica gráfica (OxyPlot) vive sólo en esta capa de
/// presentación — <c>src.Core</c> permanece libre de dependencias gráficas.
/// </para>
/// </summary>
public sealed class VigaEditorViewModel : INotifyPropertyChanged
{
    private const string OpcionEnvolvente = "Envolvente";

    private static readonly OxyColor ColorEje       = OxyColor.FromRgb(0x6B, 0x72, 0x80);
    private static readonly OxyColor ColorViga      = OxyColor.FromRgb(0x33, 0x3A, 0x45);
    private static readonly OxyColor ColorCortante  = OxyColor.FromRgb(0x0E, 0x7D, 0xB8);
    private static readonly OxyColor ColorMomento   = OxyColor.FromRgb(0xC2, 0x41, 0x1E);
    private static readonly OxyColor ColorDeflexion = OxyColor.FromRgb(0x2E, 0x7D, 0x32);
    private static readonly OxyColor ColorCarga     = OxyColor.FromRgb(0xB5, 0x8A, 0x00);

    private readonly Proyecto _proyecto;
    private readonly Action _pushUndoSnapshot;
    private readonly Func<Nivel?> _nivelActivoProvider;
    private Nivel Nivel => _nivelActivoProvider() ?? _proyecto.Edificios[0].Niveles[0];

    // Ítems del grafo de la viga activa con suscripción a PropertyChanged.
    private readonly List<TramoViga> _tramosEnganchados = new();
    private readonly List<ApoyoViga> _apoyosEnganchados = new();
    private readonly List<CargaElemento> _cargasEnganchadas = new();
    private Viga? _vigaEnganchada;

    private ResultadoViga? _resultado;
    private CancellationTokenSource? _ctsRecalculo;
    private Task _recalculoActual = Task.CompletedTask;

    public VigaEditorViewModel(Proyecto proyecto, Action pushUndoSnapshot, Func<Nivel?> nivelActivoProvider)
    {
        _proyecto = proyecto ?? throw new ArgumentNullException(nameof(proyecto));
        _pushUndoSnapshot = pushUndoSnapshot ?? throw new ArgumentNullException(nameof(pushUndoSnapshot));
        _nivelActivoProvider = nivelActivoProvider ?? throw new ArgumentNullException(nameof(nivelActivoProvider));

        _proyecto.AsegurarEstructura();

        ModeloViga      = CrearModeloBase("Modelo de la viga");
        ModeloEsfuerzos = CrearModeloBase("Cortante V(x) y Momento M(x)");
        ModeloDeflexion = CrearModeloBase("Deflexión δ(x)");
        ModeloSeccion   = CrearModeloBase("Sección Transversal");

        NuevaVigaCommand     = new RelayCommand(_ => NuevaViga());
        EliminarVigaCommand  = new RelayCommand(_ => EliminarViga(),  _ => _vigaActiva is not null);
        AgregarTramoCommand  = new RelayCommand(_ => AgregarTramo(),  _ => _vigaActiva is not null);
        EliminarTramoCommand = new RelayCommand(_ => EliminarTramo(), _ => _tramoSeleccionado is not null);
        AgregarApoyoCommand  = new RelayCommand(_ => AgregarApoyo(),  _ => _vigaActiva is not null);
        EliminarApoyoCommand = new RelayCommand(_ => EliminarApoyo(), _ => _apoyoSeleccionado is not null);
        AgregarCargaCommand  = new RelayCommand(_ => AgregarCarga(),  _ => _tramoSeleccionado is not null);
        EliminarCargaCommand = new RelayCommand(_ => EliminarCarga(), _ => _cargaSeleccionada is not null);
        GenerarVigasCommand  = new RelayCommand(_ => GenerarVigas());

        _proyecto.Combinaciones.Combinaciones.CollectionChanged += OnCombinacionesCambiaron;

        RefrescarOpcionesCombinacion();
        VigaActiva = Nivel.Vigas.FirstOrDefault();
    }

    // ---- Colecciones pass-through ----

    /// <summary>Vigas del nivel activo.</summary>
    public ObservableCollection<Viga> Vigas => Nivel.Vigas;

    /// <summary>Cargas del tramo seleccionado — alimenta el grid de cargas.</summary>
    public ObservableCollection<CargaElemento>? CargasDelTramo => _tramoSeleccionado?.Cargas;

    /// <summary>Casos de carga vivos del proyecto (Fase 2) — para el ComboBox de cargas.</summary>
    public ObservableCollection<CasoCarga> CasosDisponibles => _proyecto.Combinaciones.Casos;

    public Array TiposApoyo => Enum.GetValues(typeof(TipoApoyo));
    public Array TiposCarga => Enum.GetValues(typeof(TipoCargaElemento));

    // ---- Selección ----

    private Viga? _vigaActiva;
    /// <summary>Viga en edición. Cambiarla reengancha el grafo y recalcula.</summary>
    public Viga? VigaActiva
    {
        get => _vigaActiva;
        set
        {
            if (ReferenceEquals(_vigaActiva, value)) return;
            _vigaActiva = value;
            RehookViga(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HayVigaActiva));
            TramoSeleccionado = value?.Tramos.FirstOrDefault();
            ApoyoSeleccionado = value?.Apoyos.FirstOrDefault();
            // Revaluar predicados que leen _vigaActiva.
            EliminarVigaCommand.RaiseCanExecuteChanged();
            AgregarTramoCommand.RaiseCanExecuteChanged();
            AgregarApoyoCommand.RaiseCanExecuteChanged();
            SolicitarRecalculo();
        }
    }

    /// <summary><c>true</c> si hay una viga en edición.</summary>
    public bool HayVigaActiva => _vigaActiva is not null;

    private TramoViga? _tramoSeleccionado;
    public TramoViga? TramoSeleccionado
    {
        get => _tramoSeleccionado;
        set
        {
            _tramoSeleccionado = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CargasDelTramo));
            CargaSeleccionada = value?.Cargas.FirstOrDefault();
            // Revaluar predicados que leen _tramoSeleccionado.
            EliminarTramoCommand.RaiseCanExecuteChanged();
            AgregarCargaCommand.RaiseCanExecuteChanged();
            ConstruirModeloSeccion();
            // La sección cambia al seleccionar otro tramo sin recálculo: re-render del PNG.
            SeccionPng = DiagramaPng.Render(ModeloSeccion, PngAnchoSeccion, PngAltoSeccion);
        }
    }

    private ApoyoViga? _apoyoSeleccionado;
    public ApoyoViga? ApoyoSeleccionado
    {
        get => _apoyoSeleccionado;
        set
        {
            _apoyoSeleccionado = value;
            OnPropertyChanged();
            // Revaluar predicado que lee _apoyoSeleccionado.
            EliminarApoyoCommand.RaiseCanExecuteChanged();
        }
    }

    private CargaElemento? _cargaSeleccionada;
    public CargaElemento? CargaSeleccionada
    {
        get => _cargaSeleccionada;
        set
        {
            _cargaSeleccionada = value;
            OnPropertyChanged();
            // Revaluar predicado que lee _cargaSeleccionada.
            EliminarCargaCommand.RaiseCanExecuteChanged();
        }
    }

    // ---- Selector de combinación / envolvente ----

    private readonly ObservableCollection<string> _opcionesCombinacion = new();
    /// <summary>Nombres de las combinaciones del proyecto más «Envolvente».</summary>
    public ObservableCollection<string> OpcionesCombinacion => _opcionesCombinacion;

    private string _combinacionSeleccionada = OpcionEnvolvente;
    /// <summary>Combinación cuyos diagramas se muestran («Envolvente» = envolvente de diseño).</summary>
    public string CombinacionSeleccionada
    {
        get => _combinacionSeleccionada;
        set
        {
            if (_combinacionSeleccionada == value || value is null) return;
            _combinacionSeleccionada = value;
            OnPropertyChanged();
            ConstruirSeries();   // re-grafica desde el resultado en caché — sin re-ejecutar el motor
        }
    }

    // ---- Diagramas OxyPlot ----

    /// <summary>Diagrama del modelo físico de la viga (eje, apoyos, cargas).</summary>
    public PlotModel ModeloViga { get; }

    /// <summary>Diagrama de fuerza cortante V(x) y momento flector M(x).</summary>
    public PlotModel ModeloEsfuerzos { get; }

    /// <summary>Diagrama de deflexión elástica δ(x).</summary>
    public PlotModel ModeloDeflexion { get; }

    /// <summary>Diagrama de la sección transversal de la viga.</summary>
    public PlotModel ModeloSeccion { get; }

    // ── Workaround del bug de render de oxy:PlotView (Avalonia 11) ──────────────
    // El control no pinta las series de cortante/deflexión (sí momento). Como el
    // PlotModel y sus datos son correctos (lo prueba la paridad con OxyPlot-core),
    // se renderizan a PNG con DiagramaPng y la vista los muestra en un <Image>.
    private const int PngAncho = 1100;
    private const int PngAltoEsfuerzos = 380;
    private const int PngAltoDeflexion = 320;
    private const int PngAltoViga = 320;
    private const int PngAnchoSeccion = 600;
    private const int PngAltoSeccion = 640;

    private byte[]? _esfuerzosPng;
    /// <summary>PNG del diagrama de esfuerzos (V/M) para mostrar como imagen.</summary>
    public byte[]? EsfuerzosPng
    {
        get => _esfuerzosPng;
        private set { _esfuerzosPng = value; OnPropertyChanged(); }
    }

    private byte[]? _deflexionPng;
    /// <summary>PNG del diagrama de deflexión (δ) para mostrar como imagen.</summary>
    public byte[]? DeflexionPng
    {
        get => _deflexionPng;
        private set { _deflexionPng = value; OnPropertyChanged(); }
    }

    private byte[]? _vigaPng;
    /// <summary>PNG del modelo físico de la viga para mostrar como imagen.</summary>
    public byte[]? VigaPng
    {
        get => _vigaPng;
        private set { _vigaPng = value; OnPropertyChanged(); }
    }

    private byte[]? _seccionPng;
    /// <summary>PNG de la sección transversal del tramo seleccionado para mostrar como imagen.</summary>
    public byte[]? SeccionPng
    {
        get => _seccionPng;
        private set { _seccionPng = value; OnPropertyChanged(); }
    }

    private bool _esInestable;
    /// <summary><c>true</c> si la viga activa es un mecanismo (no resoluble).</summary>
    public bool EsInestable
    {
        get => _esInestable;
        private set { _esInestable = value; OnPropertyChanged(); }
    }

    private string _mensajeEstado = "";
    /// <summary>Mensaje del panel de diagramas (causa de inestabilidad o error).</summary>
    public string MensajeEstado
    {
        get => _mensajeEstado;
        private set { _mensajeEstado = value; OnPropertyChanged(); }
    }

    // ---- Comandos ----
    // Tipados como RelayCommand (no ICommand) para poder llamar RaiseCanExecuteChanged()
    // desde los setters de selección — Avalonia carece de CommandManager.RequerySuggested.

    public RelayCommand NuevaVigaCommand { get; }
    public RelayCommand EliminarVigaCommand { get; }
    public RelayCommand AgregarTramoCommand { get; }
    public RelayCommand EliminarTramoCommand { get; }
    public RelayCommand AgregarApoyoCommand { get; }
    public RelayCommand EliminarApoyoCommand { get; }
    public RelayCommand AgregarCargaCommand { get; }
    public RelayCommand EliminarCargaCommand { get; }
    public RelayCommand GenerarVigasCommand { get; }

    /// <summary>
    /// Lo invoca el code-behind desde <c>DataGrid.BeginningEdit</c>: toma un
    /// snapshot ANTES de que la celda cambie, para que Ctrl+Z lo revierta.
    /// </summary>
    public void SnapshotAntesDeEditar() => _pushUndoSnapshot();

    /// <summary>
    /// Lo invoca <c>MainViewModel</c> cuando cambia el <c>NivelActivo</c> (D1):
    /// el editor debe mostrar las vigas del nuevo nivel y no las del anterior.
    /// Refresca la colección <see cref="Vigas"/> (cambió el nivel detrás del
    /// pass-through) y reengancha la viga activa al primer elemento del nuevo
    /// nivel, lo que dispara el recálculo de diagramas. Se fuerza incluso si la
    /// primera viga del nuevo nivel coincide por referencia (caso ambos null) para
    /// no dejar la colección antigua visible.
    /// </summary>
    public void RefrescarPorCambioDeNivel()
    {
        OnPropertyChanged(nameof(Vigas));
        var primera = Nivel.Vigas.FirstOrDefault();
        if (ReferenceEquals(_vigaActiva, primera))
        {
            // El setter de VigaActiva haría short-circuit; re-enganchar y recalcular a mano.
            RehookViga(primera);
            TramoSeleccionado = primera?.Tramos.FirstOrDefault();
            ApoyoSeleccionado = primera?.Apoyos.FirstOrDefault();
            OnPropertyChanged(nameof(HayVigaActiva));
            EliminarVigaCommand.RaiseCanExecuteChanged();
            AgregarTramoCommand.RaiseCanExecuteChanged();
            AgregarApoyoCommand.RaiseCanExecuteChanged();
            SolicitarRecalculo();
        }
        else
        {
            VigaActiva = primera;   // recorre el setter completo (rehook + recalc)
        }
    }

    /// <summary>
    /// Lo invoca <c>MainViewModel</c> tras un Undo/Redo: las colecciones del
    /// nivel se reemplazaron en bloque, así que se reengancha la viga, se
    /// revalida la selección y se recalcula.
    /// </summary>
    public void NotificarRestauracion()
    {
        RefrescarOpcionesCombinacion();
        OnPropertyChanged(nameof(Vigas));
        _vigaActiva = Nivel.Vigas.FirstOrDefault();
        OnPropertyChanged(nameof(VigaActiva));
        OnPropertyChanged(nameof(HayVigaActiva));
        RehookViga(_vigaActiva);
        TramoSeleccionado = _vigaActiva?.Tramos.FirstOrDefault();
        ApoyoSeleccionado = _vigaActiva?.Apoyos.FirstOrDefault();
        SolicitarRecalculo();
    }

    // ---- Mutaciones (cada una toma un snapshot de Undo) ----

    private void NuevaViga()
    {
        _pushUndoSnapshot();
        var viga = new Viga
        {
            Id = Nivel.Vigas.Count == 0 ? 1 : Nivel.Vigas.Max(v => v.Id) + 1,
            Nombre = $"Viga {Nivel.Vigas.Count + 1}",
        };
        var tramo = new TramoViga();
        viga.Tramos.Add(tramo);
        viga.Apoyos.Add(new ApoyoViga(0.0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(tramo.Longitud, TipoApoyo.Fijo));
        Nivel.Vigas.Add(viga);
        VigaActiva = viga;
    }

    private void EliminarViga()
    {
        if (_vigaActiva is null) return;
        _pushUndoSnapshot();
        int idx = Nivel.Vigas.IndexOf(_vigaActiva);
        Nivel.Vigas.Remove(_vigaActiva);
        VigaActiva = Nivel.Vigas.Count > 0
            ? Nivel.Vigas[Math.Min(idx, Nivel.Vigas.Count - 1)]
            : null;
    }

    private void AgregarTramo()
    {
        if (_vigaActiva is null) return;
        _pushUndoSnapshot();
        var tramo = new TramoViga();
        _vigaActiva.Tramos.Add(tramo);
        TramoSeleccionado = tramo;
    }

    private void EliminarTramo()
    {
        if (_vigaActiva is null || _tramoSeleccionado is null) return;
        _pushUndoSnapshot();
        _vigaActiva.Tramos.Remove(_tramoSeleccionado);
        TramoSeleccionado = _vigaActiva.Tramos.FirstOrDefault();
    }

    private void AgregarApoyo()
    {
        if (_vigaActiva is null) return;
        _pushUndoSnapshot();
        var apoyo = new ApoyoViga(0.0, TipoApoyo.Fijo);
        _vigaActiva.Apoyos.Add(apoyo);
        ApoyoSeleccionado = apoyo;
    }

    private void EliminarApoyo()
    {
        if (_vigaActiva is null || _apoyoSeleccionado is null) return;
        _pushUndoSnapshot();
        _vigaActiva.Apoyos.Remove(_apoyoSeleccionado);
        ApoyoSeleccionado = _vigaActiva.Apoyos.FirstOrDefault();
    }

    private void AgregarCarga()
    {
        if (_tramoSeleccionado is null) return;
        _pushUndoSnapshot();
        var carga = new CargaElemento(
            TipoCargaElemento.Distribuida, 0.0,
            _proyecto.Combinaciones.Casos.FirstOrDefault()?.Codigo ?? "");
        _tramoSeleccionado.Cargas.Add(carga);
        CargaSeleccionada = carga;
    }

    private void EliminarCarga()
    {
        if (_tramoSeleccionado is null || _cargaSeleccionada is null) return;
        _pushUndoSnapshot();
        _tramoSeleccionado.Cargas.Remove(_cargaSeleccionada);
        CargaSeleccionada = _tramoSeleccionado.Cargas.FirstOrDefault();
    }

    private void GenerarVigas()
    {
        _pushUndoSnapshot();
        GeneradorVigas.MaterializarVigas(Nivel);
        VigaActiva = Nivel.Vigas.FirstOrDefault();
    }

    // ---- Reactividad: enganche del grafo de la viga ----

    private void RehookViga(Viga? viga)
    {
        if (_vigaEnganchada is not null)
        {
            _vigaEnganchada.Tramos.CollectionChanged -= OnTramosCambiaron;
            _vigaEnganchada.Apoyos.CollectionChanged -= OnApoyosCambiaron;
        }
        foreach (var t in _tramosEnganchados)
        {
            t.PropertyChanged -= OnElementoCambiado;
            t.Cargas.CollectionChanged -= OnCargasCambiaron;
        }
        foreach (var a in _apoyosEnganchados) a.PropertyChanged -= OnElementoCambiado;
        foreach (var c in _cargasEnganchadas) c.PropertyChanged -= OnElementoCambiado;
        _tramosEnganchados.Clear();
        _apoyosEnganchados.Clear();
        _cargasEnganchadas.Clear();

        _vigaEnganchada = viga;
        if (viga is null) return;

        viga.Tramos.CollectionChanged += OnTramosCambiaron;
        viga.Apoyos.CollectionChanged += OnApoyosCambiaron;
        foreach (var t in viga.Tramos)
        {
            t.PropertyChanged += OnElementoCambiado;
            t.Cargas.CollectionChanged += OnCargasCambiaron;
            _tramosEnganchados.Add(t);
            foreach (var c in t.Cargas)
            {
                c.PropertyChanged += OnElementoCambiado;
                _cargasEnganchadas.Add(c);
            }
        }
        foreach (var a in viga.Apoyos)
        {
            a.PropertyChanged += OnElementoCambiado;
            _apoyosEnganchados.Add(a);
        }
    }

    private void OnTramosCambiaron(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RehookViga(_vigaActiva);
        if (_tramoSeleccionado is not null && _vigaActiva is not null
            && !_vigaActiva.Tramos.Contains(_tramoSeleccionado))
            TramoSeleccionado = _vigaActiva.Tramos.FirstOrDefault();
        SolicitarRecalculo();
    }

    private void OnApoyosCambiaron(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RehookViga(_vigaActiva);
        if (_apoyoSeleccionado is not null && _vigaActiva is not null
            && !_vigaActiva.Apoyos.Contains(_apoyoSeleccionado))
            ApoyoSeleccionado = _vigaActiva.Apoyos.FirstOrDefault();
        SolicitarRecalculo();
    }

    private void OnCargasCambiaron(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RehookViga(_vigaActiva);
        SolicitarRecalculo();
    }

    private void OnElementoCambiado(object? sender, PropertyChangedEventArgs e) => SolicitarRecalculo();

    private void OnCombinacionesCambiaron(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefrescarOpcionesCombinacion();
        SolicitarRecalculo();
    }

    private void RefrescarOpcionesCombinacion()
    {
        _opcionesCombinacion.Clear();
        _opcionesCombinacion.Add(OpcionEnvolvente);
        foreach (var c in _proyecto.Combinaciones.Combinaciones)
            _opcionesCombinacion.Add(c.Nombre);
        if (!_opcionesCombinacion.Contains(_combinacionSeleccionada))
        {
            _combinacionSeleccionada = OpcionEnvolvente;
            OnPropertyChanged(nameof(CombinacionSeleccionada));
        }
    }

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
    /// determinista: los recálculos anteriores en curso quedan cancelados y no
    /// llegan a tocar los diagramas.
    /// </summary>
    public Task RecalcularAsync()
    {
        SolicitarRecalculo();
        return _recalculoActual;
    }

    private async Task RecalcularNucleoAsync(CancellationToken token)
    {
        var viga = _vigaActiva;
        if (viga is null)
        {
            LimpiarDiagramas();
            return;
        }
        var combinaciones = _proyecto.Combinaciones;
        try
        {
            // El motor corre en hilo secundario; el ensamblado de series
            // reanuda en el contexto capturado (UI en la app).
            var resultado = await Task.Run(
                () => VigaContinuaEngine.Resolver(viga, combinaciones), token);
            if (token.IsCancellationRequested) return;

            _resultado = resultado;
            EsInestable = resultado.EsInestable;
            MensajeEstado = resultado.EsInestable
                ? (resultado.Mensaje ?? "La viga es inestable.")
                : "";

            // Revalidar la combinación seleccionada contra el nuevo resultado.
            if (_combinacionSeleccionada != OpcionEnvolvente
                && !resultado.Combinaciones.Any(c => c.NombreCombinacion == _combinacionSeleccionada))
            {
                _combinacionSeleccionada = OpcionEnvolvente;
                OnPropertyChanged(nameof(CombinacionSeleccionada));
            }
            ConstruirSeries();
        }
        catch (OperationCanceledException)
        {
            // Recálculo superado por otro más reciente — descartar.
        }
        catch (Exception ex)
        {
            EsInestable = true;
            MensajeEstado = "Error al resolver la viga: " + ex.Message;
        }
    }

    // ---- Construcción de las series OxyPlot ----

    private bool EsEnvolvente => _combinacionSeleccionada == OpcionEnvolvente;

    private void ConstruirSeries()
    {
        ConstruirModeloViga();
        ConstruirModeloEsfuerzos();
        ConstruirModeloDeflexion();
        ConstruirModeloSeccion();
        // Render a imagen de los dos diagramas que oxy:PlotView deja en blanco.
        EsfuerzosPng = DiagramaPng.Render(ModeloEsfuerzos, PngAncho, PngAltoEsfuerzos);
        DeflexionPng = DiagramaPng.Render(ModeloDeflexion, PngAncho, PngAltoDeflexion);
        VigaPng = DiagramaPng.Render(ModeloViga, PngAncho, PngAltoViga);
        SeccionPng = DiagramaPng.Render(ModeloSeccion, PngAnchoSeccion, PngAltoSeccion);
    }

    private void LimpiarDiagramas()
    {
        _resultado = null;
        EsInestable = false;
        MensajeEstado = "";
        foreach (var m in new[] { ModeloViga, ModeloEsfuerzos, ModeloDeflexion, ModeloSeccion })
        {
            m.Series.Clear();
            m.Annotations.Clear();
            m.InvalidatePlot(true);
        }
        EsfuerzosPng = null;
        DeflexionPng = null;
        VigaPng = null;
        SeccionPng = null;
    }

    private void ConstruirModeloViga()
    {
        var m = ModeloViga;
        m.Series.Clear();
        m.Axes.Clear();
        m.Annotations.Clear();

        var viga = _vigaActiva;
        if (viga is null || viga.Tramos.Count == 0)
        {
            m.InvalidatePlot(true);
            return;
        }

        m.Axes.Add(Eje(AxisPosition.Bottom, "x (m)"));
        m.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left, IsAxisVisible = false,
            Minimum = -1.4, Maximum = 1.8,
        });

        double total = viga.Tramos.Sum(t => t.Longitud);
        var eje = new LineSeries { Color = ColorViga, StrokeThickness = 3.0 };
        eje.Points.Add(new DataPoint(0.0, 0.0));
        eje.Points.Add(new DataPoint(total, 0.0));
        m.Series.Add(eje);

        // Apoyos — marcador cromático por tipo.
        foreach (TipoApoyo tipo in Enum.GetValues(typeof(TipoApoyo)))
        {
            var puntos = viga.Apoyos
                .Where(a => a.Tipo == tipo)
                .Select(a => new ScatterPoint(Math.Clamp(a.CoordenadaX, 0.0, total), 0.0))
                .ToList();
            if (puntos.Count == 0) continue;
            var serie = new ScatterSeries
            {
                Title = tipo.ToString(),
                MarkerType = MarcadorApoyo(tipo),
                MarkerSize = 7.0,
                MarkerFill = ColorApoyo(tipo),
            };
            serie.Points.AddRange(puntos);
            m.Series.Add(serie);
        }

        // Cargas — flechas (puntuales) y bandas (distribuidas).
        double inicio = 0.0;
        foreach (var tramo in viga.Tramos)
        {
            foreach (var carga in tramo.Cargas)
            {
                if (carga.Tipo == TipoCargaElemento.Puntual)
                {
                    double x = Math.Clamp(inicio + carga.Posicion, 0.0, total);
                    m.Annotations.Add(new ArrowAnnotation
                    {
                        StartPoint = new DataPoint(x, 1.3),
                        EndPoint = new DataPoint(x, 0.0),
                        Color = ColorCarga, StrokeThickness = 2.0,
                        Text = carga.Magnitud.ToString("0.##"),
                    });
                }
                else
                {
                    m.Annotations.Add(new RectangleAnnotation
                    {
                        MinimumX = inicio, MaximumX = inicio + tramo.Longitud,
                        MinimumY = 0.0, MaximumY = 0.6,
                        Fill = OxyColor.FromAColor(60, ColorCarga),
                        Stroke = ColorCarga, StrokeThickness = 1.0,
                        Text = carga.Magnitud.ToString("0.##"),
                    });
                }
            }
            inicio += tramo.Longitud;
        }

        m.InvalidatePlot(true);
    }

    private void ConstruirModeloEsfuerzos()
    {
        var m = ModeloEsfuerzos;
        m.Series.Clear();
        m.Axes.Clear();
        m.Annotations.Clear();

        if (_resultado is null || _resultado.EsInestable)
        {
            m.InvalidatePlot(true);
            return;
        }

        m.Axes.Add(Eje(AxisPosition.Bottom, "x (m)"));
        m.Axes.Add(Eje(AxisPosition.Left, "V (kN)", key: "V"));
        // Eje del momento invertido: convención reglamentaria — tracción
        // inferior positiva dibujada del lado de la tracción.
        m.Axes.Add(Eje(AxisPosition.Right, "M (kN·m)", key: "M", invertido: true));

        if (EsEnvolvente)
        {
            var env = _resultado.Envolvente;
            if (env is not null)
            {
                m.Series.Add(Linea(env.Puntos.Select(p => new DataPoint(p.X, p.CortanteMaximo)), "V máx", ColorCortante, "V"));
                m.Series.Add(Linea(env.Puntos.Select(p => new DataPoint(p.X, p.CortanteMinimo)), "V mín", ColorCortante, "V", LineStyle.Dash));
                m.Series.Add(Linea(env.Puntos.Select(p => new DataPoint(p.X, p.MomentoMaximo)), "M máx", ColorMomento, "M"));
                m.Series.Add(Linea(env.Puntos.Select(p => new DataPoint(p.X, p.MomentoMinimo)), "M mín", ColorMomento, "M", LineStyle.Dash));
            }
        }
        else
        {
            var combo = _resultado.Combinaciones
                .FirstOrDefault(c => c.NombreCombinacion == _combinacionSeleccionada);
            if (combo is not null)
            {
                m.Series.Add(Linea(combo.Diagrama.Select(p => new DataPoint(p.X, p.Cortante)), "V(x)", ColorCortante, "V"));
                m.Series.Add(Linea(combo.Diagrama.Select(p => new DataPoint(p.X, p.Momento)), "M(x)", ColorMomento, "M"));
            }
        }
        m.InvalidatePlot(true);
    }

    private void ConstruirModeloDeflexion()
    {
        var m = ModeloDeflexion;
        m.Series.Clear();
        m.Axes.Clear();
        m.Annotations.Clear();

        if (_resultado is null || _resultado.EsInestable)
        {
            m.InvalidatePlot(true);
            return;
        }

        m.Axes.Add(Eje(AxisPosition.Bottom, "x (m)"));
        // δ positiva hacia abajo → eje invertido para dibujarla descendente.
        m.Axes.Add(Eje(AxisPosition.Left, "δ (m)", invertido: true));

        if (EsEnvolvente)
        {
            var env = _resultado.Envolvente;
            if (env is not null)
            {
                m.Series.Add(Linea(env.Puntos.Select(p => new DataPoint(p.X, p.DeflexionMaxima)), "δ máx", ColorDeflexion));
                m.Series.Add(Linea(env.Puntos.Select(p => new DataPoint(p.X, p.DeflexionMinima)), "δ mín", ColorDeflexion, null, LineStyle.Dash));
            }
        }
        else
        {
            var combo = _resultado.Combinaciones
                .FirstOrDefault(c => c.NombreCombinacion == _combinacionSeleccionada);
            if (combo is not null)
                m.Series.Add(Linea(combo.Diagrama.Select(p => new DataPoint(p.X, p.Deflexion)), "δ(x)", ColorDeflexion));
        }
        m.InvalidatePlot(true);
    }

    private void ConstruirModeloSeccion()
    {
        var m = ModeloSeccion;
        m.Series.Clear();
        m.Axes.Clear();
        m.Annotations.Clear();

        var tramo = _tramoSeleccionado;
        if (tramo is null)
        {
            m.InvalidatePlot(true);
            return;
        }

        double b = tramo.Base;
        double h = tramo.Peralte;
        if (b <= 0 || h <= 0) { m.InvalidatePlot(true); return; }

        // Límites isométricos (holgura para cotas y el resumen de armado).
        m.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Minimum = -b * 0.40, Maximum = b * 1.40, IsAxisVisible = false });
        m.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Minimum = -h * 0.28, Maximum = h * 1.32, IsAxisVisible = false });

        // Rectángulo de concreto. BelowSeries: si quedara encima (default
        // AboveSeries), su gris semitransparente lavaría las barras de refuerzo.
        m.Annotations.Add(new RectangleAnnotation
        {
            MinimumX = 0, MaximumX = b,
            MinimumY = 0, MaximumY = h,
            Fill = OxyColor.FromAColor(80, OxyColors.Gray),
            Stroke = OxyColors.Black,
            StrokeThickness = 2,
            Layer = AnnotationLayer.BelowSeries
        });

        // Cotas b (abajo) y h (al costado).
        m.Annotations.Add(TextoSeccion($"b = {b:0.##} m", b / 2.0, -h * 0.14, OxyPlot.HorizontalAlignment.Center));
        m.Annotations.Add(TextoSeccion($"h = {h:0.##} m", -b * 0.20, h / 2.0, OxyPlot.HorizontalAlignment.Center, rotation: -90));

        double rec = VigaFlexionDesigner.RecubrimientoDefaultM;
        if (b <= 2 * rec || h <= 2 * rec) { m.InvalidatePlot(true); return; }

        // Estribo (BelowSeries: las barras se dibujan encima, como en la realidad).
        m.Annotations.Add(new RectangleAnnotation
        {
            MinimumX = rec, MaximumX = b - rec,
            MinimumY = rec, MaximumY = h - rec,
            Fill = OxyColors.Transparent,
            Stroke = OxyColors.DarkRed,
            StrokeThickness = 1.5,
            Layer = AnnotationLayer.BelowSeries
        });

        // ---- Armado real: nº de barras desde la envolvente de diseño ----
        // Cara inferior por el momento positivo (tracción abajo), superior por el
        // negativo, tomados de la envolvente DENTRO del tramo seleccionado (cada
        // tramo arma según sus propios momentos). Sin resultado válido aún → 0/0
        // y gobierna el As mínimo.
        var (mPos, mNeg) = MomentosDeDisenoDelTramo(tramo);

        // f'c desde el módulo del tramo (Ec = 4700·√f'c, ACI 318 §19.2.2.1).
        double eMPa = tramo.ModuloElasticidad / 1000.0;            // kN/m² → MPa
        double fcMPa = eMPa > 0 ? Math.Pow(eMPa / 4700.0, 2) : 28.0;
        // fy desde el proyecto (no hard-codeado): FyKgCm2 (kg/cm²) → MPa.
        // 1 kgf/cm² = 0.0980665 MPa ⇒ 4200 kg/cm² ≈ 411.9 MPa (grado 60).
        double fyMPa = _proyecto.FyKgCm2 > 0 ? _proyecto.FyKgCm2 * 0.0980665 : 420.0;

        var dis = VigaFlexionDesigner.DisenarTramo(mPos, mNeg, b, h, fcMPa, fyMPa);
        AgregarBarrasSeccion(m, dis.Inferior.NumeroDeBarras, rec, b - rec, rec);        // inferior (+M)
        AgregarBarrasSeccion(m, dis.Superior.NumeroDeBarras, rec, b - rec, h - rec);    // superior (−M)

        // Resumen de armado (arriba de la sección).
        m.Annotations.Add(TextoSeccion(
            $"Sup: {ResumenCara(dis.Superior)}   ·   Inf: {ResumenCara(dis.Inferior)}",
            b / 2.0, h * 1.18, OxyPlot.HorizontalAlignment.Center));

        m.InvalidatePlot(true);
    }

    /// <summary>
    /// Momentos de diseño (positivo y negativo, kN·m) de la envolvente
    /// restringidos al span del <paramref name="tramo"/> seleccionado, para que
    /// la sección arme según sus propios momentos. Si no hay envolvente o el
    /// tramo no está en la viga, cae a la envolvente global; (0,0) si no hay
    /// resultado válido (→ gobierna el As mínimo).
    /// </summary>
    private (double mPos, double mNeg) MomentosDeDisenoDelTramo(TramoViga tramo)
    {
        var env = _resultado is { EsInestable: false } ? _resultado.Envolvente : null;
        if (env is null) return (0.0, 0.0);
        if (_vigaActiva is null) return (env.MomentoMaximo, env.MomentoMinimo);

        int idx = _vigaActiva.Tramos.IndexOf(tramo);
        if (idx < 0) return (env.MomentoMaximo, env.MomentoMinimo);

        double x0 = 0.0;
        for (int i = 0; i < idx; i++) x0 += _vigaActiva.Tramos[i].Longitud;
        double x1 = x0 + tramo.Longitud;

        const double tol = 1e-6;
        var enRango = env.Puntos.Where(p => p.X >= x0 - tol && p.X <= x1 + tol).ToList();
        if (enRango.Count == 0) return (env.MomentoMaximo, env.MomentoMinimo);
        return (enRango.Max(p => p.MomentoMaximo), enRango.Min(p => p.MomentoMinimo));
    }

    /// <summary>Dibuja <paramref name="n"/> barras (puntos) repartidas entre x0 y x1 a la altura y.</summary>
    private static void AgregarBarrasSeccion(PlotModel m, int n, double x0, double x1, double y)
    {
        if (n <= 0) return;
        var scatter = new ScatterSeries
        {
            MarkerType = MarkerType.Circle, MarkerSize = 5,
            MarkerFill = OxyColors.DarkBlue, MarkerStroke = OxyColors.White, MarkerStrokeThickness = 1,
        };
        if (n == 1)
            scatter.Points.Add(new ScatterPoint((x0 + x1) / 2.0, y));
        else
            for (int i = 0; i < n; i++)
                scatter.Points.Add(new ScatterPoint(x0 + (x1 - x0) * i / (n - 1), y));
        m.Series.Add(scatter);
    }

    /// <summary>Texto corto del armado de una cara: «4#5» o «sección insuf.».</summary>
    private static string ResumenCara(DisenoFlexionViga c)
        => c.SeccionInsuficiente ? "sección insuf." : $"{c.NumeroDeBarras}#{c.NumeroBarra}";

    /// <summary>Anotación de texto reutilizable para cotas y rótulos de la sección.</summary>
    private static TextAnnotation TextoSeccion(string texto, double x, double y,
        OxyPlot.HorizontalAlignment ha, double rotation = 0)
        => new()
        {
            Text = texto,
            TextPosition = new DataPoint(x, y),
            TextHorizontalAlignment = ha,
            TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle,
            TextColor = ColorEje,
            FontSize = 11,
            TextRotation = rotation,
            StrokeThickness = 0,
        };

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

    private static LinearAxis Eje(
        AxisPosition posicion, string titulo, string? key = null, bool invertido = false)
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
        if (key is not null) eje.Key = key;
        if (invertido) { eje.StartPosition = 1.0; eje.EndPosition = 0.0; }
        return eje;
    }

    private static LineSeries Linea(
        IEnumerable<DataPoint> puntos, string titulo, OxyColor color,
        string? ejeY = null, LineStyle estilo = LineStyle.Solid)
    {
        var serie = new LineSeries
        {
            Title = titulo,
            Color = color,
            StrokeThickness = 1.6,
            LineStyle = estilo,
        };
        if (ejeY is not null) serie.YAxisKey = ejeY;
        serie.Points.AddRange(puntos);
        return serie;
    }

    private static MarkerType MarcadorApoyo(TipoApoyo tipo) => tipo switch
    {
        TipoApoyo.Fijo => MarkerType.Triangle,
        TipoApoyo.Empotrado => MarkerType.Square,
        _ => MarkerType.Diamond,
    };

    private static OxyColor ColorApoyo(TipoApoyo tipo) => tipo switch
    {
        TipoApoyo.Fijo => OxyColor.FromRgb(0x0E, 0x7D, 0xB8),
        TipoApoyo.Empotrado => OxyColor.FromRgb(0x33, 0x3A, 0x45),
        _ => OxyColor.FromRgb(0x2E, 0x7D, 0x32),
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
