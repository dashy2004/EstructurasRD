using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace LosasPlus.Models;

/// <summary>
/// Una zapata aislada bajo una <see cref="Columna"/> (Fase J — extensión del
/// modelo para el descenso topológico de cargas losa→viga→columna→zapata).
/// Rectangular en planta (<see cref="Ancho"/> × <see cref="Largo"/>) con un
/// <see cref="Peralte"/> dado.
///
/// <para>Tipo puro de dominio — sin dependencias de WPF/UI ni de SharpDX.</para>
/// </summary>
public partial class Zapata : INotifyPropertyChanged
{
    private string _nombre = "Zapata 1";
    private double _ancho = 1.0;    // m
    private double _largo = 1.0;    // m
    private double _peralte = 0.40; // m

    /// <summary>Constructor sin parámetros — requerido por la deserialización JSON.</summary>
    public Zapata() { }

    /// <summary>Nombre descriptivo de la zapata.</summary>
    public string Nombre
    {
        get => _nombre;
        set { _nombre = value; OnPropertyChanged(); }
    }

    /// <summary>Ancho de la zapata en planta, en metros.</summary>
    public double Ancho
    {
        get => _ancho;
        set { _ancho = value; OnPropertyChanged(); OnPropertyChanged(nameof(AreaContacto)); }
    }

    /// <summary>Largo de la zapata en planta, en metros.</summary>
    public double Largo
    {
        get => _largo;
        set { _largo = value; OnPropertyChanged(); OnPropertyChanged(nameof(AreaContacto)); }
    }

    /// <summary>Peralte (altura) de la zapata, en metros.</summary>
    public double Peralte
    {
        get => _peralte;
        set { _peralte = value; OnPropertyChanged(); }
    }

    /// <summary>Área de contacto con el terreno = <see cref="Ancho"/> · <see cref="Largo"/>, en m².</summary>
    [JsonIgnore]
    public double AreaContacto => _ancho * _largo;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
