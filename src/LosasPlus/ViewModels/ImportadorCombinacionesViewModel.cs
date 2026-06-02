using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LosasPlus.Cargas;
using LosasPlus.Models;

namespace LosasPlus.ViewModels;

/// <summary>Estrategia de fusión al importar un archivo de combinaciones.</summary>
public enum EstrategiaImportacion
{
    /// <summary>Descarta lo actual y deja exactamente lo del archivo.</summary>
    ReemplazarTodo,

    /// <summary>Casos: actualiza los que coinciden por código y agrega los nuevos.
    /// Combinaciones: agrega las que no existen ya.</summary>
    MergeInteligente,

    /// <summary>Agrega únicamente los casos y combinaciones que aún no existen.</summary>
    SoloAgregarNuevas,

    /// <summary>Importa sólo los casos (actualiza/agrega); ignora las combinaciones.</summary>
    SoloImportarCasos,
}

/// <summary>
/// ViewModel del modal «Importar combinaciones desde DISEST» (Fase 2,
/// Iteración 3). Confronta el archivo <c>.DZP</c>/<c>.CEZ</c> parseado contra
/// la base de cargas del proyecto y aplica la estrategia de fusión elegida.
///
/// <para>La aplicación toma un snapshot de Undo antes de mutar el SSOT.</para>
/// </summary>
public sealed class ImportadorCombinacionesViewModel : INotifyPropertyChanged
{
    private readonly Proyecto _proyecto;
    private readonly Action _pushUndoSnapshot;

    public ImportadorCombinacionesViewModel(
        Proyecto proyecto, CombinacionesProyecto archivo, string nombreArchivo, Action pushUndoSnapshot)
    {
        _proyecto = proyecto ?? throw new ArgumentNullException(nameof(proyecto));
        Archivo = archivo ?? throw new ArgumentNullException(nameof(archivo));
        NombreArchivo = nombreArchivo ?? "";
        _pushUndoSnapshot = pushUndoSnapshot ?? throw new ArgumentNullException(nameof(pushUndoSnapshot));

        AplicarCommand  = new RelayCommand(_ => Aplicar());
        CancelarCommand = new RelayCommand(_ => Cerrar?.Invoke());

        ResumenDiff = CalcularResumenDiff();
    }

    /// <summary>Combinaciones parseadas del archivo importado.</summary>
    public CombinacionesProyecto Archivo { get; }

    /// <summary>Nombre del archivo importado (para la cabecera del modal).</summary>
    public string NombreArchivo { get; }

    // ---- Lo del proyecto vs. lo del archivo (los 3 paneles del modal) ----

    public ObservableCollection<CasoCarga> CasosActuales => _proyecto.Combinaciones.Casos;
    public ObservableCollection<CombinacionCarga> CombinacionesActuales => _proyecto.Combinaciones.Combinaciones;
    public ObservableCollection<CasoCarga> CasosArchivo => Archivo.Casos;
    public ObservableCollection<CombinacionCarga> CombinacionesArchivo => Archivo.Combinaciones;

    private EstrategiaImportacion _estrategia = EstrategiaImportacion.ReemplazarTodo;
    /// <summary>Estrategia de fusión seleccionada en el modal.</summary>
    public EstrategiaImportacion Estrategia
    {
        get => _estrategia;
        set { _estrategia = value; OnPropertyChanged(); }
    }

    /// <summary>Resumen del diff archivo↔proyecto — la barra inferior del modal.</summary>
    public string ResumenDiff { get; }

    public ICommand AplicarCommand { get; }
    public ICommand CancelarCommand { get; }

    /// <summary>Callback que cierra el modal — lo inyecta el code-behind de la ventana.</summary>
    public Action? Cerrar { get; set; }

    /// <summary><c>true</c> si el usuario aplicó la importación (vs. cancelar).</summary>
    public bool Aplicado { get; private set; }

    private string CalcularResumenDiff()
    {
        var actual = _proyecto.Combinaciones;
        int casosNuevos = Archivo.Casos.Count(a => actual.Casos.All(c => c.Codigo != a.Codigo));
        int casosDistintos = Archivo.Casos.Count(a =>
        {
            var c = actual.Casos.FirstOrDefault(x => x.Codigo == a.Codigo);
            return c is not null && (c.Nombre != a.Nombre || c.Tipo != a.Tipo
                                     || c.Fce != a.Fce || c.Fsis != a.Fsis);
        });
        int combosNuevas = Archivo.Combinaciones.Count(a => actual.Combinaciones.All(c => c.Nombre != a.Nombre));

        return $"Archivo: NCC={Archivo.Casos.Count} · NCOMB={Archivo.Combinaciones.Count}.  "
             + $"Diff: {casosNuevos} casos nuevos, {casosDistintos} distintos, "
             + $"{combosNuevas} combinaciones nuevas.";
    }

    private void Aplicar()
    {
        _pushUndoSnapshot();
        var destino = _proyecto.Combinaciones;

        switch (_estrategia)
        {
            case EstrategiaImportacion.ReemplazarTodo:
                destino.Casos.Clear();
                foreach (var c in Archivo.Casos) destino.Casos.Add(c);
                destino.Combinaciones.Clear();
                foreach (var k in Archivo.Combinaciones) destino.Combinaciones.Add(k);
                destino.Norma = Archivo.Norma;
                break;

            case EstrategiaImportacion.SoloImportarCasos:
                FusionarCasos(destino);
                break;

            case EstrategiaImportacion.SoloAgregarNuevas:
                foreach (var c in Archivo.Casos)
                    if (destino.Casos.All(x => x.Codigo != c.Codigo)) destino.Casos.Add(c);
                AgregarCombinacionesNuevas(destino);
                break;

            case EstrategiaImportacion.MergeInteligente:
                FusionarCasos(destino);
                AgregarCombinacionesNuevas(destino);
                break;
        }

        Aplicado = true;
        Cerrar?.Invoke();
    }

    /// <summary>Actualiza los casos que coinciden por código y agrega los nuevos.</summary>
    private void FusionarCasos(CombinacionesProyecto destino)
    {
        foreach (var c in Archivo.Casos)
        {
            var existente = destino.Casos.FirstOrDefault(x => x.Codigo == c.Codigo);
            if (existente is null)
            {
                destino.Casos.Add(c);
            }
            else
            {
                existente.Nombre = c.Nombre;
                existente.Tipo   = c.Tipo;
                existente.Fce    = c.Fce;
                existente.Fsis   = c.Fsis;
            }
        }
    }

    /// <summary>Agrega las combinaciones del archivo cuyo nombre no exista ya.</summary>
    private void AgregarCombinacionesNuevas(CombinacionesProyecto destino)
    {
        foreach (var k in Archivo.Combinaciones)
            if (destino.Combinaciones.All(x => x.Nombre != k.Nombre))
                destino.Combinaciones.Add(k);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
