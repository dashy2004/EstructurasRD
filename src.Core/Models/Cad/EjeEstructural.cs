using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Models.Cad;

/// <summary>
/// Un eje (o rejilla) estructural: una recta de referencia en planta con una
/// etiqueta ("A", "B", "1", "2"…), como las líneas de columnas de un plano
/// estructural (WS2). Sirve para organizar la geometría y para "cortar"
/// secciones del modelo 3D a lo largo de la recta.
///
/// <para>
/// Objeto <b>puro de dominio</b> — sus extremos son <see cref="PuntoCad"/>
/// (metros) y no referencia ningún tipo de UI.
/// </para>
/// </summary>
public sealed class EjeEstructural : INotifyPropertyChanged
{
    private string _etiqueta = "";
    private PuntoCad _puntoInicio;
    private PuntoCad _puntoFin;

    /// <summary>Rótulo del eje (p. ej. "A" o "1").</summary>
    public string Etiqueta
    {
        get => _etiqueta;
        set { _etiqueta = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Extremo inicial de la recta del eje (m).</summary>
    public PuntoCad PuntoInicio
    {
        get => _puntoInicio;
        set { _puntoInicio = value; OnPropertyChanged(); }
    }

    /// <summary>Extremo final de la recta del eje (m).</summary>
    public PuntoCad PuntoFin
    {
        get => _puntoFin;
        set { _puntoFin = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Distancia perpendicular (m) de un punto <paramref name="p"/> a la recta del
    /// eje. Si el eje es degenerado (extremos coincidentes) devuelve la distancia
    /// euclídea al punto. Pura — la usa el selector de secciones.
    /// </summary>
    public double DistanciaA(PuntoCad p)
    {
        double dx = _puntoFin.X - _puntoInicio.X;
        double dy = _puntoFin.Y - _puntoInicio.Y;
        double vx = p.X - _puntoInicio.X;
        double vy = p.Y - _puntoInicio.Y;

        double largo = Math.Sqrt(dx * dx + dy * dy);
        if (largo <= 0)
            return Math.Sqrt(vx * vx + vy * vy);

        double cruz = dx * vy - dy * vx;   // |d × v| en 2D
        return Math.Abs(cruz) / largo;
    }

    /// <summary>
    /// ¿El punto <paramref name="p"/> cae sobre la "sección" del eje, es decir a
    /// una distancia perpendicular ≤ <paramref name="tolerancia"/> (m)? Es el
    /// predicado que selecciona los elementos cortados por el eje para la vista de
    /// sección del 3D.
    /// </summary>
    public bool EstaEnSeccion(PuntoCad p, double tolerancia) => DistanciaA(p) <= tolerancia;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
