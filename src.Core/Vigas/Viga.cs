using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Vigas;

/// <summary>
/// Una viga continua: una sucesión de <see cref="TramoViga"/> apoyada sobre
/// uno o más <see cref="ApoyoViga"/>. Es el elemento estructural unidimensional
/// que resuelve <c>VigaContinuaEngine</c> por el Método de Rigidez Directa
/// (Fase 3 de la suite estructural).
///
/// <para>
/// Tipo <b>puro de dominio</b> — no referencia ningún tipo de WPF. Vive en
/// <c>src.Core</c> y cuelga de la jerarquía topológica del edificio:
/// <c>Edificio → Nivel → Viga</c>.
/// </para>
/// </summary>
public partial class Viga : INotifyPropertyChanged
{
    private int _id;
    private string _nombre = "Viga 1";

    /// <summary>Identificador de la viga dentro del nivel.</summary>
    public int Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    /// <summary>Nombre descriptivo de la viga (p. ej. «Viga V-1»).</summary>
    public string Nombre
    {
        get => _nombre;
        set { _nombre = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Tramos de la viga, en orden desde el origen. La longitud total de la
    /// viga es la suma de las <see cref="TramoViga.Longitud"/>. Colección
    /// get-only con inicializador — se rellena vía <c>Populate</c> al
    /// deserializar.
    /// </summary>
    public ObservableCollection<TramoViga> Tramos { get; } = new();

    /// <summary>
    /// Apoyos de la viga, posicionados por <see cref="ApoyoViga.CoordenadaX"/>
    /// a lo largo de la longitud total. Colección get-only con inicializador.
    /// </summary>
    public ObservableCollection<ApoyoViga> Apoyos { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
