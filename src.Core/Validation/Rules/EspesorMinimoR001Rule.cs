using System;
using System.Collections.Generic;
using LosasPlus.Models;
using LosasPlus.Services;

namespace LosasPlus.Validation.Rules;

/// <summary>
/// R-001 §3.2.1 — Espesor mínimo de losas macizas para uso residencial o de
/// oficina. La norma define un umbral según uso del proyecto que no depende
/// del cálculo de deflexiones de ACI 318 — es una cota inferior absoluta.
///
/// <para>
/// Umbrales (metros):
/// </para>
/// <list type="table">
///   <item><term>Residencial</term><description>0.10 m</description></item>
///   <item><term>Oficinas / Comercial</term><description>0.12 m</description></item>
///   <item><term>Industrial</term><description>0.15 m</description></item>
///   <item><term>Otros</term><description>0.10 m (mínimo conservador)</description></item>
/// </list>
/// <para>
/// Severidad: Error. Una losa por debajo bloquea la aprobación.
/// </para>
/// </summary>
public sealed class EspesorMinimoR001Rule : IValidationRule
{
    public int ContarChequeos(Proyecto proyecto)
    {
        var n = 0;
        foreach (var s in ProyectoService.EnumerarSistemas(proyecto)) n += s.Losas.Count;
        return n;
    }

    public IEnumerable<ValidationIssue> Evaluar(Proyecto proyecto)
    {
        var minimo = EspesorMinimoSegunUso(proyecto.Uso);
        var usoEtiqueta = string.IsNullOrWhiteSpace(proyecto.Uso) ? "el uso seleccionado" : proyecto.Uso.ToLowerInvariant();
        foreach (var sistema in ProyectoService.EnumerarSistemas(proyecto))
        {
            foreach (var losa in sistema.Losas)
            {
                if (losa.Espesor + 1e-9 < minimo)
                {
                    yield return new ValidationIssue
                    {
                        Codigo = "R-001-3.2.1-espesor",
                        Severidad = ValidationSeverity.Error,
                        Titulo = $"Espesor {losa.Espesor:0.00} m menor que mínimo R-001 para uso {usoEtiqueta}",
                        Descripcion = $"R-001 §3.2.1 fija un espesor mínimo de {minimo:0.00} m para losas " +
                                      $"de uso {usoEtiqueta}, independientemente del cálculo de deflexiones.",
                        ClausulaCita = "R-001 §3.2.1 — Espesor mínimo de losas macizas para uso residencial " +
                                       "o de oficina: 0.12 m para luces menores a 4.00 m, independientemente " +
                                       "del cálculo de deflexiones.",
                        ReferenciaNormativa = "R-001 §3.2.1",
                        NombreSistema = sistema.Nombre,
                        LosaId = losa.Id,
                        ValorActual = losa.Espesor,
                        ValorRequerido = minimo,
                        Unidad = "m",
                    };
                }
            }
        }
    }

    /// <summary>
    /// Devuelve el espesor mínimo en metros que R-001 §3.2.1 exige para el uso
    /// dado. Compara case-insensitive; valores desconocidos caen al mínimo
    /// conservador residencial (0.10 m).
    /// </summary>
    public static double EspesorMinimoSegunUso(string? uso)
    {
        if (string.IsNullOrWhiteSpace(uso)) return 0.10;
        var u = uso.Trim().ToLowerInvariant();
        if (u.StartsWith("comerc") || u.StartsWith("ofic"))  return 0.12;
        if (u.StartsWith("indust"))                          return 0.15;
        if (u.StartsWith("hospit"))                          return 0.12;
        return 0.10;  // Residencial / Mixto / Educacional / Otro
    }
}
