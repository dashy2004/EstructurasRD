using System;
using LosasPlus.Models.Cad;

namespace LosasPlus.Services;

/// <summary>
/// Geometría pura del redimensionado de muros por asas de extremo (UI1.10). Sin
/// estado, sin I/O, sin Avalonia — testeable en aislamiento. Hermano de
/// <c>BordesPlantaService</c> (UI1.8).
/// </summary>
public static class MurosPlantaService
{
    /// <summary>
    /// Extremo del muro bajo <paramref name="punto"/> (metros): <c>0</c> =
    /// <see cref="Muro.PuntoInicio"/>, <c>1</c> = <see cref="Muro.PuntoFin"/>, o
    /// <c>null</c>. En empate gana el más cercano. <paramref name="tol"/> es el
    /// radio de captura en metros.
    /// </summary>
    public static int? AsaExtremo(Muro muro, PuntoM punto, double tol)
    {
        double di = Dist2(punto.X, punto.Y, muro.PuntoInicio.X, muro.PuntoInicio.Y);
        double df = Dist2(punto.X, punto.Y, muro.PuntoFin.X, muro.PuntoFin.Y);
        double tol2 = tol * tol;
        bool hi = di <= tol2, hf = df <= tol2;
        if (hi && hf) return di <= df ? 0 : 1;
        if (hi) return 0;
        if (hf) return 1;
        return null;
    }

    /// <summary>
    /// Modo libre: nueva posición del extremo arrastrado = <paramref name="cursor"/>,
    /// salvo que su distancia a <paramref name="fijo"/> sea menor que
    /// <paramref name="minLen"/>, en cuyo caso se clampa a esa longitud en la
    /// dirección <c>fijo → cursor</c>. Si el cursor coincide con el extremo fijo
    /// (sin dirección), se empuja <paramref name="minLen"/> en +X.
    /// </summary>
    public static PuntoCad MoverExtremoLibre(PuntoCad fijo, PuntoM cursor, double minLen)
    {
        double dx = cursor.X - fijo.X, dy = cursor.Y - fijo.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len >= minLen) return new PuntoCad(cursor.X, cursor.Y);
        if (len < 1e-9) return new PuntoCad(fijo.X + minLen, fijo.Y);
        double s = minLen / len;
        return new PuntoCad(fijo.X + dx * s, fijo.Y + dy * s);
    }

    private static double Dist2(double ax, double ay, double bx, double by)
    {
        double dx = ax - bx, dy = ay - by;
        return dx * dx + dy * dy;
    }
}
