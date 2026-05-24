using LosasPlus.Grillas;
using LosasPlus.Models;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests.Topologia;

/// <summary>
/// Tests del motor <see cref="GridCreationEngine.CrearVigaEntreSnaps"/>
/// (Liga B paso B3). Cobertura: longitud euclidiana correcta, rechazo
/// de payloads degenerados (longitud cero, NaN/Infinity), Id
/// autoincremental, preservación de vigas existentes.
/// </summary>
public class GridCreationVigaTests
{
    private static Nivel ConstruirNivel(double cota = 0.0) =>
        new() { Nombre = "Test", Cota = cota };

    [Fact]
    public void CrearVigaEntreSnaps_PuntosLejanos_CreaVigaConLongitudEuclidiana()
    {
        var nivel = ConstruirNivel();
        var grilla = GrillaEstructural.Default();

        // Click en (0,0) → snap a (0,0). Click en (6,0) → snap a (6,0).
        var viga = GridCreationEngine.CrearVigaEntreSnaps(
            nivel, grilla, x1: 0.0, y1: 0.0, x2: 6.0, y2: 0.0);

        Assert.NotNull(viga);
        Assert.Single(nivel.Vigas);
        Assert.Single(viga!.Tramos);
        Assert.Equal(6.0, viga.Tramos[0].Longitud, 6);
        Assert.Equal(2, viga.Apoyos.Count);
        Assert.Equal(0.0, viga.Apoyos[0].CoordenadaX);
        Assert.Equal(6.0, viga.Apoyos[1].CoordenadaX);
        Assert.Equal(TipoApoyo.Fijo, viga.Apoyos[0].Tipo);
        Assert.Equal(TipoApoyo.Fijo, viga.Apoyos[1].Tipo);
    }

    [Fact]
    public void CrearVigaEntreSnaps_LongitudCero_DevuelveNull()
    {
        var nivel = ConstruirNivel();
        var grilla = GrillaEstructural.Default();

        // P1 == P2 → longitud=0 < 1cm → rechazo silencioso.
        var viga = GridCreationEngine.CrearVigaEntreSnaps(
            nivel, grilla, 3.0, 4.0, 3.0, 4.0);

        Assert.Null(viga);
        Assert.Empty(nivel.Vigas);
    }

    [Fact]
    public void CrearVigaEntreSnaps_CoordenadasNaN_DevuelveNull()
    {
        var nivel = ConstruirNivel();
        var grilla = GrillaEstructural.Default();

        // x1 = NaN → validación de frontera rechaza ANTES del snap.
        var viga = GridCreationEngine.CrearVigaEntreSnaps(
            nivel, grilla, double.NaN, 0.0, 6.0, 0.0);

        Assert.Null(viga);
        Assert.Empty(nivel.Vigas);
    }

    [Fact]
    public void CrearVigaEntreSnaps_AsignaIdAutoincremental()
    {
        var nivel = ConstruirNivel();
        var grilla = GrillaEstructural.Default();

        var v1 = GridCreationEngine.CrearVigaEntreSnaps(nivel, grilla, 0,  0,  6,  0);
        var v2 = GridCreationEngine.CrearVigaEntreSnaps(nivel, grilla, 0,  6,  6,  6);
        var v3 = GridCreationEngine.CrearVigaEntreSnaps(nivel, grilla, 0, 12,  6, 12);

        Assert.NotNull(v1);
        Assert.NotNull(v2);
        Assert.NotNull(v3);
        Assert.Equal(1, v1!.Id);
        Assert.Equal(2, v2!.Id);
        Assert.Equal(3, v3!.Id);
        Assert.Equal(3, nivel.Vigas.Count);
    }

    [Fact]
    public void CrearVigaEntreSnaps_PreservaVigasExistentes()
    {
        var nivel = ConstruirNivel();
        nivel.Vigas.Add(new Viga { Id = 5, Nombre = "V-5 legacy" });
        nivel.Vigas.Add(new Viga { Id = 8, Nombre = "V-8 legacy" });
        var grilla = GrillaEstructural.Default();

        var v = GridCreationEngine.CrearVigaEntreSnaps(nivel, grilla, 0, 0, 6, 0);

        Assert.NotNull(v);
        Assert.Equal(3, nivel.Vigas.Count);
        Assert.Equal(9, v!.Id);   // Max(5, 8) + 1
        Assert.Contains(nivel.Vigas, x => x.Id == 5);
        Assert.Contains(nivel.Vigas, x => x.Id == 8);
    }
}
