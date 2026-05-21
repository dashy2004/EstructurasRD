using System;
using System.Collections.Generic;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Models.Cad;

namespace LosasPlus.Services;

/// <summary>
/// Una entrada del resumen de muros: la longitud total de muro de un
/// <see cref="Espesor"/> dado asociada a una losa concreta (o al bucket
/// «sin asignar» cuando <see cref="LosaId"/> es <c>null</c>).
/// </summary>
/// <param name="LosaId">Id de la losa, o <c>null</c> si el muro no se asoció a ninguna.</param>
/// <param name="Espesor">Espesor del muro en metros (redondeado a mm).</param>
/// <param name="LongitudTotal">Suma de longitudes de los muros de ese espesor en esa losa (m).</param>
/// <param name="Cantidad">Cantidad de muros agrupados en esta entrada.</param>
public sealed record EntradaResumenMuro(int? LosaId, double Espesor, double LongitudTotal, int Cantidad)
{
    /// <summary>
    /// Etiqueta legible de la losa para la leyenda «Suma de Colores»:
    /// <c>«Losa N»</c>, o <c>«Sin asignar»</c> cuando el muro no se asoció.
    /// </summary>
    public string EtiquetaLosa => LosaId is { } id ? $"Losa {id}" : "Sin asignar";
}

/// <summary>
/// Resumen «Suma de Colores»: el desglose de longitudes de muro por losa y
/// por espesor (Epic v1.4.0, Módulo Muros v0.9.0).
/// </summary>
/// <param name="Entradas">Una entrada por cada combinación (losa, espesor) presente.</param>
public sealed record ResumenMuros(IReadOnlyList<EntradaResumenMuro> Entradas);

/// <summary>
/// Helper de geometría analítica del Módulo de Muros (Epic v1.4.0). Asocia
/// cada <see cref="Muro"/> a la(s) losa(s) que toca y totaliza las
/// longitudes por losa y por espesor para la leyenda «Suma de Colores».
///
/// <para>
/// Lógica <b>puramente matemática</b>, sin dependencias de WPF — testeable
/// de forma exhaustiva. Sólo considera losas <b>ancladas</b>
/// (<see cref="Losa.TienePosicionExplicita"/>): una losa flotante no tiene
/// una posición autoral sobre el lienzo contra la cual asociar muros.
/// </para>
/// </summary>
public static class AnalisisMuros
{
    /// <summary>
    /// Tolerancia por defecto, en metros, para considerar que el punto medio
    /// de un muro «cae dentro» de una losa. Cubre que los muros perimetrales
    /// tienen su eje sobre el borde de la losa, más la imprecisión de dibujo.
    /// </summary>
    public const double ToleranciaAsociacionMetros = 0.15;

    /// <summary>
    /// Asocia los muros del sistema a sus losas y devuelve el desglose de
    /// longitudes por losa y por espesor. Un muro sobre una arista
    /// compartida cuenta para ambas losas; uno que no toca ninguna losa va
    /// al bucket «sin asignar» (<c>LosaId == null</c>).
    /// </summary>
    public static ResumenMuros Resumir(Sistema? sistema, double toleranciaMetros = ToleranciaAsociacionMetros)
    {
        if (sistema is null || sistema.Muros.Count == 0)
            return new ResumenMuros(Array.Empty<EntradaResumenMuro>());

        // Rectángulos de las losas ancladas (esquina sup-izq + tamaño).
        var rects = sistema.Losas
            .Where(l => l.TienePosicionExplicita)
            .Select(l => (Id: l.Id, X: l.PosX!.Value, Y: l.PosY!.Value, l.Lx, l.Ly))
            .ToList();

        // Acumulador: (LosaId, Espesor) → (longitud total, cantidad).
        var acc = new Dictionary<(int? LosaId, double Espesor), (double Longitud, int Cantidad)>();

        foreach (var muro in sistema.Muros)
        {
            double midX = (muro.PuntoInicio.X + muro.PuntoFin.X) / 2.0;
            double midY = (muro.PuntoInicio.Y + muro.PuntoFin.Y) / 2.0;
            double espesor = Math.Round(muro.Espesor, 3);
            double longitud = muro.Longitud;

            // Losas cuyo rectángulo expandido por la tolerancia contiene el
            // punto medio del muro.
            var asociadas = rects
                .Where(r => midX >= r.X - toleranciaMetros && midX <= r.X + r.Lx + toleranciaMetros
                         && midY >= r.Y - toleranciaMetros && midY <= r.Y + r.Ly + toleranciaMetros)
                .Select(r => (int?)r.Id)
                .ToList();

            if (asociadas.Count == 0)
                asociadas.Add(null);   // bucket «sin asignar»

            foreach (var losaId in asociadas)
            {
                var key = (losaId, espesor);
                acc.TryGetValue(key, out var actual);   // actual = (0, 0) si no existe
                acc[key] = (actual.Longitud + longitud, actual.Cantidad + 1);
            }
        }

        var entradas = acc
            .Select(kv => new EntradaResumenMuro(
                kv.Key.LosaId, kv.Key.Espesor, kv.Value.Longitud, kv.Value.Cantidad))
            .OrderBy(e => e.LosaId ?? int.MaxValue)
            .ThenBy(e => e.Espesor)
            .ToList();

        return new ResumenMuros(entradas);
    }
}
