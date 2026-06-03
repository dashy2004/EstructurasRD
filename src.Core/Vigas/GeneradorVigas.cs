using System.Collections.Generic;
using LosasPlus.Models;
using LosasPlus.Transmision;

namespace LosasPlus.Vigas;

/// <summary>
/// Genera <see cref="Viga"/> ya cargadas a partir de la geometría del edificio
/// (Sesión E), para que el editor de vigas muestre sus diagramas M/V/δ y su
/// sección sin que el usuario las construya a mano. Es el puente que faltaba
/// entre el reparto de cargas losa→viga (<c>RepartoCargaLosa</c>) y el motor
/// analítico (<c>VigaContinuaEngine</c>).
///
/// <para>Tipo <b>puro de dominio</b> — sin dependencias de UI, testeable.</para>
/// </summary>
public static class GeneradorVigas
{
    /// <summary>Conversión de tonelada-fuerza a kilonewton (1 tonf = 9.80665 kN).</summary>
    public const double TonF_a_KN = 9.80665;

    /// <summary>
    /// Construye una viga simplemente apoyada de un solo tramo: dos apoyos
    /// <see cref="TipoApoyo.Fijo"/> en los extremos y una carga
    /// <see cref="TipoCargaElemento.Distribuida"/> uniforme sobre todo el tramo.
    /// </summary>
    /// <param name="longitud">Luz de la viga, en metros.</param>
    /// <param name="cargaDistribuida">Carga lineal uniforme (kN/m, unidad del motor de vigas).</param>
    /// <param name="codigoCaso">Caso de carga al que se vincula la carga (p. ej. «D», «L»).</param>
    public static Viga VigaSimplementeApoyada(double longitud, double cargaDistribuida, string codigoCaso)
    {
        var viga = new Viga();

        var tramo = new TramoViga { Longitud = longitud };
        tramo.Cargas.Add(new CargaElemento
        {
            Tipo = TipoCargaElemento.Distribuida,
            Magnitud = cargaDistribuida,
            CodigoCaso = codigoCaso,
        });
        viga.Tramos.Add(tramo);

        viga.Apoyos.Add(new ApoyoViga { CoordenadaX = 0.0, Tipo = TipoApoyo.Fijo });
        viga.Apoyos.Add(new ApoyoViga { CoordenadaX = longitud, Tipo = TipoApoyo.Fijo });

        return viga;
    }

    /// <summary>
    /// Genera las cuatro vigas de apoyo de un paño de losa, cargadas con la carga
    /// tributaria que cada borde recibe por áreas tributarias
    /// (<see cref="RepartoCargaLosa"/>): dos vigas de la luz corta y dos de la luz
    /// larga. La carga superficial de la losa (<see cref="Losa.Carga"/>, en
    /// ton/m²) se reparte a línea (ton/m) y se convierte a kN/m para el motor de
    /// vigas.
    ///
    /// <para>
    /// Aproximación: la carga triangular/trapezoidal real se modela como la carga
    /// <b>uniforme equivalente</b> que conserva la fuerza total (el modelo de
    /// dominio sólo soporta cargas distribuidas uniformes). Una losa con
    /// dimensiones o carga no positivas devuelve una lista vacía.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Viga> VigasDeLosa(Losa losa, string codigoCaso = "D")
    {
        var vigas = new List<Viga>();
        if (losa is null) return vigas;

        var reparto = RepartoCargaLosa.Calcular(losa);
        if (reparto.CargaTotal <= 0) return vigas;

        double wCorto = reparto.BordeCorto.LineaUniformeEquivalente * TonF_a_KN;
        double wLargo = reparto.BordeLargo.LineaUniformeEquivalente * TonF_a_KN;

        vigas.Add(VigaSimplementeApoyada(reparto.LadoCorto, wCorto, codigoCaso));
        vigas.Add(VigaSimplementeApoyada(reparto.LadoCorto, wCorto, codigoCaso));
        vigas.Add(VigaSimplementeApoyada(reparto.LadoLargo, wLargo, codigoCaso));
        vigas.Add(VigaSimplementeApoyada(reparto.LadoLargo, wLargo, codigoCaso));
        return vigas;
    }
}
