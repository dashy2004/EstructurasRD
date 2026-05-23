using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Zapatas;

/// <summary>
/// Geometría rectangular de una <see cref="ZapataAislada"/>: las dimensiones
/// de la base, el espesor del dado de concreto, la profundidad de desplante
/// y la sección de la columna que la apoya (Fase 6 de la suite estructural).
///
/// <para>
/// Convención de ejes: <see cref="LargoB"/> es la dimensión en la dirección
/// X global de la base; <see cref="AnchoL"/> es la dimensión en la dirección
/// Y global. <see cref="ProfundidadDesplante"/> mide desde la superficie del
/// terreno natural hasta la cara inferior de la zapata; el motor calcula el
/// peso del suelo de relleno sobre la zapata usando <c>Df − H</c>.
/// </para>
///
/// <para>Tipo puro de dominio — sin dependencias de WPF.</para>
/// </summary>
public partial class GeometriaZapata : INotifyPropertyChanged
{
    private double _largoB = 1.50;                // m — dimensión en X
    private double _anchoL = 1.50;                // m — dimensión en Y
    private double _espesorH = 0.30;              // m — peralte de la zapata
    private double _profundidadDesplante = 1.50;  // m — desde NTN a cara inferior
    private double _peralteColumna = 0.30;        // m — dimensión de la columna en Y
    private double _anchoColumna = 0.30;          // m — dimensión de la columna en X

    /// <summary>Largo de la base de la zapata en la dirección X, en metros.</summary>
    public double LargoB
    {
        get => _largoB;
        set { _largoB = value; OnPropertyChanged(); }
    }

    /// <summary>Ancho de la base de la zapata en la dirección Y, en metros.</summary>
    public double AnchoL
    {
        get => _anchoL;
        set { _anchoL = value; OnPropertyChanged(); }
    }

    /// <summary>Espesor (peralte) del dado de concreto de la zapata, en metros.</summary>
    public double EspesorH
    {
        get => _espesorH;
        set { _espesorH = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Profundidad de desplante Df: distancia desde la superficie del terreno
    /// natural hasta la cara inferior de la zapata, en metros. El motor usa
    /// <c>Df − EspesorH</c> como el espesor del suelo de relleno sobre la
    /// zapata para calcular su peso aportado a la carga axial permanente.
    /// </summary>
    public double ProfundidadDesplante
    {
        get => _profundidadDesplante;
        set { _profundidadDesplante = value; OnPropertyChanged(); }
    }

    /// <summary>Peralte (dimensión en Y) de la columna que apoya la zapata, en metros.</summary>
    public double PeralteColumna
    {
        get => _peralteColumna;
        set { _peralteColumna = value; OnPropertyChanged(); }
    }

    /// <summary>Ancho (dimensión en X) de la columna que apoya la zapata, en metros.</summary>
    public double AnchoColumna
    {
        get => _anchoColumna;
        set { _anchoColumna = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
