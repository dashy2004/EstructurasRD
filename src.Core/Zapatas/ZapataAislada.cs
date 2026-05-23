using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Zapatas;

/// <summary>
/// Una zapata aislada de concreto reforzado bajo una columna de un nivel.
/// Es la entidad raíz del módulo de cimentaciones (Fase 6 de la suite
/// estructural): la dirección estructural del análisis es la presión de
/// contacto que la zapata ejerce sobre el suelo bajo cada combinación de
/// servicio.
///
/// <para>
/// Cuelga de la jerarquía topológica <c>Edificio → Nivel → ZapataAislada</c>.
/// El motor <c>ZapataDesignEngine</c> consume <see cref="Dimensiones"/>,
/// <see cref="Suelo"/> y <see cref="Cargas"/> para producir el perfil de
/// presiones en las cuatro esquinas de la base.
/// </para>
///
/// <para>Tipo puro de dominio — sin dependencias de WPF.</para>
/// </summary>
public partial class ZapataAislada : INotifyPropertyChanged
{
    private int _id;
    private string _nombre = "Zapata 1";
    private GeometriaZapata _dimensiones = new();
    private PropiedadesTerreno _suelo = new();

    /// <summary>Identificador de la zapata dentro del nivel.</summary>
    public int Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    /// <summary>Nombre descriptivo de la zapata (p. ej. «Z-1»).</summary>
    public string Nombre
    {
        get => _nombre;
        set { _nombre = value; OnPropertyChanged(); }
    }

    /// <summary>Geometría rectangular de la zapata + el dado de la columna que la apoya.</summary>
    public GeometriaZapata Dimensiones
    {
        get => _dimensiones;
        set { _dimensiones = value; OnPropertyChanged(); }
    }

    /// <summary>Propiedades del terreno de fundación bajo la zapata.</summary>
    public PropiedadesTerreno Suelo
    {
        get => _suelo;
        set { _suelo = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Cargas actuantes (P, Mx, My) por caso de carga del proyecto. El motor
    /// las mayora por el factor que cada combinación de servicio asigna al
    /// <see cref="CargaZapata.CodigoCaso"/>. Colección get-only con
    /// inicializador — <c>Populate</c> la rellena en deserialización.
    /// </summary>
    public ObservableCollection<CargaZapata> Cargas { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
