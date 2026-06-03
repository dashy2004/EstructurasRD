using LosasPlus.Calculo;
using Xunit;

namespace LosasPlus.Tests.Calculo;

/// <summary>
/// Tests del diseño de zapatas aisladas (ACI 318-19), en unidades SI (N, mm,
/// MPa) — espeja a <see cref="ColumnaDisenador"/>. Empieza por la presión de
/// contacto última y crece hacia punzonamiento, cortante y flexión.
/// </summary>
public class ZapataDisenadorTests
{
    [Fact]
    public void PresionContactoUltima_es_Pu_sobre_el_area()
    {
        // Pu = 1000 kN = 1e6 N sobre zapata 2000×2000 mm → 1e6/4e6 = 0.25 MPa.
        Assert.Equal(0.25, ZapataDisenador.PresionContactoUltima(puN: 1.0e6, bMm: 2000, lMm: 2000), 6);
    }

    [Fact]
    public void PerimetroCriticoPunzonamiento_es_el_de_la_columna_mas_d_a_d_medios()
    {
        // ACI 318-19 §22.6.4.1 (columna interior): b0 = 2(c1+d) + 2(c2+d).
        // c1=c2=400, d=500 → 2(900) + 2(900) = 3600 mm.
        Assert.Equal(3600.0, ZapataDisenador.PerimetroCriticoPunzonamiento(c1Mm: 400, c2Mm: 400, dMm: 500), 6);
    }

    [Fact]
    public void CortantePunzonamiento_es_Pu_menos_la_carga_dentro_del_perimetro()
    {
        // Pu=1e6 N, B=L=2000, c1=c2=400, d=500 → Vu = 1e6·(1 − 900·900/(2000·2000)) = 797500 N.
        var vu = ZapataDisenador.CortantePunzonamiento(puN: 1.0e6, bMm: 2000, lMm: 2000, c1Mm: 400, c2Mm: 400, dMm: 500);
        Assert.Equal(797500.0, vu, 1);
    }

    [Fact]
    public void ResistenciaPunzonamiento_toma_el_minimo_de_las_tres_formulas_por_phi()
    {
        // fc=28, b0=3600, d=500, beta=1, alphaS=40 → gobierna 0.33·√fc.
        // φVc = 0.75 · 0.33·√28 · 3600 · 500.
        double raiz = System.Math.Sqrt(28.0);
        double esperado = 0.75 * (0.33 * raiz * 3600.0 * 500.0);

        var phiVc = ZapataDisenador.ResistenciaPunzonamiento(fcMPa: 28, b0Mm: 3600, dMm: 500, beta: 1.0);

        Assert.Equal(esperado, phiVc, 0);
    }

    [Fact]
    public void ChequeoPunzonamiento_compone_Vu_phiVc_ratio_y_cumple()
    {
        var chk = ZapataDisenador.ChequeoPunzonamiento(
            puN: 1.0e6, bMm: 2000, lMm: 2000, c1Mm: 400, c2Mm: 400, dMm: 500, fcMPa: 28);

        var vu = ZapataDisenador.CortantePunzonamiento(1.0e6, 2000, 2000, 400, 400, 500);
        var b0 = ZapataDisenador.PerimetroCriticoPunzonamiento(400, 400, 500);
        var phiVc = ZapataDisenador.ResistenciaPunzonamiento(28, b0, 500, beta: 1.0);

        Assert.Equal(vu, chk.VuN, 1);
        Assert.Equal(phiVc, chk.PhiVcN, 0);
        Assert.Equal(vu / phiVc, chk.Ratio, 6);
        Assert.True(chk.Cumple);   // 797.5 kN ≤ ~2357 kN
    }

    [Fact]
    public void VoladizoZapata_es_medio_lado_libre()
    {
        // (B − c)/2 = (2000 − 400)/2 = 800 mm.
        Assert.Equal(800.0, ZapataDisenador.VoladizoZapata(bMm: 2000, cMm: 400), 6);
    }

    [Fact]
    public void CortanteUnidireccional_es_qu_por_la_franja_mas_alla_de_d()
    {
        // q_u=0.25 MPa, B=2000, voladizo=800, d=500 → Vu = 0.25·2000·(800−500) = 150000 N.
        var vu = ZapataDisenador.CortanteUnidireccional(puN: 1.0e6, bMm: 2000, lMm: 2000, cMm: 400, dMm: 500);
        Assert.Equal(150000.0, vu, 1);
    }

    [Fact]
    public void ResistenciaCortanteUnidireccional_es_phi_0_17_raiz_fc_b_d()
    {
        double esperado = 0.75 * 0.17 * System.Math.Sqrt(28.0) * 2000.0 * 500.0;
        Assert.Equal(esperado, ZapataDisenador.ResistenciaCortanteUnidireccional(fcMPa: 28, bMm: 2000, dMm: 500), 0);
    }

    [Fact]
    public void ChequeoCortanteUnidireccional_compone_Vu_phiVc_y_cumple()
    {
        var chk = ZapataDisenador.ChequeoCortanteUnidireccional(
            puN: 1.0e6, bMm: 2000, lMm: 2000, cMm: 400, dMm: 500, fcMPa: 28);

        Assert.Equal(150000.0, chk.VuN, 1);
        Assert.Equal(ZapataDisenador.ResistenciaCortanteUnidireccional(28, 2000, 500), chk.PhiVcN, 0);
        Assert.True(chk.Cumple);
    }
}
