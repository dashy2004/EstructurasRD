using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Collections;
using System.Windows.Input;
using LosasPlus.Cargas;
using LosasPlus.Models;

namespace LosasPlus.ViewModels;

/// <summary>
/// ViewModel de la pestaña «Cargas y Combinaciones» (Fase 2, Iteración 4 —
/// rediseño a matriz de factores). Gestiona los casos y combinaciones de carga
/// del proyecto — la base de diseño transversal en
/// <see cref="Proyecto.Combinaciones"/>.
///
/// <para>
/// Las combinaciones se exponen segmentadas en dos <see cref="ICollectionView"/>
/// (<see cref="CombinacionesServicio"/> / <see cref="CombinacionesUltimas"/>) —
/// una por pestaña de la matriz. El panel de resumen (<see cref="CantidadCasos"/>,
/// <see cref="AlertasNormativas"/>, <see cref="EstadoValidacion"/>…) se recomputa
/// reactivamente ante cualquier cambio en los casos, las combinaciones o sus
/// términos.
/// </para>
///
/// <para>
/// Toda mutación de las colecciones (alta, baja y edición de celdas) se envuelve
/// en un snapshot de Undo para que Ctrl+Z funcione perfectamente. Las altas y
/// bajas pasan por comandos; la edición inline —incluida la matriz de factores—
/// la captura el code-behind de la vista vía <see cref="SnapshotAntesDeEditar"/>.
/// </para>
/// </summary>
public sealed class CargasCombinacionesViewModel : INotifyPropertyChanged
{
    private readonly Proyecto _proyecto;
    private readonly Action _pushUndoSnapshot;

    // Dos vistas filtradas sobre la MISMA colección de combinaciones — una por
    // pestaña. El filtro se reevalúa con Refresh() cuando cambia un Tipo.
    // Port a Avalonia: WPF CollectionViewSource/ICollectionView → DataGridCollectionView.
    private readonly DataGridCollectionView _vistaServicio;
    private readonly DataGridCollectionView _vistaUltimas;

    // Ítems con suscripción a PropertyChanged activa — para poder desengancharlos
    // antes de reenganchar tras un cambio de colección (o un Undo).
    private readonly List<CasoCarga> _casosEnganchados = new();
    private readonly List<CombinacionCarga> _combosEnganchados = new();

    private IReadOnlyList<string> _alertas = new List<string>();

    public CargasCombinacionesViewModel(Proyecto proyecto, Action pushUndoSnapshot)
    {
        _proyecto = proyecto ?? throw new ArgumentNullException(nameof(proyecto));
        _pushUndoSnapshot = pushUndoSnapshot ?? throw new ArgumentNullException(nameof(pushUndoSnapshot));

        AgregarCasoCommand         = new RelayCommand(_ => AgregarCaso());
        EliminarCasoCommand        = new RelayCommand(_ => EliminarCaso(), _ => _casoSeleccionado is not null);
        AgregarCombinacionCommand  = new RelayCommand(p => AgregarCombinacion(p as TipoCombinacion?));
        EliminarCombinacionCommand = new RelayCommand(p => EliminarCombinacion(p as CombinacionCarga),
                                                      p => p is CombinacionCarga);
        AgregarTerminoCommand      = new RelayCommand(_ => AgregarTermino(),  _ => _combinacionSeleccionada is not null);
        EliminarTerminoCommand     = new RelayCommand(_ => EliminarTermino(), _ => _terminoSeleccionado is not null);

        // Vistas filtradas — una por pestaña de la matriz (Servicio / Últimas).
        _vistaServicio = new DataGridCollectionView(Combinaciones)
        {
            Filter = o => o is CombinacionCarga c && c.Tipo == TipoCombinacion.Servicio,
        };
        _vistaUltimas = new DataGridCollectionView(Combinaciones)
        {
            Filter = o => o is CombinacionCarga c && c.Tipo == TipoCombinacion.Ultima,
        };

        // Plomería reactiva del panel de resumen / validación.
        Casos.CollectionChanged += OnCasosColeccionCambiada;
        Combinaciones.CollectionChanged += OnCombinacionesColeccionCambiada;
        RehookCasos();
        RehookCombinaciones();
        RecomputarResumen();
    }

    // ---- Pass-throughs estables al dominio (mismas instancias que el SSOT) ----

    /// <summary>Casos de carga del proyecto.</summary>
    public ObservableCollection<CasoCarga> Casos => _proyecto.Combinaciones.Casos;

    /// <summary>Combinaciones de carga del proyecto (servicio y últimas).</summary>
    public ObservableCollection<CombinacionCarga> Combinaciones => _proyecto.Combinaciones.Combinaciones;

    /// <summary>Edificios del proyecto — alimenta el <c>TreeView</c> del panel derecho.</summary>
    public ObservableCollection<Edificio> Edificios => _proyecto.Edificios;

    /// <summary>Norma de combinaciones activa. Cambiarla toma un snapshot de Undo.</summary>
    public NormaCombinaciones Norma
    {
        get => _proyecto.Combinaciones.Norma;
        set
        {
            if (_proyecto.Combinaciones.Norma == value) return;
            _pushUndoSnapshot();
            _proyecto.Combinaciones.Norma = value;
            OnPropertyChanged();
        }
    }

    // ---- Vistas filtradas — una por pestaña de la matriz ----

    /// <summary>Combinaciones de servicio — pestaña «Servicio» de la matriz.</summary>
    public DataGridCollectionView CombinacionesServicio => _vistaServicio;

    /// <summary>Combinaciones últimas — pestaña «Últimas» de la matriz.</summary>
    public DataGridCollectionView CombinacionesUltimas => _vistaUltimas;

    // ---- Listas de enum para los ComboBox de la vista ----

    public Array TiposCaso        => Enum.GetValues(typeof(TipoCasoCarga));
    public Array TiposCombinacion => Enum.GetValues(typeof(TipoCombinacion));
    public Array Normas           => Enum.GetValues(typeof(NormaCombinaciones));

    // ---- Resumen y validación (panel derecho, reactivo) ----

    /// <summary>Número de casos de carga definidos.</summary>
    public int CantidadCasos => Casos.Count;

    /// <summary>Número total de combinaciones definidas.</summary>
    public int CantidadCombinaciones => Combinaciones.Count;

    /// <summary>Número de combinaciones de servicio.</summary>
    public int CantidadServicio => Combinaciones.Count(c => c.Tipo == TipoCombinacion.Servicio);

    /// <summary>Número de combinaciones últimas.</summary>
    public int CantidadUltimas => Combinaciones.Count(c => c.Tipo == TipoCombinacion.Ultima);

    /// <summary>Alertas normativas detectadas por <see cref="AnalisisCombinaciones"/>.</summary>
    public IReadOnlyList<string> AlertasNormativas => _alertas;

    /// <summary><c>true</c> si hay al menos una alerta normativa.</summary>
    public bool HayAlertas => _alertas.Count > 0;

    /// <summary>Línea de estado resumida del panel de validación.</summary>
    public string EstadoValidacion => _alertas.Count == 0
        ? "✓  Base de cargas conforme"
        : $"⚠  {_alertas.Count} alerta(s) normativa(s)";

    // ---- Selección ----

    private CasoCarga? _casoSeleccionado;
    /// <summary>Caso seleccionado en el DataGrid de casos.</summary>
    public CasoCarga? CasoSeleccionado
    {
        get => _casoSeleccionado;
        set
        {
            _casoSeleccionado = value;
            OnPropertyChanged();
            // Revaluar predicado que lee _casoSeleccionado.
            EliminarCasoCommand.RaiseCanExecuteChanged();
        }
    }

    private CombinacionCarga? _combinacionSeleccionada;
    /// <summary>
    /// Combinación seleccionada. La fija <see cref="AgregarCombinacion"/> al crear
    /// una combinación; alimenta también la API heredada de términos
    /// (<see cref="AgregarTerminoCommand"/>) que la matriz ya no expone en la UI.
    /// </summary>
    public CombinacionCarga? CombinacionSeleccionada
    {
        get => _combinacionSeleccionada;
        set
        {
            _combinacionSeleccionada = value;
            OnPropertyChanged();
            // Revaluar predicado que lee _combinacionSeleccionada.
            AgregarTerminoCommand.RaiseCanExecuteChanged();
            TerminoSeleccionado = null;   // la selección de detalle se reinicia
        }
    }

    private TerminoCombinacion? _terminoSeleccionado;
    /// <summary>Término seleccionado — API heredada de la Iteración 2.</summary>
    public TerminoCombinacion? TerminoSeleccionado
    {
        get => _terminoSeleccionado;
        set
        {
            _terminoSeleccionado = value;
            OnPropertyChanged();
            // Revaluar predicado que lee _terminoSeleccionado.
            EliminarTerminoCommand.RaiseCanExecuteChanged();
        }
    }

    // ---- Comandos ----
    // Los que tienen predicados de selección se tipan como RelayCommand (no ICommand)
    // para poder llamar RaiseCanExecuteChanged() desde los setters — Avalonia carece
    // de CommandManager.RequerySuggested.

    public ICommand AgregarCasoCommand { get; }
    public RelayCommand EliminarCasoCommand { get; }
    public ICommand AgregarCombinacionCommand { get; }
    public ICommand EliminarCombinacionCommand { get; }
    public RelayCommand AgregarTerminoCommand { get; }
    public RelayCommand EliminarTerminoCommand { get; }

    /// <summary>
    /// Lo invoca el code-behind de la vista desde <c>DataGrid.BeginningEdit</c>:
    /// toma un snapshot ANTES de que la edición inline se confirme al modelo,
    /// de modo que cada edición de celda —incluida la matriz de factores— sea
    /// reversible con Ctrl+Z.
    /// </summary>
    public void SnapshotAntesDeEditar() => _pushUndoSnapshot();

    /// <summary>
    /// Lo invoca <c>MainViewModel</c> tras restaurar un snapshot (Undo/Redo):
    /// las colecciones del dominio fueron reemplazadas en bloque, así que se
    /// reinicia la selección, se reenganchan los ítems y se recalcula el resumen.
    /// </summary>
    public void NotificarRestauracion()
    {
        CombinacionSeleccionada = null;
        CasoSeleccionado = null;
        OnPropertyChanged(nameof(Norma));
        RehookCasos();
        RehookCombinaciones();
        RefrescarVistas();
        RecomputarResumen();
    }

    /// <summary>
    /// Parsea el texto de un archivo <c>.DZP</c>/<c>.CEZ</c> y construye el
    /// ViewModel del modal de importación sobre el proyecto activo.
    /// </summary>
    public ImportadorCombinacionesViewModel CrearImportador(
        string textoArchivo, FormatoCombinaciones formato, string nombreArchivo)
    {
        var archivo = ParserDzpCez.Parsear(textoArchivo, formato);
        return new ImportadorCombinacionesViewModel(_proyecto, archivo, nombreArchivo, _pushUndoSnapshot);
    }

    // ---- Mutaciones ----

    private void AgregarCaso()
    {
        _pushUndoSnapshot();
        var caso = new CasoCarga { Codigo = "NUEVO", Nombre = "Caso nuevo", Tipo = TipoCasoCarga.Muerta };
        Casos.Add(caso);
        CasoSeleccionado = caso;
    }

    private void EliminarCaso()
    {
        if (_casoSeleccionado is null) return;
        _pushUndoSnapshot();
        Casos.Remove(_casoSeleccionado);
        CasoSeleccionado = null;
    }

    /// <summary>
    /// Crea una combinación del <paramref name="tipo"/> indicado — la pestaña
    /// activa de la matriz lo pasa por <c>CommandParameter</c>. <c>null</c> →
    /// <see cref="TipoCombinacion.Ultima"/> (retro-compatible con la Iteración 2).
    /// </summary>
    private void AgregarCombinacion(TipoCombinacion? tipo)
    {
        _pushUndoSnapshot();
        var combo = new CombinacionCarga
        {
            Nombre = "Nueva combinación",
            Tipo   = tipo ?? TipoCombinacion.Ultima,
        };
        Combinaciones.Add(combo);
        CombinacionSeleccionada = combo;
    }

    /// <summary>
    /// Elimina la combinación recibida por <c>CommandParameter</c> — la matriz
    /// pasa la fila seleccionada de la pestaña activa.
    /// </summary>
    private void EliminarCombinacion(CombinacionCarga? combo)
    {
        if (combo is null) return;
        _pushUndoSnapshot();
        Combinaciones.Remove(combo);
        if (ReferenceEquals(_combinacionSeleccionada, combo))
            CombinacionSeleccionada = null;
    }

    private void AgregarTermino()
    {
        if (_combinacionSeleccionada is null) return;
        _pushUndoSnapshot();
        var termino = new TerminoCombinacion { Factor = 1.0 };
        _combinacionSeleccionada.Terminos.Add(termino);
        TerminoSeleccionado = termino;
    }

    private void EliminarTermino()
    {
        if (_combinacionSeleccionada is null || _terminoSeleccionado is null) return;
        _pushUndoSnapshot();
        _combinacionSeleccionada.Terminos.Remove(_terminoSeleccionado);
        TerminoSeleccionado = null;
    }

    // ---- Reactividad del resumen / validación ----

    private void OnCasosColeccionCambiada(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RehookCasos();
        RecomputarResumen();
    }

    private void OnCombinacionesColeccionCambiada(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RehookCombinaciones();
        RecomputarResumen();
    }

    private void OnCasoCambiado(object? sender, PropertyChangedEventArgs e) => RecomputarResumen();

    private void OnCombinacionCambiada(object? sender, PropertyChangedEventArgs e)
    {
        // Un cambio de Tipo mueve la combinación de pestaña — refiltrar ambas vistas.
        if (e.PropertyName == nameof(CombinacionCarga.Tipo))
            RefrescarVistas();
        RecomputarResumen();
    }

    private void OnTerminosCambiados(object? sender, NotifyCollectionChangedEventArgs e) => RecomputarResumen();

    /// <summary>Reengancha la suscripción a <c>PropertyChanged</c> de cada caso.</summary>
    private void RehookCasos()
    {
        foreach (var c in _casosEnganchados)
            c.PropertyChanged -= OnCasoCambiado;
        _casosEnganchados.Clear();
        foreach (var c in Casos)
        {
            c.PropertyChanged += OnCasoCambiado;
            _casosEnganchados.Add(c);
        }
    }

    /// <summary>
    /// Reengancha la suscripción a <c>PropertyChanged</c> y a los términos de
    /// cada combinación — la matriz edita factores agregando/quitando términos.
    /// </summary>
    private void RehookCombinaciones()
    {
        foreach (var k in _combosEnganchados)
        {
            k.PropertyChanged -= OnCombinacionCambiada;
            k.Terminos.CollectionChanged -= OnTerminosCambiados;
        }
        _combosEnganchados.Clear();
        foreach (var k in Combinaciones)
        {
            k.PropertyChanged += OnCombinacionCambiada;
            k.Terminos.CollectionChanged += OnTerminosCambiados;
            _combosEnganchados.Add(k);
        }
    }

    private void RefrescarVistas()
    {
        _vistaServicio.Refresh();
        _vistaUltimas.Refresh();
    }

    private void RecomputarResumen()
    {
        _alertas = AnalisisCombinaciones.Validar(_proyecto.Combinaciones);
        OnPropertyChanged(nameof(CantidadCasos));
        OnPropertyChanged(nameof(CantidadCombinaciones));
        OnPropertyChanged(nameof(CantidadServicio));
        OnPropertyChanged(nameof(CantidadUltimas));
        OnPropertyChanged(nameof(AlertasNormativas));
        OnPropertyChanged(nameof(HayAlertas));
        OnPropertyChanged(nameof(EstadoValidacion));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
