using System.Collections.Generic;
using System.Linq;
using LosasPlus.Models;

namespace LosasPlus.Validation.Rules;

/// <summary>
/// R-001 §2.1.4 — Carga viva mínima por uso para sistemas de entrepiso o techo.
///
/// <para>Umbrales (t/m²):</para>
/// <list type="table">
///   <item><term>Residencial — entrepiso</term><description>0.200 t/m²</description></item>
///   <item><term>Residencial — techo</term><description>0.100 t/m²</description></item>
///   <item><term>Oficina — entrepiso</term><description>0.250 t/m²</description></item>
///   <item><term>Comercial — entrepiso</term><description>0.500 t/m²</description></item>
/// </list>
///
/// <para>
/// El chequeo es per-sistema (no per-losa) y compara contra <see cref="Losa.Ql"/>
/// de la primera losa del sistema (el engine de cálculo asigna el mismo valor
/// a todas las losas del nivel). Sistemas vacíos no se chequean.
/// </para>
/// </summary>
public sealed class CargaVivaMinimaR001Rule : IValidationRule
{
    public int ContarChequeos(Proyecto proyecto)
        => proyecto.Sistemas.Count(s => s.Losas.Any(l => l.Ql.HasValue));

    public IEnumerable<ValidationIssue> Evaluar(Proyecto proyecto)
    {
        var minimo = CargaMinimaSegunUso(proyecto.Uso);
        if (minimo <= 0) yield break;

        foreach (var sistema in proyecto.Sistemas)
        {
            var primeraConQl = sistema.Losas.FirstOrDefault(l => l.Ql.HasValue);
            if (primeraConQl is null) continue;

            var ql = primeraConQl.Ql!.Value;
            var minSistema = sistema.Uso == SistemaUso.Techo ? minimo * 0.5 : minimo;

            if (ql + 1e-9 < minSistema)
            {
                var tipoSistemaTexto = sistema.Uso == SistemaUso.Techo ? "techo" : "entrepiso";
                yield return new ValidationIssue
                {
                    Codigo = "R-001-2.1.4-carga-viva",
                    Severidad = ValidationSeverity.Error,
                    Titulo = $"Carga viva {ql:0.00} t/m² menor que mínima R-001 para {tipoSistemaTexto}",
                    Descripcion = $"R-001 §2.1.4 fija una carga viva mínima de {minSistema:0.00} t/m² para " +
                                  $"sistemas de {tipoSistemaTexto} con uso {proyecto.Uso?.ToLowerInvariant()}. " +
                                  $"El sistema {sistema.Nombre} está {minSistema - ql:0.00} t/m² por debajo.",
                    ClausulaCita = "R-001 §2.1.4 — Las cargas vivas de cálculo no podrán ser menores que los " +
                                   "valores tabulados según ocupación del edificio.",
                    ReferenciaNormativa = "R-001 §2.1.4",
                    NombreSistema = sistema.Nombre,
                    ValorActual = ql,
                    ValorRequerido = minSistema,
                    Unidad = "t/m²",
                };
            }
        }
    }

    /// <summary>
    /// Carga viva mínima R-001 para uso del proyecto, expresada como el valor
    /// para entrepiso. Para techo, el engine divide por 2 internamente.
    /// </summary>
    public static double CargaMinimaSegunUso(string? uso)
    {
        if (string.IsNullOrWhiteSpace(uso)) return 0.20;
        var u = uso.Trim().ToLowerInvariant();
        if (u.StartsWith("ofic"))    return 0.25;
        if (u.StartsWith("comerc"))  return 0.50;
        if (u.StartsWith("indust"))  return 0.50;
        if (u.StartsWith("hospit"))  return 0.30;
        if (u.StartsWith("educac"))  return 0.30;
        return 0.20;  // Residencial / Mixto / Otro
    }
}
