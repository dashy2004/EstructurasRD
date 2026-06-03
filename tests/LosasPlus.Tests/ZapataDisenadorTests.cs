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
}
