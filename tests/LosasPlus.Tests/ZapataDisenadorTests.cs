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
}
