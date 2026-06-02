using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LosasPlus.Models;

namespace LosasPlus.ViewModels;

/// <summary>
/// ViewModel del editor de columnas (Fase J.10/J.12): agregar, editar y eliminar
/// las <see cref="Columna"/> del <see cref="NivelSeleccionado"/> del edificio
/// activo, que alimentan la vista 3D (I.5/I.6) y el descenso de cargas (J.7).
///
/// <para>
/// Sin dependencias de Avalonia — testeable. Recibe el edificio por un
/// <c>Func</c> y expone los <see cref="Niveles"/> para elegir cuál editar
/// (J.12); por defecto el primero.
/// </para>
/// </summary>
public sealed class ColumnasEditorViewModel : INotifyPropertyChanged
{
    private readonly Func<Edificio?> _getEdificio;

    public ColumnasEditorViewModel(Func<Edificio?> getEdificio)
    {
        _getEdificio = getEdificio ?? throw new ArgumentNullException(nameof(getEdificio));
        Recargar();
    }

    /// <summary>Niveles del edificio activo, para elegir cuál editar.</summary>
    public ObservableCollection<Nivel> Niveles { get; } = new();

    private Nivel? _nivelSeleccionado;
    /// <summary>Nivel cuyas columnas se editan.</summary>
    public Nivel? NivelSeleccionado
    {
        get => _nivelSeleccionado;
        set
        {
            if (ReferenceEquals(_nivelSeleccionado, value)) return;
            _nivelSeleccionado = value;
            Seleccionada = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Columnas));
        }
    }

    /// <summary>Columnas del nivel seleccionado (la colección real — editable).</summary>
    public ObservableCollection<Columna>? Columnas => _nivelSeleccionado?.Columnas;

    private Columna? _seleccionada;
    /// <summary>Columna seleccionada en la tabla (objetivo de «Eliminar»).</summary>
    public Columna? Seleccionada
    {
        get => _seleccionada;
        set { _seleccionada = value; OnPropertyChanged(); }
    }

    /// <summary>Agrega una nueva columna al nivel seleccionado, con Id/Nombre correlativos.</summary>
    public Columna? Agregar()
    {
        var nivel = _nivelSeleccionado;
        if (nivel is null) return null;

        int id = nivel.Columnas.Count > 0 ? nivel.Columnas.Max(c => c.Id) + 1 : 1;
        var columna = new Columna { Id = id, Nombre = $"C-{id}" };
        nivel.Columnas.Add(columna);
        Seleccionada = columna;
        return columna;
    }

    /// <summary>Elimina la <see cref="Seleccionada"/> (o la indicada) del nivel seleccionado.</summary>
    public void Eliminar(Columna? columna = null)
    {
        var nivel = _nivelSeleccionado;
        var objetivo = columna ?? _seleccionada;
        if (nivel is null || objetivo is null) return;
        nivel.Columnas.Remove(objetivo);
        if (ReferenceEquals(objetivo, _seleccionada)) Seleccionada = null;
    }

    /// <summary>Repuebla la lista de niveles desde el edificio activo y selecciona el primero.</summary>
    public void Recargar()
    {
        Niveles.Clear();
        var edificio = _getEdificio();
        if (edificio is not null)
            foreach (var nivel in edificio.Niveles) Niveles.Add(nivel);
        NivelSeleccionado = Niveles.FirstOrDefault();
        OnPropertyChanged(nameof(Columnas));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
