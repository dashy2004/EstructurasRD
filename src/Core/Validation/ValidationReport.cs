using System;
using System.Collections.Generic;
using System.Linq;

namespace LosasPlus.Validation;

/// <summary>
/// Resultado de correr el <see cref="ValidationEngine"/> sobre un proyecto.
/// Contiene los issues encontrados y conteos agregados para el indicador del
/// top bar y la lista lateral.
/// </summary>
public sealed class ValidationReport
{
    public ValidationReport(
        IReadOnlyList<ValidationIssue> issues,
        int chequeosTotal,
        string reglamentoAplicable)
    {
        Issues = issues;
        ChequeosTotal = chequeosTotal;
        ReglamentoAplicable = reglamentoAplicable;
        Generado = DateTime.Now;
    }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>
    /// Cantidad TOTAL de chequeos individuales corridos contra el proyecto,
    /// pasados o fallados. <see cref="Conformes"/> = ChequeosTotal − Issues.Count.
    /// Usado por el panel para mostrar "X conformes".
    /// </summary>
    public int ChequeosTotal { get; }

    public string ReglamentoAplicable { get; }

    public DateTime Generado { get; }

    public int Errores       => Issues.Count(i => i.Severidad == ValidationSeverity.Error);
    public int Observaciones => Issues.Count(i => i.Severidad == ValidationSeverity.Warning);
    public int Conformes     => Math.Max(0, ChequeosTotal - Issues.Count);

    /// <summary>True si hay al menos un error que bloquea aprobación.</summary>
    public bool TieneErrores => Errores > 0;

    /// <summary>True si no hay errores ni observaciones — proyecto limpio.</summary>
    public bool EstaLimpio => Issues.Count == 0;

    /// <summary>Reporte vacío para inicializar VM antes de la primera validación.</summary>
    public static ValidationReport Vacio { get; } = new(Array.Empty<ValidationIssue>(), 0, "");

    /// <summary>
    /// Texto plano para "Copiar reporte" (commit 22). Lista cada issue con su
    /// referencia normativa y ubicación, prefijado por la fecha de generación.
    /// </summary>
    public string AReporteTexto()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"VALIDACIÓN NORMATIVA — {ReglamentoAplicable}");
        sb.AppendLine($"Generado: {Generado:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Errores: {Errores} · Observaciones: {Observaciones} · Conformes: {Conformes}");
        sb.AppendLine(new string('─', 60));
        foreach (var issue in Issues)
        {
            var marca = issue.Severidad == ValidationSeverity.Error ? "[ERROR]" : "[OBS]";
            sb.AppendLine($"{marca} {issue.ReferenciaNormativa} — {issue.Titulo}");
            sb.AppendLine($"        Ubicación: {issue.UbicacionFormateada}");
            if (!string.IsNullOrWhiteSpace(issue.Descripcion))
                sb.AppendLine($"        {issue.Descripcion}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
