using System;
using System.Collections.Generic;
using LosasPlus.Models;

namespace LosasPlus.Validation.Rules;

/// <summary>
/// ACI 318 §9.5.3.3 — el espesor configurado de una losa no debe ser menor que
/// el espesor calculado por la fórmula <c>h_calc = Ln / 30..36</c> (función de
/// la condición de borde) que controla la deflexión a largo plazo.
///
/// <para>
/// Esta regla emite:
/// </para>
/// <list type="bullet">
///   <item><see cref="ValidationSeverity.Error"/> cuando <c>Espesor &lt; HCalc</c>
///     (déficit real — la memoria no cumplirá).</item>
///   <item><see cref="ValidationSeverity.Warning"/> cuando <c>Espesor</c> está
///     dentro de un margen de 0.5 % del cálculo (sin margen de seguridad).</item>
/// </list>
/// <para>
/// Si <see cref="Losa.HCalc"/> no ha corrido (null) la losa NO suma chequeos —
/// no se penaliza por falta de input.
/// </para>
/// </summary>
public sealed class EspesorVsCalculadoRule : IValidationRule
{
    private const double MargenSeguridadRel = 0.005;  // 0.5 %

    public int ContarChequeos(Proyecto proyecto)
    {
        var n = 0;
        foreach (var s in proyecto.Sistemas)
        foreach (var l in s.Losas)
            if (l.HCalc.HasValue) n++;
        return n;
    }

    public IEnumerable<ValidationIssue> Evaluar(Proyecto proyecto)
    {
        foreach (var sistema in proyecto.Sistemas)
        {
            foreach (var losa in sistema.Losas)
            {
                if (!losa.HCalc.HasValue) continue;
                var hCalc = losa.HCalc.Value;
                if (hCalc <= 0) continue;

                if (losa.Espesor + 1e-9 < hCalc)
                {
                    yield return new ValidationIssue
                    {
                        Codigo = "ACI-9.5.3.3-deficit",
                        Severidad = ValidationSeverity.Error,
                        Titulo = $"Espesor insuficiente: configurado {losa.Espesor:0.000} m, requiere {hCalc:0.000} m",
                        Descripcion = $"El espesor configurado ({losa.Espesor:0.000} m) es menor que el espesor " +
                                      $"calculado según ACI 318 §9.5.3 ({hCalc:0.000} m). La memoria no cumplirá.",
                        ClausulaCita = "ACI 318 §9.5.3.3 — Para losas en dos direcciones, el espesor mínimo no " +
                                       "debe ser menor que el valor calculado por la fórmula h = Ln / (36+9β) " +
                                       "para asegurar control de deflexiones a largo plazo.",
                        ReferenciaNormativa = "ACI 318 §9.5.3.3",
                        NombreSistema = sistema.Nombre,
                        LosaId = losa.Id,
                        ValorActual = losa.Espesor,
                        ValorRequerido = hCalc,
                        Unidad = "m",
                    };
                }
                else if (losa.Espesor < hCalc * (1.0 + MargenSeguridadRel))
                {
                    yield return new ValidationIssue
                    {
                        Codigo = "ACI-9.5.3.3-sin-margen",
                        Severidad = ValidationSeverity.Warning,
                        Titulo = $"Espesor de {losa.Espesor:0.000} m igual al mínimo. Considerar margen de seguridad",
                        Descripcion = "El espesor configurado coincide con el calculado al límite. " +
                                      "Cualquier desvío constructivo lo dejará por debajo del mínimo.",
                        ClausulaCita = "Recomendación de la práctica profesional: dejar al menos 0.5–1.0 % de " +
                                       "margen sobre h_calc para absorber tolerancias de obra.",
                        ReferenciaNormativa = "Recomendación",
                        NombreSistema = sistema.Nombre,
                        LosaId = losa.Id,
                        ValorActual = losa.Espesor,
                        ValorRequerido = hCalc,
                        Unidad = "m",
                    };
                }
            }
        }
    }
}
