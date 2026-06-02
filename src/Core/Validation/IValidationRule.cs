using System.Collections.Generic;
using LosasPlus.Models;

namespace LosasPlus.Validation;

/// <summary>
/// Una regla de validación. Cada regla representa una cláusula del reglamento
/// y emite uno o más <see cref="ValidationIssue"/> al evaluarse contra un
/// proyecto. Las reglas DEBEN ser stateless (sin side-effects) — el engine
/// las invoca múltiples veces y en paralelo en el futuro.
/// </summary>
public interface IValidationRule
{
    /// <summary>
    /// Cantidad de chequeos individuales que esta regla corre. Usado para
    /// calcular el "conformes" del reporte (chequeos pasados = total − issues).
    /// Por ejemplo, una regla per-losa contra un proyecto de 30 losas devuelve 30.
    /// </summary>
    int ContarChequeos(Proyecto proyecto);

    IEnumerable<ValidationIssue> Evaluar(Proyecto proyecto);
}
