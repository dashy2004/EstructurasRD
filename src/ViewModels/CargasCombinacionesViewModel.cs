using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LosasPlus.Cargas;
using LosasPlus.Models;

namespace LosasPlus.ViewModels;

/// <summary>
/// ViewModel de la pestaña «Cargas y Combinaciones» (Fase 2, Iteración 2).
/// Gestiona los casos y combinaciones de carga del proyecto — la base de
/// diseño transversal en <see cref="Proyecto.Combinaciones"/>.
///
/// <para>
/// Toda mutación de las colecciones (alta, baja y edición de filas) se envuelve
/// en un snapshot de Undo para que Ctrl+Z funcione perfectamente. Las altas y
/// bajas pasan por comandos; la edición inline la captura el code-behind de la
/// vista vía <see cref="SnapshotAntesDeEditar"/>.
/// </para>
/// </summary>
public sealed class CargasCombinacionesViewModel : INotifyPropertyChanged
{
    private readonly Proyecto _proyecto;
    private readonly Action _pushUndoSnapshot;

    public CargasCombinacionesViewModel(Proyecto proyecto, Action pushUndoSnapshot)
    {
        _proyecto = proyecto ?? throw new ArgumentNullException(nameof(proyecto));
        _pushUndoSnapshot = pushUndoSnapshot ?? throw new ArgumentNullException(nameof(pushUndoSnapshot));

        AgregarCasoCommand         = new RelayCommand(_ => AgregarCaso());
        EliminarCasoCommand        = new RelayCommand(_ => EliminarCaso(), _ => _casoSeleccionado is not null);
        AgregarCombinacionCommand  = new RelayCommand(_ => AgregarCombinacion());
        EliminarCombinacionCommand = new RelayCommand(_ => EliminarCombinacion(), _ => _combinacionSeleccionada is not null);
        AgregarTerminoCommand      = new RelayCommand(_ => AgregarTermino(), _ => _combinacionSeleccionada is not null);
        EliminarTerminoCommand     = new RelayCommand(_ => EliminarTermino(), _ => _terminoSeleccionado is not null);
    }

    // ---- Pass-throughs estables al dominio (mismas instancias que el SSOT) ----

    /// <summary>Casos de carga del proyecto.</summary>
    public ObservableCollection<CasoCarga> Casos => _proyecto.Combinaciones.Casos;

    /// <summary>Combinaciones de carga del proyecto.</summary>
    public ObservableCollection<CombinacionCarga> Combinaciones => _proyecto.Combinaciones.Combinaciones;

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

    // ---- Listas de enum para los ComboBox de la vista ----

    public Array TiposCaso        => Enum.GetValues(typeof(TipoCasoCarga));
    public Array TiposCombinacion => Enum.GetValues(typeof(TipoCombinacion));
    public Array Normas           => Enum.GetValues(typeof(NormaCombinaciones));

    // ---- Selección master-detail ----

    private CasoCarga? _casoSeleccionado;
    /// <summary>Caso seleccionado en el DataGrid de casos.</summary>
    public CasoCarga? CasoSeleccionado
    {
        get => _casoSeleccionado;
        set { _casoSeleccionado = value; OnPropertyChanged(); }
    }

    private CombinacionCarga? _combinacionSeleccionada;
    /// <summary>Combinación seleccionada (maestro) — alimenta el DataGrid de términos (detalle).</summary>
    public CombinacionCarga? CombinacionSeleccionada
    {
        get => _combinacionSeleccionada;
        set
        {
            _combinacionSeleccionada = value;
            OnPropertyChanged();
            TerminoSeleccionado = null;   // la selección de detalle se reinicia
        }
    }

    private TerminoCombinacion? _terminoSeleccionado;
    /// <summary>Término seleccionado en el DataGrid de detalle.</summary>
    public TerminoCombinacion? TerminoSeleccionado
    {
        get => _terminoSeleccionado;
        set { _terminoSeleccionado = value; OnPropertyChanged(); }
    }

    // ---- Comandos ----

    public ICommand AgregarCasoCommand { get; }
    public ICommand EliminarCasoCommand { get; }
    public ICommand AgregarCombinacionCommand { get; }
    public ICommand EliminarCombinacionCommand { get; }
    public ICommand AgregarTerminoCommand { get; }
    public ICommand EliminarTerminoCommand { get; }

    /// <summary>
    /// Lo invoca el code-behind de la vista desde <c>DataGrid.CellEditEnding</c>:
    /// toma un snapshot ANTES de que la edición inline se confirme al modelo,
    /// de modo que cada edición de celda sea reversible con Ctrl+Z.
    /// </summary>
    public void SnapshotAntesDeEditar() => _pushUndoSnapshot();

    /// <summary>
    /// Lo invoca <c>MainViewModel</c> tras restaurar un snapshot (Undo/Redo):
    /// las colecciones del dominio fueron reemplazadas en bloque, así que se
    /// reinicia la selección y se re-notifican las propiedades escalares.
    /// </summary>
    public void NotificarRestauracion()
    {
        CombinacionSeleccionada = null;
        CasoSeleccionado = null;
        OnPropertyChanged(nameof(Norma));
    }

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

    private void AgregarCombinacion()
    {
        _pushUndoSnapshot();
        var combo = new CombinacionCarga { Nombre = "Nueva combinación", Tipo = TipoCombinacion.Ultima };
        Combinaciones.Add(combo);
        CombinacionSeleccionada = combo;
    }

    private void EliminarCombinacion()
    {
        if (_combinacionSeleccionada is null) return;
        _pushUndoSnapshot();
        Combinaciones.Remove(_combinacionSeleccionada);
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
