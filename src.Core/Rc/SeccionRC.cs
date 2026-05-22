using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Rc;

/// <summary>
/// Una sección rectangular de concreto reforzado: la geometría y los materiales
/// que el motor <c>RcDesignEngine</c> evalúa a flexión por el bloque
/// equivalente de Whitney (Fase 4 de la suite estructural).
///
/// <para>
/// Unidades: <see cref="Base"/>, <see cref="Peralte"/> y
/// <see cref="Recubrimiento"/> en metros; <see cref="Fc"/> y <see cref="Fy"/>
/// en MPa.
/// </para>
///
/// <para>Tipo <b>puro de dominio</b> — sin dependencias de WPF.</para>
/// </summary>
public partial class SeccionRC : INotifyPropertyChanged
{
    private double _base = 0.30;            // m
    private double _peralte = 0.60;         // m
    private double _recubrimiento = 0.04;   // m
    private double _fc = 28.0;              // MPa — resistencia del concreto
    private double _fy = 420.0;             // MPa — fluencia del acero (Grado 60)

    /// <summary>Ancho de la sección rectangular, en metros.</summary>
    public double Base
    {
        get => _base;
        set { _base = value; OnPropertyChanged(); }
    }

    /// <summary>Peralte (altura total) de la sección, en metros.</summary>
    public double Peralte
    {
        get => _peralte;
        set { _peralte = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Recubrimiento, en metros — distancia de la fibra extrema en tracción al
    /// centroide del acero longitudinal de tracción. El peralte efectivo de
    /// diseño es <c>Peralte − Recubrimiento</c>.
    /// </summary>
    public double Recubrimiento
    {
        get => _recubrimiento;
        set { _recubrimiento = value; OnPropertyChanged(); }
    }

    /// <summary>Resistencia especificada del concreto a compresión f'c, en MPa.</summary>
    public double Fc
    {
        get => _fc;
        set { _fc = value; OnPropertyChanged(); }
    }

    /// <summary>Esfuerzo de fluencia del acero de refuerzo fy, en MPa.</summary>
    public double Fy
    {
        get => _fy;
        set { _fy = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
