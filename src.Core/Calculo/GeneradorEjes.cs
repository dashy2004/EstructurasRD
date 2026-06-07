using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LosasPlus.Models;
using LosasPlus.Models.Cad;

namespace LosasPlus.Calculo;

/// <summary>
/// Genera ejes/rejilla estructural (<see cref="EjeEstructural"/>) a partir de las
/// posiciones en planta de las columnas: agrupa columnas alineadas en líneas de
/// rejilla y crea ejes <b>verticales</b> (X constante, etiquetados A, B, C…) y
/// <b>horizontales</b> (Y constante, etiquetados 1, 2, 3…). Función pura — la usa
/// el comando "Generar ejes" y la rinde <c>PlantaCanvas</c> tal cual.
/// </summary>
public static class GeneradorEjes
{
    /// <summary>Tolerancia (m) para considerar dos columnas en la misma línea de rejilla.</summary>
    public const double ToleranciaDefaultM = 0.25;

    /// <summary>Margen (m) que sobresale cada eje más allá del extremo de las columnas.</summary>
    public const double MargenM = 1.0;

    /// <summary>Genera los ejes de rejilla a partir de un conjunto de columnas.</summary>
    public static List<EjeEstructural> DesdeColumnas(
        IReadOnlyCollection<Columna> columnas, double toleranciaM = ToleranciaDefaultM)
    {
        var ejes = new List<EjeEstructural>();
        if (columnas is null || columnas.Count == 0) return ejes;

        var xs = columnas.Select(c => c.CoordenadaX).ToList();
        var ys = columnas.Select(c => c.CoordenadaY).ToList();
        double minX = xs.Min(), maxX = xs.Max();
        double minY = ys.Min(), maxY = ys.Max();

        // Verticales (X constante) → A, B, C… de izquierda a derecha.
        var lineasX = AgruparColineales(xs, toleranciaM);
        for (int i = 0; i < lineasX.Count; i++)
            ejes.Add(new EjeEstructural
            {
                Etiqueta = LetraEje(i),
                PuntoInicio = new PuntoCad(lineasX[i], minY - MargenM),
                PuntoFin = new PuntoCad(lineasX[i], maxY + MargenM),
            });

        // Horizontales (Y constante) → 1, 2, 3… de abajo hacia arriba.
        var lineasY = AgruparColineales(ys, toleranciaM);
        for (int j = 0; j < lineasY.Count; j++)
            ejes.Add(new EjeEstructural
            {
                Etiqueta = (j + 1).ToString(CultureInfo.InvariantCulture),
                PuntoInicio = new PuntoCad(minX - MargenM, lineasY[j]),
                PuntoFin = new PuntoCad(maxX + MargenM, lineasY[j]),
            });

        return ejes;
    }

    /// <summary>Agrupa coordenadas a ≤ <paramref name="tol"/> en clústeres y devuelve los centros ordenados.</summary>
    private static List<double> AgruparColineales(IEnumerable<double> valores, double tol)
    {
        var grupos = new List<List<double>>();
        foreach (var v in valores.OrderBy(x => x))
        {
            if (grupos.Count > 0 && Math.Abs(v - grupos[^1].Average()) <= tol)
                grupos[^1].Add(v);
            else
                grupos.Add(new List<double> { v });
        }
        return grupos.Select(g => g.Average()).ToList();
    }

    /// <summary>Etiqueta tipo hoja de cálculo: 0→A, 1→B, … 25→Z, 26→AA…</summary>
    private static string LetraEje(int indice)
    {
        var sb = new StringBuilder();
        int i = indice + 1;
        while (i > 0)
        {
            i--;
            sb.Insert(0, (char)('A' + i % 26));
            i /= 26;
        }
        return sb.ToString();
    }
}
