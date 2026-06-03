using System.Linq;
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
}
