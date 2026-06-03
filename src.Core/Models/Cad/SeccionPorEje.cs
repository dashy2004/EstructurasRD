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
}
