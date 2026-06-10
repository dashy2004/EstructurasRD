using System.Collections.Generic;
using LosasPlus.Models;
using LosasPlus.Services;

namespace LosasPlus.Validation.Rules;

/// <summary>
/// Regla de negocio estricta — el código de tipo de cada losa debe ser uno de
/// los 23 tipos de borde del catálogo del formato .DL
/// (<see cref="TipoLosa.CodigosValidos"/>): 10, 13, 14, 21–24, 31–34, 40,
/// 43, 44, 51–54, 60, 63, 64, 71, 72.
///
/// <para>
/// Cualquier losa con un código fuera de ese conjunto produce un
/// <see cref="ValidationSeverity.Error"/>. Es la red de seguridad "fail-fast"
/// del modelo: aunque un .DL legacy o una edición manual introduzca un código
/// inválido (p.ej. 41, o 99), el panel de Validación lo señala de inmediato y
/// el ingeniero no puede aprobar el cálculo hasta corregirlo.
/// </para>
/// </summary>
public sealed class TipoLosaValidoRule : IValidationRule
{
    public int ContarChequeos(Proyecto proyecto)
    {
        var n = 0;
        foreach (var s in ProyectoService.EnumerarSistemas(proyecto))
            n += s.Losas.Count;
        return n;
    }

    public IEnumerable<ValidationIssue> Evaluar(Proyecto proyecto)
    {
        foreach (var sistema in ProyectoService.EnumerarSistemas(proyecto))
        {
            foreach (var losa in sistema.Losas)
            {
                if (TipoLosa.EsCodigoValido(losa.Tipo)) continue;
                yield return new ValidationIssue
                {
                    Codigo = "TIPO-LOSA-NO-PERMITIDO",
                    Severidad = ValidationSeverity.Error,
                    Titulo = $"Tipo de losa {losa.Tipo} no permitido",
                    Descripcion =
                        $"La losa usa el código de tipo {losa.Tipo}, que no pertenece al " +
                        "catálogo de 23 tipos de borde del formato .DL " +
                        "(10, 13, 14, 21–24, 31–34, 40, 43, 44, 51–54, 60, 63, 64, 71, 72). " +
                        "Corregí el tipo en el editor o en el archivo .DL antes de calcular.",
                    ClausulaCita = "Catálogo de patrones de borde de Pieper-Martens — " +
                                   "tipos del formato .DL (Losas v5.21).",
                    ReferenciaNormativa = "Catálogo Pieper-Martens",
                    NombreSistema = sistema.Nombre,
                    LosaId = losa.Id,
                    ValorActual = losa.Tipo,
                    Unidad = "",
                };
            }
        }
    }
}
