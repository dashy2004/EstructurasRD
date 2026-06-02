using System.Collections.Generic;
using LosasPlus.Cargas;

namespace LosasPlus.Vigas;

/// <summary>
/// Un punto muestreado de los diagramas de esfuerzos de una combinación, en una
/// abscisa <see cref="X"/> medida desde el origen de la viga.
/// </summary>
/// <param name="X">Abscisa a lo largo de la viga, en metros.</param>
/// <param name="Cortante">Fuerza cortante V(x), en kN.</param>
/// <param name="Momento">Momento flector M(x), en kN·m (positivo = tracción inferior).</param>
/// <param name="Deflexion">Deflexión δ(x), en metros (positiva hacia abajo).</param>
public readonly record struct PuntoDiagrama(
    double X,
    double Cortante,
    double Momento,
    double Deflexion);

/// <summary>Reacción vertical de un apoyo bajo una combinación.</summary>
/// <param name="X">Abscisa del apoyo, en metros.</param>
/// <param name="Valor">Reacción vertical, en kN (positiva hacia arriba).</param>
public readonly record struct ReaccionApoyo(
    double X,
    double Valor);

/// <summary>
/// Resultado de una combinación de carga: los diagramas V(x), M(x), δ(x)
/// muestreados a lo largo de la viga y las reacciones en los apoyos.
/// </summary>
/// <param name="NombreCombinacion">Nombre de la <c>CombinacionCarga</c> resuelta.</param>
/// <param name="TipoCombinacion">Servicio o última.</param>
/// <param name="Diagrama">Puntos muestreados de los diagramas de esfuerzos.</param>
/// <param name="Reacciones">Reacciones verticales en los apoyos.</param>
public sealed record ResultadoCombinacion(
    string NombreCombinacion,
    TipoCombinacion TipoCombinacion,
    IReadOnlyList<PuntoDiagrama> Diagrama,
    IReadOnlyList<ReaccionApoyo> Reacciones);

/// <summary>
/// Un punto de la envolvente de diseño: los valores extremos (máximo y mínimo)
/// de cada esfuerzo en una abscisa, tomados sobre todas las combinaciones.
/// </summary>
public readonly record struct PuntoEnvolvente(
    double X,
    double CortanteMaximo,
    double CortanteMinimo,
    double MomentoMaximo,
    double MomentoMinimo,
    double DeflexionMaxima,
    double DeflexionMinima);

/// <summary>
/// Envolvente de diseño de la viga: los diagramas extremos punto a punto sobre
/// todas las combinaciones, más los escalares de diseño globales.
/// </summary>
public sealed record EnvolventeViga(
    IReadOnlyList<PuntoEnvolvente> Puntos,
    double MomentoMaximo,
    double MomentoMinimo,
    double CortanteMaximo,
    double CortanteMinimo,
    double DeflexionMaxima,
    double DeflexionMinima);

/// <summary>
/// Resultado completo de resolver una <see cref="Viga"/>: un
/// <see cref="ResultadoCombinacion"/> por cada combinación de la base de cargas
/// y la <see cref="EnvolventeViga"/> de diseño.
///
/// <para>
/// Si la viga es un mecanismo (apoyos insuficientes para una solución estable),
/// <see cref="EsInestable"/> es <c>true</c>, <see cref="Mensaje"/> describe la
/// causa, <see cref="Combinaciones"/> queda vacía y <see cref="Envolvente"/>
/// es <c>null</c> — el motor nunca lanza una excepción por inestabilidad.
/// </para>
/// </summary>
public sealed record ResultadoViga(
    bool EsInestable,
    string? Mensaje,
    IReadOnlyList<ResultadoCombinacion> Combinaciones,
    EnvolventeViga? Envolvente)
{
    /// <summary>Construye un resultado que marca la viga como inestable (mecanismo).</summary>
    public static ResultadoViga Inestable(string mensaje)
        => new(true, mensaje, new List<ResultadoCombinacion>(), null);
}
