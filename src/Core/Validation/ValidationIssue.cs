using System;

namespace LosasPlus.Validation;

/// <summary>
/// Severidad de un hallazgo de validación normativa.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>Incumple una cláusula obligatoria — bloquea aprobación.</summary>
    Error,
    /// <summary>No incumple, pero requiere atención del ingeniero (margen de seguridad bajo, recomendación, etc.).</summary>
    Warning,
}

/// <summary>
/// Un hallazgo individual del <see cref="ValidationEngine"/>. Inmutable.
/// Cada issue mapea a una cláusula de un reglamento (R-001 / ACI 318) y describe
/// dónde aparece (proyecto / sistema / losa) y qué valor causó el problema.
/// </summary>
public sealed record ValidationIssue
{
    /// <summary>Código interno único — útil para suprimir / agrupar issues.</summary>
    public required string Codigo { get; init; }

    public required ValidationSeverity Severidad { get; init; }

    /// <summary>Título corto (una línea) — mostrado en la lista lateral.</summary>
    public required string Titulo { get; init; }

    /// <summary>Descripción larga — visible en el detalle.</summary>
    public string Descripcion { get; init; } = "";

    /// <summary>Cita exacta de la cláusula del reglamento (mostrada en el detalle).</summary>
    public string ClausulaCita { get; init; } = "";

    /// <summary>Etiqueta corta del reglamento — ej. "R-001 §3.2.1" / "ACI 318 §9.5.3".</summary>
    public required string ReferenciaNormativa { get; init; }

    /// <summary>Nombre del sistema (nivel) afectado, o null si es un issue global.</summary>
    public string? NombreSistema { get; init; }

    /// <summary>Id de la losa afectada, o null si el issue es del sistema completo o del proyecto.</summary>
    public int? LosaId { get; init; }

    /// <summary>Valor que se midió. Formateado por la UI.</summary>
    public double? ValorActual { get; init; }

    /// <summary>Valor mínimo (o máximo) requerido por la norma.</summary>
    public double? ValorRequerido { get; init; }

    /// <summary>Unidad de los valores numéricos — ej. "m", "t/m²", "cm".</summary>
    public string Unidad { get; init; } = "";

    /// <summary>
    /// Texto formateado para mostrar como ubicación en la card del panel:
    /// "Nivel E1 · Losa L4" / "Sistema E2" / "Proyecto".
    /// </summary>
    public string UbicacionFormateada
    {
        get
        {
            if (NombreSistema is null) return "Proyecto";
            if (LosaId is null)        return $"Sistema {NombreSistema}";
            return $"Nivel {NombreSistema} · Losa L{LosaId}";
        }
    }
}
