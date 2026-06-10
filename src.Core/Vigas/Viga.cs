using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

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
    private double _origenX;        // m — posición en planta del inicio de la viga
    private double _origenY;        // m
    private double _anguloGrados;   // orientación en planta (0 = +X)

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

    /// <summary>
    /// Posición X en planta del inicio de la viga, en metros (Fase J — geometría
    /// en planta para el descenso topológico). La viga corre desde
    /// (<see cref="OrigenX"/>, <see cref="OrigenY"/>) en la dirección
    /// <see cref="AnguloGrados"/> a lo largo de <see cref="LongitudTotal"/>.
    /// Aditivo; default 0.
    /// </summary>
    public double OrigenX
    {
        get => _origenX;
        set { _origenX = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExtremoX)); }
    }

    /// <summary>Posición Y en planta del inicio de la viga, en metros (ver <see cref="OrigenX"/>).</summary>
    public double OrigenY
    {
        get => _origenY;
        set { _origenY = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExtremoY)); }
    }

    /// <summary>Orientación de la viga en planta, en grados (0 = eje +X).</summary>
    public double AnguloGrados
    {
        get => _anguloGrados;
        set { _anguloGrados = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExtremoX)); OnPropertyChanged(nameof(ExtremoY)); }
    }

    /// <summary>Longitud total de la viga = suma de las longitudes de sus tramos, en metros.</summary>
    [JsonIgnore]
    public double LongitudTotal => Tramos.Sum(t => t.Longitud);

    /// <summary>Coordenada X en planta del extremo final de la viga.</summary>
    [JsonIgnore]
    public double ExtremoX => _origenX + LongitudTotal * Math.Cos(_anguloGrados * Math.PI / 180.0);

    /// <summary>Coordenada Y en planta del extremo final de la viga.</summary>
    [JsonIgnore]
    public double ExtremoY => _origenY + LongitudTotal * Math.Sin(_anguloGrados * Math.PI / 180.0);

    /// <summary>
    /// Reescala la posición de los apoyos tras un cambio de longitud (p. ej. al
    /// estirar la viga en el lienzo de planta): cada apoyo conserva su posición
    /// RELATIVA — el que estaba al 50% sigue al 50%, los de los extremos siguen
    /// en los extremos. Llamar DESPUÉS de actualizar la longitud del tramo,
    /// pasando la longitud total previa. Con <paramref name="longitudAnterior"/>
    /// no positiva no hace nada (sin división por cero).
    /// </summary>
    public void ReescalarApoyos(double longitudAnterior)
    {
        if (longitudAnterior <= 0) return;
        double factor = LongitudTotal / longitudAnterior;
        foreach (var apoyo in Apoyos)
            apoyo.CoordenadaX *= factor;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
