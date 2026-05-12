using System.Collections.Generic;
using LosasPlus.Models;

namespace LosasPlus.Validation.Rules;

/// <summary>
/// Recomendación práctica — la relación de aspecto <c>Ly/Lx</c> debe quedar
/// dentro del rango [0.5, 2.0] cubierto por las tablas Pieper-Martens. Fuera
/// de él, las tablas extrapolan y los momentos calculados pierden precisión.
///
/// <para>Severidad: <see cref="ValidationSeverity.Warning"/>.</para>
/// </summary>
public sealed class AspectoLosaRule : IValidationRule
{
    public int ContarChequeos(Proyecto proyecto)
    {
        var n = 0;
        foreach (var s in proyecto.Sistemas)
        foreach (var l in s.Losas)
            if (l.Lx > 0 && l.Ly > 0) n++;
        return n;
    }

    public IEnumerable<ValidationIssue> Evaluar(Proyecto proyecto)
    {
        foreach (var sistema in proyecto.Sistemas)
        {
            foreach (var losa in sistema.Losas)
            {
                if (!losa.AspectoFueraDeRango) continue;
                var aspecto = losa.Aspecto;
                yield return new ValidationIssue
                {
                    Codigo = "PM-aspecto-fuera-rango",
                    Severidad = ValidationSeverity.Warning,
                    Titulo = $"Relación Ly/Lx = {aspecto:0.00} fuera del rango Pieper-Martens [0.50, 2.00]",
                    Descripcion = "Las tablas de Pieper-Martens están definidas para relaciones de aspecto " +
                                  "entre 0.50 y 2.00. Fuera de ese rango, los coeficientes se extrapolan y " +
                                  "los momentos calculados pierden precisión. Considerar subdividir la losa.",
                    ClausulaCita = "Pieper-Martens (1965) — Tablas de coeficientes de momentos para losas " +
                                   "rectangulares en flexión bidireccional.",
                    ReferenciaNormativa = "Tablas Pieper-Martens",
                    NombreSistema = sistema.Nombre,
                    LosaId = losa.Id,
                    ValorActual = aspecto,
                    Unidad = "",
                };
            }
        }
    }
}
