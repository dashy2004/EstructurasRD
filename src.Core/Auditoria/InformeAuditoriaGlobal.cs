using System;
using System.Collections.Generic;

namespace LosasPlus.Auditoria;

/// <summary>
/// Informe consolidado de la auditoría cruzada de un proyecto (Fase 7,
/// Iteración 1). Reúne la lista estructurada de <see cref="MensajeAuditoria"/>
/// emitidos por el motor, los ratios máximos demanda/capacidad por tipo de
/// elemento estructural, el ratio máximo global del proyecto y la bandera
/// agregada de conformidad.
/// </summary>
/// <param name="Mensajes">Hallazgos emitidos por el motor — ordenados por categoría y código.</param>
/// <param name="CantidadAdvertencias">Total de mensajes de tipo <c>Advertencia</c>.</param>
/// <param name="CantidadErrores">Total de mensajes de tipo <c>ErrorFallo</c>.</param>
/// <param name="RatioMaximoVigas">Peor ratio D/C de flexión sobre todas las vigas activas.</param>
/// <param name="RatioMaximoColumnas">Peor ratio D/C radial sobre todas las demandas de columnas.</param>
/// <param name="RatioMaximoZapatas">Peor ratio estructural ELU sobre todas las zapatas activas.</param>
/// <param name="RatioMaximoGlobal">Máximo de los tres ratios anteriores.</param>
/// <param name="ProyectoConforme">
/// <c>true</c> si <see cref="RatioMaximoGlobal"/> ≤ 1.0 Y no hay mensajes de
/// tipo <see cref="TipoMensajeAuditoria.ErrorFallo"/>. Las advertencias no
/// invalidan la conformidad por sí solas — el usuario debe revisarlas.
/// </param>
public sealed record InformeAuditoriaGlobal(
    IReadOnlyList<MensajeAuditoria> Mensajes,
    int CantidadAdvertencias,
    int CantidadErrores,
    double RatioMaximoVigas,
    double RatioMaximoColumnas,
    double RatioMaximoZapatas,
    double RatioMaximoGlobal,
    bool ProyectoConforme)
{
    /// <summary>
    /// Informe vacío — para un proyecto nulo o con datos inválidos a nivel
    /// de configuración (fc/fy no positivos).
    /// </summary>
    public static InformeAuditoriaGlobal Vacio { get; } = new(
        Mensajes: Array.Empty<MensajeAuditoria>(),
        CantidadAdvertencias: 0,
        CantidadErrores: 0,
        RatioMaximoVigas: 0.0,
        RatioMaximoColumnas: 0.0,
        RatioMaximoZapatas: 0.0,
        RatioMaximoGlobal: 0.0,
        ProyectoConforme: true);
}
