namespace LosasPlus.Auditoria;

/// <summary>
/// Severidad de un <see cref="MensajeAuditoria"/> emitido por el
/// <c>ProyectoAuditorEngine</c> al recorrer el grafo del proyecto
/// (Fase 7, Iteración 1 de la suite estructural).
/// </summary>
public enum TipoMensajeAuditoria
{
    /// <summary>Información puramente diagnóstica — no invalida la conformidad del proyecto.</summary>
    Info,

    /// <summary>Advertencia — el proyecto sigue siendo conforme, pero hay una incongruencia que revisar.</summary>
    Advertencia,

    /// <summary>Error de fallo — la condición invalida la conformidad global del proyecto.</summary>
    ErrorFallo,
}

/// <summary>
/// Un hallazgo emitido por el motor de auditoría cruzada del proyecto. Cada
/// mensaje categoriza la incidencia, le asigna un código estable y describe
/// la ubicación del elemento estructural afectado dentro del grafo del
/// proyecto (Fase 7, Iteración 1).
/// </summary>
/// <param name="Tipo">Severidad del hallazgo (Info / Advertencia / ErrorFallo).</param>
/// <param name="Categoria">
/// Categoría temática del hallazgo (p. ej. <c>"Materiales"</c>,
/// <c>"ContinuidadCarga"</c>, <c>"RatioDC"</c>) — facilita el filtrado en la UI.
/// </param>
/// <param name="Codigo">
/// Código estable del hallazgo (p. ej. <c>"MAT-001"</c>, <c>"CARGA-001"</c>) —
/// inmune a cambios del texto del <see cref="Mensaje"/>, sirve para tests
/// y para vincularse con la documentación reglamentaria.
/// </param>
/// <param name="Mensaje">Descripción legible del hallazgo, con valores numéricos cuando aplica.</param>
/// <param name="Ubicacion">
/// Ubicación del elemento dentro del grafo (p. ej.
/// <c>"Nivel 1 &gt; Columna C-3"</c>) — permite que la UI guíe al usuario
/// al lugar del problema.
/// </param>
public sealed record MensajeAuditoria(
    TipoMensajeAuditoria Tipo,
    string Categoria,
    string Codigo,
    string Mensaje,
    string Ubicacion);
