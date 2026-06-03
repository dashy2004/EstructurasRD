using System.Collections.Generic;
using LosasPlus.Calculo;
using LosasPlus.Models;
using LosasPlus.Models.Cad;

namespace LosasPlus.Transmision;

/// <summary>
/// Desglose de la carga última directa de un sistema (Sesión D). Todas las
/// cargas superficiales en t/m²; <see cref="Qmamp"/> en toneladas.
/// </summary>
/// <param name="Qmamp">Peso total de mampostería de los muros del sistema (ton).</param>
/// <param name="Qmap">Mampostería distribuida sobre el área de losa (t/m², con piso 0.10).</param>
/// <param name="Qd">Carga muerta total (t/m²) = peso propio (h·γ + uso) + Qmap.</param>
/// <param name="Ql">Carga viva (t/m²) según el uso del sistema.</param>
/// <param name="Qu">Carga última factorizada (t/m²), combinación LRFD.</param>
public readonly record struct CargaUltimaResultado(
    double Qmamp,
    double Qmap,
    double Qd,
    double Ql,
    double Qu);

/// <summary>
/// Carga última directa (Sesión D): toma la geometría real del modelo —en
/// particular los <see cref="Muro"/> dibujados en planta— y la compone con el
/// pipeline de cargas ya existente en <c>CalculoEngine</c>
/// (Qmamp → Qmap → Qd → Ql → Qu, combinación LRFD) para obtener la carga
/// última factorizada sin que el usuario teclee metros lineales de mampostería.
///
/// <para>
/// Tipo <b>puro de dominio</b> — multiplataforma y testeable. Unidades: peso de
/// mampostería en toneladas; cargas superficiales en t/m².
/// </para>
/// </summary>
public static class CargaUltimaCalculator
{
    /// <summary>Densidad de mampostería (bloque de hormigón hueco con repello), t/m³.</summary>
    public const double DensidadMamposteria = 1.8;

    /// <summary>
    /// Peso total de la mampostería (ton) a partir de los muros dibujados:
    /// <c>Σ 1.8 · Longitud · Espesor · Altura</c>. Reproduce la convención de
    /// <c>CalculoEngine.ComputeQmamp</c> pero desde la geometría real en vez de
    /// metros lineales tecleados. Una colección nula o vacía devuelve 0.
    /// </summary>
    public static double PesoMamposteria(IEnumerable<Muro>? muros)
    {
        if (muros is null) return 0;
        double total = 0;
        foreach (var muro in muros)
        {
            if (muro is null) continue;
            total += DensidadMamposteria * muro.Longitud * muro.Espesor * muro.Altura;
        }
        return total;
    }

    /// <summary>Peso de mampostería (ton) de los muros de un <paramref name="sistema"/>.</summary>
    public static double PesoMamposteria(Sistema? sistema)
        => sistema is null ? 0 : PesoMamposteria(sistema.Muros);

    /// <summary>
    /// Carga última directa de un sistema: compone el peso real de mampostería
    /// (de los muros dibujados) con el pipeline de cargas de
    /// <see cref="CalculoEngine"/> — Qmamp → Qmap → Qd → Ql → Qu (LRFD).
    ///
    /// <para>
    /// La mampostería se reparte uniformemente sobre el <paramref name="area"/>
    /// de losa del sistema (aproximación de «smearing»: el modelo de dominio aún
    /// no asigna cada muro a una franja tributaria). <paramref name="hEq"/> es el
    /// espesor equivalente para el lookup de peso propio.
    /// </para>
    /// </summary>
    public static CargaUltimaResultado Calcular(Sistema sistema, CargasGlobales cargas, double hEq, double area)
    {
        var qmamp = PesoMamposteria(sistema);
        var qmap = CalculoEngine.ComputeQmap(qmamp, area);
        var qd = CalculoEngine.ComputeQd(hEq, cargas, sistema.Uso, qmap);
        var ql = CalculoEngine.ComputeQl(sistema.Uso, cargas);
        var qu = CalculoEngine.ComputeQu(qd, ql, cargas.Factores);
        return new CargaUltimaResultado(qmamp, qmap, qd, ql, qu);
    }
}
