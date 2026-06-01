using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LosasPlus.Models;

namespace LosasPlus.ViewModels;

/// <summary>
/// ViewModel del editor de columnas (Fase J.10): permite agregar, editar y
/// eliminar las <see cref="Columna"/> del primer nivel del edificio activo, que
/// alimentan la vista 3D (I.5/I.6) y el descenso de cargas (J.7).
///
/// <para>
/// Sin dependencias de Avalonia — testeable. Recibe el edificio por un
/// <c>Func</c> para reflejar siempre el activo. La selección multi-nivel queda
/// como mejora futura (hoy edita el primer nivel).
/// </para>
/// </summary>
public sealed class ColumnasEditorViewModel : INotifyPropertyChanged
{
    private readonly System.Func<Edificio?> _getEdificio;

    public ColumnasEditorViewModel(System.Func<Edificio?> getEdificio)
        => _getEdificio = getEdificio ?? throw new System.ArgumentNullException(nameof(getEdificio));

    private Nivel? PrimerNivel => _getEdificio()?.Niveles.FirstOrDefault();

    /// <summary>Columnas del primer nivel del edificio activo (la colección real — editable).</summary>
    public ObservableCollection<Columna>? Columnas => PrimerNivel?.Columnas;

    private Columna? _seleccionada;
    /// <summary>Columna seleccionada en la tabla (objetivo de «Eliminar»).</summary>
    public Columna? Seleccionada
    {
        get => _seleccionada;
        set { _seleccionada = value; OnPropertyChanged(); }
    }

    /// <summary>Agrega una nueva columna al primer nivel, con Id/Nombre correlativos.</summary>
    public Columna? Agregar()
    {
        var nivel = PrimerNivel;
        if (nivel is null) return null;

        int id = nivel.Columnas.Count > 0 ? nivel.Columnas.Max(c => c.Id) + 1 : 1;
        var columna = new Columna { Id = id, Nombre = $"C-{id}" };
        nivel.Columnas.Add(columna);
        Seleccionada = columna;
        return columna;
    }

    /// <summary>Elimina la <see cref="Seleccionada"/> (o la indicada) del primer nivel.</summary>
    public void Eliminar(Columna? columna = null)
    {
        var nivel = PrimerNivel;
        var objetivo = columna ?? _seleccionada;
        if (nivel is null || objetivo is null) return;
        nivel.Columnas.Remove(objetivo);
        if (ReferenceEquals(objetivo, _seleccionada)) Seleccionada = null;
    }

    /// <summary>Notifica que la colección pudo cambiar (al cambiar de proyecto/edificio).</summary>
    public void Recargar() => OnPropertyChanged(nameof(Columnas));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
