using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace LosasPlus.Columnas;

/// <summary>
/// El acero de refuerzo longitudinal de una columna, descrito como una
/// colección de capas (<see cref="CapaAcero"/>) y la resistencia común
/// <see cref="Fy"/>. Modela un refuerzo Top/Bot con 2 capas o un refuerzo
/// perimetral con N capas distribuidas (Fase 5).
///
/// <para>Tipo puro de dominio — sin dependencias de WPF.</para>
/// </summary>
public partial class RefuerzoColumna : INotifyPropertyChanged
{
    private double _fy = 420.0;   // MPa — fluencia del acero (Grado 60)

    /// <summary>Esfuerzo de fluencia del acero de refuerzo fy, en MPa.</summary>
    public double Fy
    {
        get => _fy;
        set { _fy = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Capas de acero longitudinal, cada una a su profundidad y con su área.
    /// Colección get-only con inicializador — <c>ProyectoSerializer</c> la
    /// rellena vía <c>Populate</c> al deserializar.
    /// </summary>
    public ObservableCollection<CapaAcero> Capas { get; } = new();

    /// <summary>Suma de las áreas de acero de todas las capas, en m².</summary>
    [JsonIgnore]
    public double AreaAceroTotal => Capas.Sum(c => c.Area);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
