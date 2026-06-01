using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Models.Cad;

/// <summary>
/// Un muro estructural: un elemento vertical rígido representado en planta
/// como un segmento recto con espesor físico (Epic v1.4.0, Módulo Muros
/// v0.9.0).
///
/// <para>
/// Es un objeto <b>puro de dominio</b> — sus puntos son <see cref="PuntoCad"/>
/// (metros) y no referencia ningún tipo de WPF. La codificación cromática
/// por espesor vive en la capa de presentación, no aquí.
/// </para>
/// </summary>
public sealed class Muro : INotifyPropertyChanged
{
    private int _id;
    private double _espesor = 0.15;   // m
    private double _altura  = 2.80;   // m
    private PuntoCad _puntoInicio;
    private PuntoCad _puntoFin;

    /// <summary>Id correlativo del muro dentro de su <see cref="Sistema"/>.</summary>
    public int Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    /// <summary>Espesor físico del muro, en metros. Define su ancho en planta y su color.</summary>
    public double Espesor
    {
        get => _espesor;
        set { _espesor = value; OnPropertyChanged(); }
    }

    /// <summary>Altura libre del muro, en metros.</summary>
    public double Altura
    {
        get => _altura;
        set { _altura = value; OnPropertyChanged(); }
    }

    /// <summary>Extremo inicial del muro (eje del segmento), en metros de lienzo.</summary>
    public PuntoCad PuntoInicio
    {
        get => _puntoInicio;
        set { _puntoInicio = value; OnPropertyChanged(); OnPropertyChanged(nameof(Longitud)); }
    }

    /// <summary>Extremo final del muro (eje del segmento), en metros de lienzo.</summary>
    public PuntoCad PuntoFin
    {
        get => _puntoFin;
        set { _puntoFin = value; OnPropertyChanged(); OnPropertyChanged(nameof(Longitud)); }
    }

    /// <summary>
    /// Longitud física del muro (m) — distancia euclídea entre los extremos.
    /// Es una propiedad <b>derivada</b>: se calcula de <see cref="PuntoInicio"/>
    /// y <see cref="PuntoFin"/> para que nunca quede inconsistente con la
    /// geometría.
    /// </summary>
    public double Longitud
    {
        get
        {
            double dx = _puntoFin.X - _puntoInicio.X;
            double dy = _puntoFin.Y - _puntoInicio.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
