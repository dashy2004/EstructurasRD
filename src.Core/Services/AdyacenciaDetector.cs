using System;
using System.Collections.Generic;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Una adyacencia geométrica detectada entre dos losas que aún <b>no</b> está
/// declarada como <see cref="BordeAdic"/>.
/// </summary>
/// <param name="BI">Id de la losa I — la izquierda si <paramref name="EsBordeX"/>, la de arriba si no.</param>
/// <param name="BJ">Id de la losa J — la derecha si <paramref name="EsBordeX"/>, la de abajo si no.</param>
/// <param name="EsBordeX">
/// <c>true</c> → la adyacencia va a <c>Sistema.BordesX</c> (las losas comparten
/// un borde vertical); <c>false</c> → <c>Sistema.BordesY</c> (borde horizontal).
/// </param>
/// <param name="MidX">X del punto medio del borde compartido, en metros de lienzo.</param>
/// <param name="MidY">Y del punto medio del borde compartido, en metros de lienzo.</param>
public readonly record struct AdyacenciaCandidata(
    int BI, int BJ, bool EsBordeX, double MidX, double MidY);

/// <summary>
/// Detector de adyacencias estructurales del editor CAD (Fase 4 del
/// <c>PLAN_CAD_V1.md</c>). Dado el estado de un <see cref="Sistema"/>, identifica
/// pares de losas que se tocan o solapan en un borde y cuya continuidad aún no
/// está declarada en <see cref="Sistema.BordesX"/> / <see cref="Sistema.BordesY"/>.
///
/// <para>
/// Lógica puramente matemática, sin dependencias de UI → testeable de forma
/// exhaustiva. Sólo considera losas <b>ancladas</b>
/// (<see cref="Losa.TienePosicionExplicita"/>): una losa flotante no tiene una
/// posición autoral sobre el lienzo —la infiere el solver—, así que detectar
/// adyacencias entre posiciones derivadas sería circular.
/// </para>
/// </summary>
public static class AdyacenciaDetector
{
    /// <summary>Tolerancia por defecto para considerar dos bordes "en contacto", en metros.</summary>
    public const double ToleranciaPorDefecto = 0.05;

    /// <summary>
    /// Devuelve los pares de losas ancladas adyacentes en un borde cuya
    /// adyacencia aún no figura en <c>BordesX</c> ni <c>BordesY</c>.
    /// </summary>
    public static List<AdyacenciaCandidata> Detectar(
        Sistema sistema, double tolerancia = ToleranciaPorDefecto)
    {
        var resultado = new List<AdyacenciaCandidata>();
        if (sistema is null) return resultado;

        // Pares ya conectados — clave no ordenada (min, max), de BordesX ∪ BordesY.
        var conectados = new HashSet<(int, int)>();
        foreach (var b in sistema.BordesX) conectados.Add(ClavePar(b.BI, b.BJ));
        foreach (var b in sistema.BordesY) conectados.Add(ClavePar(b.BI, b.BJ));

        // Sólo las losas con posición explícita en el lienzo.
        var ancladas = new List<Losa>();
        foreach (var l in sistema.Losas)
            if (l.TienePosicionExplicita) ancladas.Add(l);

        for (int i = 0; i < ancladas.Count; i++)
            for (int j = i + 1; j < ancladas.Count; j++)
            {
                var a = ancladas[i];
                var b = ancladas[j];
                if (conectados.Contains(ClavePar(a.Id, b.Id))) continue;
                if (TryAdyacencia(a, b, tolerancia, out var cand))
                    resultado.Add(cand);
            }

        return resultado;
    }

    /// <summary>Clave no ordenada de un par de Ids (para comparar ignorando el sentido).</summary>
    private static (int, int) ClavePar(int x, int y) => x <= y ? (x, y) : (y, x);

    /// <summary>
    /// Determina si dos losas ancladas comparten un borde —horizontal o
    /// vertical— dentro de <paramref name="tol"/>. Dos rectángulos comparten a
    /// lo sumo un borde, así que a lo sumo una de las 4 pruebas da positivo.
    /// </summary>
    private static bool TryAdyacencia(Losa a, Losa b, double tol, out AdyacenciaCandidata cand)
    {
        cand = default;

        // Ambas losas son ancladas (filtradas por el llamador) → PosX/PosY no nulos.
        double aL = a.PosX!.Value, aT = a.PosY!.Value;
        double aR = aL + a.Lx,     aB = aT + a.Ly;
        double bL = b.PosX!.Value, bT = b.PosY!.Value;
        double bR = bL + b.Lx,     bB = bT + b.Ly;

        // --- Bordes verticales (adyacencia X) — el segmento compartido es en Y ---
        double solapeY = Math.Min(aB, bB) - Math.Max(aT, bT);
        if (solapeY > tol)
        {
            double midY = (Math.Max(aT, bT) + Math.Min(aB, bB)) / 2.0;
            if (Math.Abs(aR - bL) <= tol)        // a a la izquierda de b
            {
                cand = new AdyacenciaCandidata(a.Id, b.Id, true, (aR + bL) / 2.0, midY);
                return true;
            }
            if (Math.Abs(bR - aL) <= tol)        // b a la izquierda de a
            {
                cand = new AdyacenciaCandidata(b.Id, a.Id, true, (bR + aL) / 2.0, midY);
                return true;
            }
        }

        // --- Bordes horizontales (adyacencia Y) — el segmento compartido es en X ---
        double solapeX = Math.Min(aR, bR) - Math.Max(aL, bL);
        if (solapeX > tol)
        {
            double midX = (Math.Max(aL, bL) + Math.Min(aR, bR)) / 2.0;
            if (Math.Abs(aB - bT) <= tol)        // a arriba de b
            {
                cand = new AdyacenciaCandidata(a.Id, b.Id, false, midX, (aB + bT) / 2.0);
                return true;
            }
            if (Math.Abs(bB - aT) <= tol)        // b arriba de a
            {
                cand = new AdyacenciaCandidata(b.Id, a.Id, false, midX, (bB + aT) / 2.0);
                return true;
            }
        }

        return false;
    }
}
