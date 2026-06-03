using System.Linq;
using LosasPlus.Models;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del generador de vigas (Sesión E): materializa <see cref="Viga"/>
/// cargadas a partir de la geometría, para que el editor muestre sus diagramas
/// M/V/δ sin que el usuario tenga que construirlas a mano.
/// </summary>
public class GeneradorVigasTests
{
    [Fact]
    public void VigaSimplementeApoyada_un_tramo_dos_apoyos_fijos_y_carga_distribuida()
    {
        var viga = GeneradorVigas.VigaSimplementeApoyada(longitud: 5.0, cargaDistribuida: 3.2, codigoCaso: "D");

        // Un solo tramo de la longitud pedida.
        Assert.Single(viga.Tramos);
        Assert.Equal(5.0, viga.Tramos[0].Longitud, 6);

        // Dos apoyos fijos en los extremos (simplemente apoyada).
        Assert.Equal(2, viga.Apoyos.Count);
        Assert.Equal(0.0, viga.Apoyos[0].CoordenadaX, 6);
        Assert.Equal(5.0, viga.Apoyos[1].CoordenadaX, 6);
        Assert.All(viga.Apoyos, a => Assert.Equal(TipoApoyo.Fijo, a.Tipo));

        // Una carga distribuida uniforme con la magnitud y el caso dados.
        var carga = Assert.Single(viga.Tramos[0].Cargas);
        Assert.Equal(TipoCargaElemento.Distribuida, carga.Tipo);
        Assert.Equal(3.2, carga.Magnitud, 6);
        Assert.Equal("D", carga.CodigoCaso);
    }

    [Fact]
    public void VigasDeLosa_genera_cuatro_vigas_con_carga_tributaria_en_kN_por_m()
    {
        // Paño 4×5, q = 1.0 ton/m². Reparto por áreas tributarias:
        //   borde corto (L=4): w = q·a/4·... → línea equiv = 1.0 ton/m
        //   borde largo (L=5): w línea equiv = 1.2 ton/m
        // Conversión a kN/m: × 9.80665 (1 tonf = 9.80665 kN).
        var losa = new Losa { Lx = 4.0, Ly = 5.0, Carga = 1.0 };

        var vigas = GeneradorVigas.VigasDeLosa(losa, "D");

        Assert.Equal(4, vigas.Count);

        var cortas = vigas.Where(v => v.LongitudTotal < 4.5).ToList();
        var largas = vigas.Where(v => v.LongitudTotal >= 4.5).ToList();
        Assert.Equal(2, cortas.Count);
        Assert.Equal(2, largas.Count);

        Assert.All(cortas, v =>
        {
            Assert.Equal(4.0, v.LongitudTotal, 6);
            Assert.Equal(1.0 * 9.80665, v.Tramos[0].Cargas[0].Magnitud, 4);
        });
        Assert.All(largas, v =>
        {
            Assert.Equal(5.0, v.LongitudTotal, 6);
            Assert.Equal(1.2 * 9.80665, v.Tramos[0].Cargas[0].Magnitud, 4);
        });
    }
}
