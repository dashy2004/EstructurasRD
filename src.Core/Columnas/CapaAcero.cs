using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Columnas;

/// <summary>
/// Una capa de acero longitudinal de una columna: el conjunto de barras
/// situadas a una misma <see cref="Profundidad"/>, medida desde la cara
/// extrema en compresión, con un área total <see cref="Area"/> (Fase 5).
///
/// <para>
/// La colección <c>RefuerzoColumna.Capas</c> permite representar tanto un
/// refuerzo Top/Bot (2 capas) como un refuerzo perimetral (N capas
/// distribuidas a lo largo del peralte) bajo el mismo modelo.
/// </para>
///
/// <para>Tipo puro de dominio — sin dependencias de WPF.</para>
/// </summary>
public partial class CapaAcero : INotifyPropertyChanged
{
    private double _profundidad = 0.05;   // m — desde la cara extrema en compresión
    private double _area;                  // m²

    /// <summary>
    /// Profundidad de la capa medida desde la cara extrema en compresión, en
    /// metros. Para una sección 0.40×0.40 con recubrimiento de 0.05 m, la capa
    /// superior está en <c>0.05</c> y la inferior en <c>0.35</c>.
    /// </summary>
    public double Profundidad
    {
        get => _profundidad;
        set { _profundidad = value; OnPropertyChanged(); }
    }

    /// <summary>Área total de acero longitudinal de la capa, en m².</summary>
    public double Area
    {
        get => _area;
        set { _area = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
