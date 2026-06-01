using System;
using System.Collections.Generic;
using System.Linq;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Métricas del resultado de una pasada de auto-alineación + auto-conexión
/// (Epic v1.3 Iteración 3).
/// </summary>
/// <param name="LosasAlineadasCount">
/// Cantidad de losas distintas cuya posición fue ajustada en el PASO 1.
/// </param>
/// <param name="BordesCreadosCount">
/// Cantidad de <see cref="BordeAdic"/> nuevos inyectados en el PASO 2.
/// </param>
public sealed record ResultadoAlineacion(int LosasAlineadasCount, int BordesCreadosCount);

/// <summary>
/// Motor de geometría analítica del editor CAD (Epic v1.3 Iteración 3).
/// Automatiza dos tareas del modelado de losas:
///
/// <list type="number">
///   <item><b>Auto-alineación</b> — cierra los gaps pequeños (≤ tolerancia)
///         entre losas vecinas para que sus aristas calcen exacto.</item>
///   <item><b>Auto-conexión</b> — genera los <see cref="BordeAdic"/> de
///         continuidad de acero entre las losas que quedaron en contacto
///         físico perfecto, reutilizando <see cref="AdyacenciaDetector"/>.</item>
/// </list>
///
/// <para>
/// Servicio de dominio <b>puramente matemático</b>: cero dependencias de WPF
/// o <c>System.Windows</c>. Opera sólo sobre losas <b>ancladas</b>
/// (<see cref="Losa.TienePosicionExplicita"/>) — una losa flotante no tiene
/// una posición autoral que alinear.
/// </para>
/// </summary>
public static class MotorGeometriaAnalitica
{
    /// <summary>Tolerancia mínima para distinguir un gap real de ruido numérico (m).</summary>
    private const double EpsilonContacto = 1e-9;

    /// <summary>
    /// Ejecuta la auto-alineación y la auto-conexión sobre <paramref name="sistema"/>.
    /// Muta directamente las posiciones de las losas y las colecciones
    /// <see cref="Sistema.BordesX"/> / <see cref="Sistema.BordesY"/>.
    /// </summary>
    /// <param name="sistema">El sistema de losas a optimizar (se modifica in situ).</param>
    /// <param name="toleranciaMetros">
    /// Holgura máxima entre aristas para considerarlas "casi en contacto" y
    /// cerrarlas (por defecto 5 cm).
    /// </param>
    public static ResultadoAlineacion EjecutarAlineacionYConexion(
        Sistema sistema, double toleranciaMetros = 0.05)
    {
        if (sistema is null) return new ResultadoAlineacion(0, 0);

        // ----- PASO 1 — Alineación (cerrar gaps entre vecinas reales) -----
        // Pasada única sobre todas las parejas (i < j): la losa de menor
        // índice ancla, la de mayor índice se mueve. Determinístico.
        var ancladas = sistema.Losas.Where(l => l.TienePosicionExplicita).ToList();
        var movidas = new HashSet<int>();
        for (int i = 0; i < ancladas.Count; i++)
            for (int j = i + 1; j < ancladas.Count; j++)
                if (AlinearPar(ancladas[i], ancladas[j], toleranciaMetros))
                    movidas.Add(ancladas[j].Id);

        // ----- PASO 2 — Conexión (reutiliza AdyacenciaDetector) -----
        // Tras el PASO 1 las vecinas alineadas quedan a distancia EXACTA 0;
        // un epsilon mínimo deja pasar sólo el contacto físico perfecto. El
        // detector ya verifica el solape de frontera y excluye los pares con
        // un BordeAdic preexistente.
        var candidatas = AdyacenciaDetector.Detectar(sistema, EpsilonContacto);
        foreach (var c in candidatas)
        {
            var borde = new BordeAdic { BI = c.BI, BJ = c.BJ, Balanceo = "S" };
            (c.EsBordeX ? sistema.BordesX : sistema.BordesY).Add(borde);
        }

        return new ResultadoAlineacion(movidas.Count, candidatas.Count);
    }

    /// <summary>
    /// Si <paramref name="a"/> y <paramref name="b"/> son vecinas (se solapan
    /// en el eje perpendicular) y la arista enfrentada está a ≤
    /// <paramref name="tol"/>, mueve <paramref name="b"/> para que las aristas
    /// calcen exacto. Cada par se alinea en un único eje: X si están lado a
    /// lado, Y si están apiladas — determinado por en qué eje se solapan.
    /// </summary>
    /// <returns><c>true</c> si se movió la losa <paramref name="b"/>.</returns>
    private static bool AlinearPar(Losa a, Losa b, double tol)
    {
        // Ambas ancladas (filtradas por el llamador) → PosX/PosY no nulos.
        double aL = a.PosX!.Value, aT = a.PosY!.Value, aR = aL + a.Lx, aB = aT + a.Ly;
        double bL = b.PosX!.Value, bT = b.PosY!.Value, bR = bL + b.Lx, bB = bT + b.Ly;

        // --- Vecindad lado a lado: se solapan en Y → posible gap en X ---
        double solapeY = Math.Min(aB, bB) - Math.Max(aT, bT);
        if (solapeY > tol)
        {
            bool bDerecha = (bL + bR) > (aL + aR);          // centro X de B vs A
            double gap = bDerecha ? (bL - aR) : (aL - bR);
            if (Math.Abs(gap) <= tol && Math.Abs(gap) > EpsilonContacto)
            {
                b.PosX = bDerecha ? aR : (aL - b.Lx);
                return true;
            }
            return false;
        }

        // --- Vecindad apilada: se solapan en X → posible gap en Y ---
        double solapeX = Math.Min(aR, bR) - Math.Max(aL, bL);
        if (solapeX > tol)
        {
            bool bAbajo = (bT + bB) > (aT + aB);            // centro Y de B vs A
            double gap = bAbajo ? (bT - aB) : (aT - bB);
            if (Math.Abs(gap) <= tol && Math.Abs(gap) > EpsilonContacto)
            {
                b.PosY = bAbajo ? aB : (aT - b.Ly);
                return true;
            }
        }

        return false;
    }
}
