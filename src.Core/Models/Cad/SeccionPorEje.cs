using System.Collections.Generic;
using System.Linq;
using LosasPlus.Models;

namespace LosasPlus.Models.Cad;

/// <summary>
/// Selecciona los elementos "cortados" por un <see cref="EjeEstructural"/> — los
/// que caen dentro de su sección (a distancia perpendicular ≤ tolerancia de la
/// recta del eje). Es la base para la vista de sección del 3D y para detectar la
/// topología de vigas continuas a lo largo de un eje (WS1-C). Helper puro.
/// </summary>
public static class SeccionPorEje
{
    /// <summary>
    /// Columnas cuyo centro (<see cref="Columna.CoordenadaX"/>,
    /// <see cref="Columna.CoordenadaY"/>) cae en la sección del <paramref name="eje"/>.
    /// </summary>
    public static IEnumerable<Columna> Columnas(
        EjeEstructural eje, IEnumerable<Columna> columnas, double tolerancia)
    {
        if (eje is null || columnas is null) return Enumerable.Empty<Columna>();
        return columnas.Where(c =>
            eje.EstaEnSeccion(new PuntoCad(c.CoordenadaX, c.CoordenadaY), tolerancia));
    }

    /// <summary>
    /// Losas cuyo <b>centro</b> de paño (<c>X+Lx/2, Y+Ly/2</c>) cae en la sección
    /// del <paramref name="eje"/>. Base de la detección de topología de vigas
    /// continuas a lo largo del eje (WS1-C).
    /// </summary>
    public static IEnumerable<Losa> Losas(
        EjeEstructural eje, IEnumerable<Losa> losas, double tolerancia)
    {
        if (eje is null || losas is null) return Enumerable.Empty<Losa>();
        return losas.Where(l => eje.EstaEnSeccion(
            new PuntoCad(l.CoordenadaX + l.Lx / 2.0, l.CoordenadaY + l.Ly / 2.0), tolerancia));
    }
}
