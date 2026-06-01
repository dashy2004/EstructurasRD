using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Models;

/// <summary>
/// Una columna del edificio (Fase J — extensión del modelo para el descenso
/// topológico de cargas losa→viga→<b>columna</b>→zapata). Posicionada en planta
/// por (<see cref="CoordenadaX"/>, <see cref="CoordenadaY"/>), con sección
/// rectangular (<see cref="Base"/> × <see cref="Peralte"/>) y una
/// <see cref="Altura"/> de entrepiso. Lleva opcionalmente su
/// <see cref="Zapata"/> de cimentación.
///
/// <para>
/// Las coordenadas en planta permitirán, en incrementos posteriores, conectar
/// las reacciones de las vigas a la columna y bajar su carga axial a la zapata.
/// Tipo puro de dominio — sin dependencias de WPF/UI ni de SharpDX.
/// </para>
/// </summary>
public partial class Columna : INotifyPropertyChanged
{
    private int _id;
    private string _nombre = "Columna 1";
    private double _coordenadaX;   // m
    private double _coordenadaY;   // m
    private double _base = 0.30;   // m
    private double _peralte = 0.30;// m
    private double _altura = 3.0;  // m
    private Zapata? _zapata;

    /// <summary>Constructor sin parámetros — requerido por la deserialización JSON.</summary>
    public Columna() { }

    /// <summary>Identificador de la columna dentro del nivel.</summary>
    public int Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    /// <summary>Nombre descriptivo (p. ej. «C-1»).</summary>
    public string Nombre
    {
        get => _nombre;
        set { _nombre = value; OnPropertyChanged(); }
    }

    /// <summary>Posición X en planta, en metros.</summary>
    public double CoordenadaX
    {
        get => _coordenadaX;
        set { _coordenadaX = value; OnPropertyChanged(); }
    }

    /// <summary>Posición Y en planta, en metros.</summary>
    public double CoordenadaY
    {
        get => _coordenadaY;
        set { _coordenadaY = value; OnPropertyChanged(); }
    }

    /// <summary>Ancho de la sección rectangular, en metros.</summary>
    public double Base
    {
        get => _base;
        set { _base = value; OnPropertyChanged(); OnPropertyChanged(nameof(AreaSeccion)); }
    }

    /// <summary>Peralte (la otra dimensión) de la sección, en metros.</summary>
    public double Peralte
    {
        get => _peralte;
        set { _peralte = value; OnPropertyChanged(); OnPropertyChanged(nameof(AreaSeccion)); }
    }

    /// <summary>Altura de entrepiso de la columna, en metros.</summary>
    public double Altura
    {
        get => _altura;
        set { _altura = value; OnPropertyChanged(); }
    }

    /// <summary>Zapata de cimentación de la columna (opcional).</summary>
    public Zapata? Zapata
    {
        get => _zapata;
        set { _zapata = value; OnPropertyChanged(); }
    }

    /// <summary>Área de la sección transversal = <see cref="Base"/> · <see cref="Peralte"/>, en m².</summary>
    public double AreaSeccion => _base * _peralte;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
