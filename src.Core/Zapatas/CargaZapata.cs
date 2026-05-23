using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Zapatas;

/// <summary>
/// Una carga actuante en la cabeza de una <see cref="ZapataAislada"/>
/// transmitida por la columna que la apoya (Fase 6 de la suite estructural).
/// La carga se introduce por caso de carga del proyecto; el motor la mayora
/// por el factor que cada combinación de servicio asigna al
/// <see cref="CodigoCaso"/>, idéntico al patrón de
/// <c>CargaElemento</c> en vigas.
///
/// <para>
/// Convención de signos: <see cref="P"/> positivo = compresión sobre la
/// zapata; <see cref="Mx"/> positivo es el momento alrededor del eje X
/// global que comprime el lado <c>+y</c> de la base; <see cref="My"/>
/// positivo es el momento alrededor del eje Y global que comprime el lado
/// <c>+x</c>.
/// </para>
///
/// <para>Tipo puro de dominio — sin dependencias de WPF.</para>
/// </summary>
public partial class CargaZapata : INotifyPropertyChanged
{
    private string _codigoCaso = "";
    private double _p;   // kN
    private double _mx;  // kN·m
    private double _my;  // kN·m

    /// <summary>
    /// Código del caso de carga (<c>CasoCarga.Codigo</c>) al que pertenece la
    /// carga — debe coincidir con un caso de la base de cargas del proyecto.
    /// Determina con qué factor la mayora cada combinación de servicio.
    /// </summary>
    public string CodigoCaso
    {
        get => _codigoCaso;
        set { _codigoCaso = value; OnPropertyChanged(); }
    }

    /// <summary>Carga axial actuante P, en kN. Positiva = compresión sobre la zapata.</summary>
    public double P
    {
        get => _p;
        set { _p = value; OnPropertyChanged(); }
    }

    /// <summary>Momento flector alrededor del eje X global, en kN·m.</summary>
    public double Mx
    {
        get => _mx;
        set { _mx = value; OnPropertyChanged(); }
    }

    /// <summary>Momento flector alrededor del eje Y global, en kN·m.</summary>
    public double My
    {
        get => _my;
        set { _my = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
