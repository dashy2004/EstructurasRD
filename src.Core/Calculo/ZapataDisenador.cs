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

    /// <summary>
    /// Perímetro crítico de punzonamiento b0 (mm) de una columna <b>interior</b>,
    /// medido a <c>d/2</c> de las caras (ACI 318-19 §22.6.4.1):
    /// <c>b0 = 2(c1+d) + 2(c2+d)</c>, con <paramref name="c1Mm"/>×<paramref name="c2Mm"/>
    /// las dimensiones de la columna y <paramref name="dMm"/> el peralte efectivo.
    /// </summary>
    public static double PerimetroCriticoPunzonamiento(double c1Mm, double c2Mm, double dMm)
        => 2.0 * (c1Mm + dMm) + 2.0 * (c2Mm + dMm);
}
