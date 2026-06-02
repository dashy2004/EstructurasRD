using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LosasPlus.Models;
using LosasPlus.Transmision;

namespace LosasPlus.ViewModels;

/// <summary>
/// ViewModel de la vista «Bajada de cargas» (Fase J.4): expone el resultado de
/// la transmisión vertical (<see cref="BajadaCargas"/>) del edificio activo y el
/// predimensionado de zapata (<see cref="PredimZapata"/>) para una presión
/// admisible editable.
///
/// <para>
/// Sin dependencias de Avalonia — testeable. Recibe el edificio por un
/// <c>Func</c> para reflejar siempre el activo del proyecto al
/// <see cref="Recalcular"/>.
/// </para>
/// </summary>
public sealed class BajadaCargasViewModel : INotifyPropertyChanged
{
    private readonly Func<Edificio?> _getEdificio;

    public BajadaCargasViewModel(Func<Edificio?> getEdificio)
    {
        _getEdificio = getEdificio ?? throw new ArgumentNullException(nameof(getEdificio));
        Recalcular();
    }

    /// <summary>Niveles con su carga propia y acumulada, de arriba hacia la base.</summary>
    public ObservableCollection<CargaNivel> Filas { get; } = new();

    private double _cargaEnBase;
    /// <summary>Carga total que llega a la base (última, ton — igual que Losa.Carga = Wu).</summary>
    public double CargaEnBase
    {
        get => _cargaEnBase;
        private set { _cargaEnBase = value; OnPropertyChanged(); }
    }

    private double _presionAdmisible = 15.0; // t/m²
    /// <summary>Capacidad portante admisible del terreno, en t/m² (editable).</summary>
    public double PresionAdmisible
    {
        get => _presionAdmisible;
        set
        {
            if (value == _presionAdmisible) return;
            _presionAdmisible = value;
            OnPropertyChanged();
            RecalcularZapata();
        }
    }

    private double _areaZapata;
    /// <summary>Área requerida de la zapata, en m² (predimensionado preliminar).</summary>
    public double AreaZapata
    {
        get => _areaZapata;
        private set { _areaZapata = value; OnPropertyChanged(); }
    }

    private double _ladoZapata;
    /// <summary>Lado de una zapata cuadrada equivalente, en m.</summary>
    public double LadoZapata
    {
        get => _ladoZapata;
        private set { _ladoZapata = value; OnPropertyChanged(); }
    }

    /// <summary>Recarga la bajada de cargas desde el edificio activo y recalcula la zapata.</summary>
    public void Recalcular()
    {
        var bajada = BajadaCargas.Acumular(_getEdificio());
        Filas.Clear();
        foreach (var fila in bajada.Niveles) Filas.Add(fila);
        CargaEnBase = bajada.CargaEnBase;
        RecalcularZapata();
    }

    private void RecalcularZapata()
    {
        var z = PredimZapata.Cuadrada(CargaEnBase, PresionAdmisible);
        AreaZapata = z.AreaRequerida;
        LadoZapata = z.LadoCuadrada;
    }

    /// <summary>
    /// Exporta el reporte de bajada de cargas del edificio activo a un libro
    /// Excel en <paramref name="path"/>. No hace nada si no hay edificio activo.
    /// </summary>
    public void ExportarXlsx(string path)
    {
        var edificio = _getEdificio();
        if (edificio is null) return;
        BajadaCargasExporter.Export(edificio, PresionAdmisible, path);
    }

    private string _resumenZapatas = "";
    /// <summary>Resumen del último predimensionado de zapatas de columnas.</summary>
    public string ResumenZapatas
    {
        get => _resumenZapatas;
        private set { _resumenZapatas = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Reparte la carga en base entre todas las columnas del edificio (reparto
    /// equitativo, <see cref="DescensoColumnas"/>) y predimensiona la zapata de
    /// cada una para la <see cref="PresionAdmisible"/> actual. Actualiza
    /// <see cref="ResumenZapatas"/>.
    /// </summary>
    public void PredimensionarZapatas()
    {
        var edificio = _getEdificio();
        if (edificio is null) { ResumenZapatas = "Sin edificio activo."; return; }

        var columnas = edificio.Niveles.SelectMany(n => n.Columnas).ToList();
        if (columnas.Count == 0) { ResumenZapatas = "No hay columnas definidas (agrégalas en el editor de Columnas)."; return; }

        var res = DescensoColumnas.RepartirEquitativo(columnas, CargaEnBase, PresionAdmisible);
        if (res.Count == 0) { ResumenZapatas = "Sin carga que repartir."; return; }

        double axial = res[0].CargaAxial, lado = res[0].LadoZapata;
        ResumenZapatas = $"{res.Count} columna(s): {axial:0.##} t c/u (reparto equitativo, Wu) → zapata {lado:0.##}×{lado:0.##} m";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
