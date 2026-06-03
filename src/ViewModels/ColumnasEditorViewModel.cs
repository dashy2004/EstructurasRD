using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LosasPlus.Calculo;
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
        set { _seleccionada = value; OnPropertyChanged(); RecalcularDiseno(); }
    }

    // ---- Diseño a flexo-compresión de la columna seleccionada (ACI 318-19) ----

    private double _fcMPa = 28.0, _fyMPa = 420.0, _recubrimientoMm = 40.0;
    private int _numeroBarra = 8, _barrasX = 3, _barrasY = 3;
    private double _puKN, _muKNm;

    /// <summary>Resistencia del hormigón f'c (MPa) para el diseño de la columna.</summary>
    public double FcMPa { get => _fcMPa; set { _fcMPa = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Resistencia del acero fy (MPa).</summary>
    public double FyMPa { get => _fyMPa; set { _fyMPa = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Recubrimiento al centro de la barra (mm).</summary>
    public double RecubrimientoMm { get => _recubrimientoMm; set { _recubrimientoMm = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Número de barra longitudinal (#3..#11).</summary>
    public int NumeroBarra { get => _numeroBarra; set { _numeroBarra = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Barras por cara horizontal (≥2, incluye esquinas).</summary>
    public int BarrasX { get => _barrasX; set { _barrasX = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Barras por cara vertical (≥2, incluye esquinas).</summary>
    public int BarrasY { get => _barrasY; set { _barrasY = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Carga axial última de demanda Pu (kN).</summary>
    public double PuKN { get => _puKN; set { _puKN = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Momento último de demanda Mu (kN·m).</summary>
    public double MuKNm { get => _muKNm; set { _muKNm = value; OnPropertyChanged(); RecalcularDiseno(); } }

    /// <summary>
    /// Diseño de la columna seleccionada (cuantía, Po, φPn,max, estribo, diagrama,
    /// chequeo), o <c>null</c> si no hay selección o la geometría/armado es inválido.
    /// </summary>
    public ColumnaDisenador.DisenoColumna? DisenoActual { get; private set; }

    private void RecalcularDiseno()
    {
        var col = _seleccionada;
        if (col is null || col.Base <= 0 || col.Peralte <= 0
            || _barrasX < 2 || _barrasY < 2
            || ColumnaDisenador.DiametroBarraMm(_numeroBarra) <= 0)
        {
            DisenoActual = null;
        }
        else
        {
            double b = col.Base * 1000.0;       // m → mm
            double h = col.Peralte * 1000.0;
            var barras = ColumnaDisenador.LayoutPerimetral(b, h, _recubrimientoMm, _barrasX, _barrasY, _numeroBarra);
            var sec = new ColumnaSeccion(b, h, _fcMPa, _fyMPa, barras);
            DisenoActual = ColumnaDisenador.DisenarColumna(sec, _numeroBarra, _puKN * 1000.0, _muKNm * 1e6);
        }
        OnPropertyChanged(nameof(DisenoActual));
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
