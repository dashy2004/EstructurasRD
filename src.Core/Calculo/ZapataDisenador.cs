using System;

namespace LosasPlus.Calculo;

/// <summary>
/// Diseño de <b>zapatas aisladas</b> de hormigón (ACI 318-19), en unidades SI
/// (N, mm, MPa) — espeja y complementa a <see cref="ColumnaDisenador"/> y al
/// predimensionado por presión admisible (<c>PredimZapata</c>). Cubre la presión
/// de contacto última y, progresivamente, punzonamiento (§22.6), cortante
/// unidireccional (§22.5) y flexión (§13.3). Funciones puras, testeables headless.
/// </summary>
public static class ZapataDisenador
{
    /// <summary>
    /// Presión de contacto <b>última</b> q_u (MPa) bajo una zapata: la carga
    /// axial factorizada <paramref name="puN"/> (N) repartida sobre el área de
    /// contacto <paramref name="bMm"/>×<paramref name="lMm"/> (mm). Área no
    /// positiva devuelve 0.
    /// </summary>
    public static double PresionContactoUltima(double puN, double bMm, double lMm)
    {
        double area = bMm * lMm;
        return area > 0 ? puN / area : 0.0;
    }
}
