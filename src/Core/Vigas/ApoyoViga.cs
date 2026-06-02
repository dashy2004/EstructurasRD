using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Vigas;

/// <summary>Tipo de restricción de un <see cref="ApoyoViga"/>.</summary>
public enum TipoApoyo
{
    /// <summary>Apoyo fijo: restringe el desplazamiento vertical; la rotación queda libre.</summary>
    Fijo,

    /// <summary>Empotramiento: restringe el desplazamiento vertical y la rotación.</summary>
    Empotrado,

    /// <summary>Apoyo elástico: resorte vertical de rigidez <see cref="ApoyoViga.RigidezResorte"/>.</summary>
    Elastico,
}

/// <summary>
/// Un apoyo de una <see cref="Viga"/>, posicionado por su
/// <see cref="CoordenadaX"/> a lo largo de la longitud total de la viga. El
/// <see cref="Tipo"/> define qué grados de libertad restringe el motor de
/// resolución en ese nodo.
///
/// <para>Tipo puro de dominio — sin dependencias de WPF.</para>
/// </summary>
public partial class ApoyoViga : INotifyPropertyChanged
{
    private double _coordenadaX;
    private TipoApoyo _tipo = TipoApoyo.Fijo;
    private double _rigidezResorte;

    /// <summary>Constructor sin parámetros — requerido por la deserialización JSON.</summary>
    public ApoyoViga() { }

    /// <summary>Constructor de conveniencia para construir apoyos en código.</summary>
    public ApoyoViga(double coordenadaX, TipoApoyo tipo)
    {
        _coordenadaX = coordenadaX;
        _tipo = tipo;
    }

    /// <summary>
    /// Posición del apoyo, en metros, medida desde el origen de la viga
    /// (el inicio del primer tramo).
    /// </summary>
    public double CoordenadaX
    {
        get => _coordenadaX;
        set { _coordenadaX = value; OnPropertyChanged(); }
    }

    /// <summary>Tipo de restricción — ver <see cref="TipoApoyo"/>.</summary>
    public TipoApoyo Tipo
    {
        get => _tipo;
        set { _tipo = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Rigidez del resorte vertical, en kN/m. Sólo tiene efecto cuando
    /// <see cref="Tipo"/> es <see cref="TipoApoyo.Elastico"/>: el motor la
    /// suma a la rigidez del grado de libertad vertical del nodo.
    /// </summary>
    public double RigidezResorte
    {
        get => _rigidezResorte;
        set { _rigidezResorte = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
