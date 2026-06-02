using System.Collections.Generic;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Validation.Rules;

namespace LosasPlus.Validation;

/// <summary>
/// Engine de validación normativa. Aplica un conjunto de
/// <see cref="IValidationRule"/> sobre un proyecto y agrega los hallazgos en
/// un <see cref="ValidationReport"/>.
///
/// <para>
/// Uso:
/// </para>
/// <code>
/// var engine = ValidationEngine.Default();
/// var reporte = engine.Validar(proyecto);
/// // reporte.Errores, reporte.Issues, reporte.AReporteTexto()
/// </code>
///
/// <para>
/// El engine es stateless y reentrante — puede correrse en cada cambio del
/// modelo sin penalty. Si en el futuro se vuelve costoso, se puede cachear
/// el reporte y solo recalcular cuando cambian propiedades relevantes.
/// </para>
/// </summary>
public sealed class ValidationEngine
{
    private readonly IReadOnlyList<IValidationRule> _reglas;
    private readonly string _reglamentoAplicable;

    public ValidationEngine(IReadOnlyList<IValidationRule> reglas, string reglamentoAplicable)
    {
        _reglas = reglas;
        _reglamentoAplicable = reglamentoAplicable;
    }

    /// <summary>
    /// Engine pre-poblado con las reglas estándar para República Dominicana
    /// (R-001 + ACI 318-05) en el orden de prioridad en el panel:
    /// errores primero, luego advertencias.
    /// </summary>
    public static ValidationEngine Default() => new(
        new IValidationRule[]
        {
            new TipoLosaValidoRule(),
            new EspesorMinimoR001Rule(),
            new CargaVivaMinimaR001Rule(),
            new EspesorVsCalculadoRule(),
            new AspectoLosaRule(),
        },
        "República Dominicana · R-001 + ACI 318-05");

    public ValidationReport Validar(Proyecto proyecto)
    {
        var todos = new List<ValidationIssue>();
        var chequeos = 0;
        foreach (var regla in _reglas)
        {
            chequeos += regla.ContarChequeos(proyecto);
            todos.AddRange(regla.Evaluar(proyecto));
        }

        // Ordenar: errores primero, luego warnings; dentro de cada grupo,
        // mantener el orden de descubrimiento (estable para que la UI no
        // reordene cards al re-validar sin cambios).
        var ordenadas = todos
            .OrderBy(i => i.Severidad == ValidationSeverity.Error ? 0 : 1)
            .ToList();

        return new ValidationReport(ordenadas, chequeos, _reglamentoAplicable);
    }
}
