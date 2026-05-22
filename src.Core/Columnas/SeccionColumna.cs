using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Columnas;

/// <summary>
/// Sección transversal rectangular de una columna de concreto reforzado: la
/// geometría y la resistencia del concreto que el motor
/// <c>ColumnaDesignEngine</c> evalúa para el diagrama de interacción P-M
/// uniaxial 2D (Fase 5).
///
/// <para>
/// Unidades: <see cref="Base"/> y <see cref="Peralte"/> en metros;
/// <see cref="Fc"/> en MPa. La sección es rectangular y los recubrimientos
/// quedan implícitos en las profundidades de las capas de
/// <see cref="RefuerzoColumna"/>.
/// </para>
///
/// <para>Tipo puro de dominio — sin dependencias de WPF.</para>
/// </summary>
public partial class SeccionColumna : INotifyPropertyChanged
{
    private double _base = 0.40;       // m
    private double _peralte = 0.40;    // m — dirección de la flexión uniaxial
    private double _fc = 28.0;         // MPa — resistencia del concreto

    /// <summary>Ancho de la sección rectangular, en metros.</summary>
    public double Base
    {
        get => _base;
        set { _base = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Peralte (altura) de la sección rectangular, en metros — dirección a lo
    /// largo de la cual se mide la profundidad de las capas y respecto a cuyo
    /// eje perpendicular se calcula el momento flector del diagrama P-M.
    /// </summary>
    public double Peralte
    {
        get => _peralte;
        set { _peralte = value; OnPropertyChanged(); }
    }

    /// <summary>Resistencia especificada del concreto a compresión f'c, en MPa.</summary>
    public double Fc
    {
        get => _fc;
        set { _fc = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
