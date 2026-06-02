namespace LosasPlus.Services;

/// <summary>
/// Las 8 asas de redimensionado de un rectángulo: 4 esquinas + 4 puntos medios
/// de borde. El orden coincide con los tiradores del lienzo CAD (Fase 3).
/// </summary>
public enum AsaRedim
{
    /// <summary>Esquina superior-izquierda.</summary>
    SupIzq,
    /// <summary>Punto medio del borde superior.</summary>
    SupCentro,
    /// <summary>Esquina superior-derecha.</summary>
    SupDer,
    /// <summary>Punto medio del borde derecho.</summary>
    CentroDer,
    /// <summary>Esquina inferior-derecha.</summary>
    InfDer,
    /// <summary>Punto medio del borde inferior.</summary>
    InfCentro,
    /// <summary>Esquina inferior-izquierda.</summary>
    InfIzq,
    /// <summary>Punto medio del borde izquierdo.</summary>
    CentroIzq,
}

/// <summary>
/// Geometría de edición restringida (A2 de la FASE A del
/// <c>PLAN_V1.1_DIBUJO_AVANZADO.md</c>) — funciones puras que mueven o
/// redimensionan un <see cref="RectM"/>.
///
/// <para>
/// El resultado es <b>siempre</b> un rectángulo ortogonal (invariante
/// "Pieper-Martens"): el motor de cálculo sólo admite rectángulos, y estas
/// operaciones —que sólo tocan posición y dimensiones— no pueden producir otra
/// cosa. Sin dependencias de <c>System.Windows</c>.
/// </para>
/// </summary>
public static class GeometriaEdicion
{
    /// <summary>
    /// Traslada <paramref name="original"/> por <paramref name="dx"/>,
    /// <paramref name="dy"/>. Las dimensiones quedan intactas.
    /// </summary>
    public static RectM Mover(RectM original, double dx, double dy)
        => new(original.X + dx, original.Y + dy, original.Ancho, original.Alto);

    /// <summary>
    /// Redimensiona <paramref name="original"/> arrastrando el asa
    /// <paramref name="asa"/> hasta <paramref name="destino"/>. El borde o la
    /// esquina <b>opuesta</b> al asa queda anclada. Se respeta
    /// <paramref name="ladoMin"/> como lado mínimo: un borde móvil nunca cruza
    /// al borde fijo opuesto (sin "flip"). El resultado es ortogonal.
    /// </summary>
    /// <param name="original">Rectángulo de partida.</param>
    /// <param name="asa">El asa que el usuario arrastra.</param>
    /// <param name="destino">Posición destino del asa, en metros.</param>
    /// <param name="ladoMin">Lado mínimo permitido del rectángulo, en metros.</param>
    public static RectM Redimensionar(RectM original, AsaRedim asa, PuntoM destino, double ladoMin)
    {
        bool mueveIzq = asa is AsaRedim.SupIzq or AsaRedim.InfIzq or AsaRedim.CentroIzq;
        bool mueveDer = asa is AsaRedim.SupDer or AsaRedim.CentroDer or AsaRedim.InfDer;
        bool mueveSup = asa is AsaRedim.SupIzq or AsaRedim.SupCentro or AsaRedim.SupDer;
        bool mueveInf = asa is AsaRedim.InfDer or AsaRedim.InfCentro or AsaRedim.InfIzq;

        double izq = original.Izq, der = original.Der;
        double sup = original.Sup, inf = original.Inf;

        if (mueveIzq) izq = destino.X;
        if (mueveDer) der = destino.X;
        if (mueveSup) sup = destino.Y;
        if (mueveInf) inf = destino.Y;

        // Lado mínimo, sin "flip": el borde móvil no cruza al borde fijo opuesto.
        if (mueveIzq && izq > der - ladoMin) izq = der - ladoMin;
        if (mueveDer && der < izq + ladoMin) der = izq + ladoMin;
        if (mueveSup && sup > inf - ladoMin) sup = inf - ladoMin;
        if (mueveInf && inf < sup + ladoMin) inf = sup + ladoMin;

        return new RectM(izq, sup, der - izq, inf - sup);
    }
}
