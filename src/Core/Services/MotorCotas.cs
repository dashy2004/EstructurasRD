using System;
using System.Collections.Generic;

namespace LosasPlus.Services;

/// <summary>Orientación de una <see cref="CotaInfo"/>.</summary>
public enum EjeCota
{
    /// <summary>Mide una distancia horizontal; la línea de cota es horizontal.</summary>
    Horizontal,
    /// <summary>Mide una distancia vertical; la línea de cota es vertical.</summary>
    Vertical,
}

/// <summary>
/// Una cota (dimensión) a dibujar, en metros (A4 de la FASE A). Una cota
/// <see cref="EjeCota.Horizontal"/> mide de <see cref="Inicio"/> a
/// <see cref="Fin"/> sobre el eje X, con la línea de cota a la altura
/// <see cref="CoordenadaLinea"/> (un valor de Y); la <see cref="EjeCota.Vertical"/>
/// es análoga intercambiando los ejes.
/// </summary>
/// <param name="Eje">Orientación de la medida.</param>
/// <param name="CoordenadaLinea">Coordenada perpendicular donde se traza la línea de cota.</param>
/// <param name="Inicio">Coordenada inicial sobre el eje medido.</param>
/// <param name="Fin">Coordenada final sobre el eje medido.</param>
/// <param name="Valor">Distancia medida (escalar, = |Fin − Inicio|).</param>
/// <param name="Etiqueta">Etiqueta legible de la cota ("d1", "d2", …).</param>
public readonly record struct CotaInfo(
    EjeCota Eje,
    double CoordenadaLinea,
    double Inicio,
    double Fin,
    double Valor,
    string Etiqueta);

/// <summary>
/// Motor de cotas dinámicas (A4 de la FASE A del
/// <c>PLAN_V1.1_DIBUJO_AVANZADO.md</c>). Dada una losa activa en movimiento y la
/// colección de losas ancladas, calcula las cotas ortogonales —las distancias
/// dₙ— entre la activa y sus vecinas relevantes.
///
/// <para>
/// Función pura, sin dependencias de UI. Se apoya en
/// <see cref="TopologiaAdyacencia"/>; el renderizado de las cotas es
/// responsabilidad de la FASE B.
/// </para>
/// </summary>
public static class MotorCotas
{
    /// <summary>Separación entre líneas de cota paralelas, en metros (escalonado).</summary>
    private const double EscalonLinea = 0.30;

    /// <summary>
    /// Calcula las cotas dinámicas de <paramref name="activa"/> respecto de las
    /// losas <paramref name="ancladas"/>. Devuelve una lista vacía si no hay
    /// ninguna vecina relevante.
    /// </summary>
    public static IReadOnlyList<CotaInfo> Calcular(
        RectM activa, IReadOnlyList<RectM> ancladas,
        double tolerancia = TopologiaAdyacencia.ToleranciaPorDefecto)
    {
        var cotas = new List<CotaInfo>();
        if (ancladas is null) return cotas;

        int paralelasV = 0;   // cotas verticales emitidas (para escalonar)
        int paralelasH = 0;   // cotas horizontales emitidas
        int etiqueta = 0;     // contador d1, d2, …

        foreach (var info in TopologiaAdyacencia.AnalizarVecindario(activa, ancladas, tolerancia))
        {
            if (info.Clasificacion == ClasificacionAdyacencia.SinRelacion) continue;
            var vecina = ancladas[info.IndiceVecina];

            if (info.EsBordeX)
            {
                // Cara compartida vertical → eje de conexión X, desfases en Y.
                if (info.Holgura > tolerancia)
                {
                    double ini = Math.Min(activa.Der, vecina.Der);
                    double fin = Math.Max(activa.Izq, vecina.Izq);
                    double linea = activa.Inf + EscalonLinea * ++paralelasH;
                    cotas.Add(new CotaInfo(EjeCota.Horizontal, linea,
                        ini, fin, Math.Abs(fin - ini), "d" + ++etiqueta));
                }
                if (Math.Abs(info.DesfaseSuperior) > tolerancia)
                {
                    double linea = activa.Der + EscalonLinea * ++paralelasV;
                    cotas.Add(new CotaInfo(EjeCota.Vertical, linea,
                        activa.Sup, vecina.Sup, Math.Abs(activa.Sup - vecina.Sup), "d" + ++etiqueta));
                }
                if (Math.Abs(info.DesfaseInferior) > tolerancia)
                {
                    double linea = activa.Der + EscalonLinea * ++paralelasV;
                    cotas.Add(new CotaInfo(EjeCota.Vertical, linea,
                        activa.Inf, vecina.Inf, Math.Abs(activa.Inf - vecina.Inf), "d" + ++etiqueta));
                }
            }
            else
            {
                // Cara compartida horizontal → eje de conexión Y, desfases en X.
                if (info.Holgura > tolerancia)
                {
                    double ini = Math.Min(activa.Inf, vecina.Inf);
                    double fin = Math.Max(activa.Sup, vecina.Sup);
                    double linea = activa.Der + EscalonLinea * ++paralelasV;
                    cotas.Add(new CotaInfo(EjeCota.Vertical, linea,
                        ini, fin, Math.Abs(fin - ini), "d" + ++etiqueta));
                }
                if (Math.Abs(info.DesfaseSuperior) > tolerancia)
                {
                    double linea = activa.Inf + EscalonLinea * ++paralelasH;
                    cotas.Add(new CotaInfo(EjeCota.Horizontal, linea,
                        activa.Izq, vecina.Izq, Math.Abs(activa.Izq - vecina.Izq), "d" + ++etiqueta));
                }
                if (Math.Abs(info.DesfaseInferior) > tolerancia)
                {
                    double linea = activa.Inf + EscalonLinea * ++paralelasH;
                    cotas.Add(new CotaInfo(EjeCota.Horizontal, linea,
                        activa.Der, vecina.Der, Math.Abs(activa.Der - vecina.Der), "d" + ++etiqueta));
                }
            }
        }
        return cotas;
    }
}
