using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Rc;

/// <summary>
/// El acero de refuerzo longitudinal de una sección de concreto reforzado,
/// descrito por el área total de acero en cada cara (Fase 4).
///
/// <para>
/// Unidades: <see cref="AreaAceroTop"/> y <see cref="AreaAceroBot"/> en m².
/// El acero superior trabaja a tracción bajo momento negativo; el inferior,
/// bajo momento positivo.
/// </para>
///
/// <para>Tipo <b>puro de dominio</b> — sin dependencias de WPF.</para>
/// </summary>
public partial class RefuerzoLongitudinal : INotifyPropertyChanged
{
    private double _areaAceroTop;   // m²
    private double _areaAceroBot;   // m²

    /// <summary>Área total de acero longitudinal en la cara superior, en m².</summary>
    public double AreaAceroTop
    {
        get => _areaAceroTop;
        set { _areaAceroTop = value; OnPropertyChanged(); }
    }

    /// <summary>Área total de acero longitudinal en la cara inferior, en m².</summary>
    public double AreaAceroBot
    {
        get => _areaAceroBot;
        set { _areaAceroBot = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
