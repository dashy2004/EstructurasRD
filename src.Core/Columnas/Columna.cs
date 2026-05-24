using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Columnas;

/// <summary>
/// Una columna de concreto reforzado, descrita por su altura libre, su sección
/// transversal y la disposición de acero longitudinal. Es la entidad raíz del
/// módulo de columnas (Fase 5 de la suite estructural).
///
/// <para>
/// Cuelga de la jerarquía topológica <c>Edificio → Nivel → Columna</c>. El
/// motor <c>ColumnaDesignEngine</c> consume <see cref="Geometria"/> y
/// <see cref="Acero"/> para construir el diagrama de interacción P-M uniaxial.
/// </para>
///
/// <para>Tipo puro de dominio — sin dependencias de WPF.</para>
/// </summary>
public partial class Columna : INotifyPropertyChanged
{
    private int _id;
    private string _nombre = "Columna 1";
    private double _altura = 3.0;             // m — altura libre por defecto
    private SeccionColumna _geometria = new();
    private RefuerzoColumna _acero = new();
    private double? _posX;                    // m — Módulo 2C Fase 3D-II (opcional)
    private double? _posY;                    // m — idem

    /// <summary>Identificador de la columna dentro del nivel.</summary>
    public int Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    /// <summary>Nombre descriptivo de la columna (p. ej. «C-1»).</summary>
    public string Nombre
    {
        get => _nombre;
        set { _nombre = value; OnPropertyChanged(); }
    }

    /// <summary>Altura libre de la columna entre niveles, en metros.</summary>
    public double Altura
    {
        get => _altura;
        set { _altura = value; OnPropertyChanged(); }
    }

    /// <summary>Sección transversal rectangular de la columna.</summary>
    public SeccionColumna Geometria
    {
        get => _geometria;
        set { _geometria = value; OnPropertyChanged(); }
    }

    /// <summary>Acero de refuerzo longitudinal — capas a profundidades conocidas.</summary>
    public RefuerzoColumna Acero
    {
        get => _acero;
        set { _acero = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Coordenada X de la columna en el lienzo (m), opcional —
    /// Módulo 2C Fase 3D-II del Plan Maestro. Si está poblada, el
    /// <c>GrafoProyectadoBuilder</c> la usa para posicionar la columna
    /// en el visor 3D; si es <c>null</c>, fallback a la grilla
    /// artificial por Id (comportamiento legacy preservado).
    /// </summary>
    public double? PosX
    {
        get => _posX;
        set { _posX = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Coordenada Y de la columna en el lienzo (m), opcional. Idem
    /// semántica que <see cref="PosX"/> — ambos deben estar no-null
    /// para que el builder respete la posición real.
    /// </summary>
    public double? PosY
    {
        get => _posY;
        set { _posY = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Demandas actuantes (Pu, Mu) — pares de carga axial y momento últimos
    /// que se verifican contra el diagrama de interacción P-M de diseño
    /// (Fase 5, Iteración 3). Colección get-only con inicializador: se
    /// (de)serializa vía <c>Populate</c> de forma aditiva y sobrevive los
    /// proyectos creados antes de Iter 3 con la colección vacía.
    /// </summary>
    public ObservableCollection<DemandaColumna> Demandas { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
