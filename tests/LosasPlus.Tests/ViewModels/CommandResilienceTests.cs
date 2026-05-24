using System;
using LosasPlus.Columnas;
using LosasPlus.Grillas;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Servicios;
using LosasPlus.Topologia;
using LosasPlus.Vigas;
using LosasPlus.Zapatas;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests del servicio abstracto <see cref="IAutoria3DService"/> y su
/// implementación default <see cref="GridAutoria3DService"/> (Liga B
/// paso B3). Cobertura de las 4 directivas:
/// <list type="number">
///   <item>Validación defensiva — payloads degenerados rechazados sin throw.</item>
///   <item>Ghost Deletion — eliminación de IDs huérfanos no lanza, retorna false.</item>
///   <item>Servicio abstracto — interface intercambiable por mock con contador.</item>
///   <item>Consistencia M2C — borrar columna en nivel base también borra zapata pareada.</item>
/// </list>
///
/// Tests del comando MainViewModel-wired (idempotencia HashSet,
/// marshaling Dispatcher) quedan para una iteración futura con
/// refactor de testabilidad — requieren WPF Application activo.
/// </summary>
public class CommandResilienceTests
{
    // ===== Helpers de fixture =====
    private static Proyecto ConstruirProyectoConNivel(double cota = 0.0)
    {
        var proyecto = new Proyecto();
        var edif = new Edificio();
        var nivel = new Nivel { Nombre = "PB", Cota = cota };
        edif.Niveles.Add(nivel);
        proyecto.Edificios.Add(edif);
        return proyecto;
    }

    private static Nivel PrimerNivel(Proyecto p) => p.Edificios[0].Niveles[0];

    // ===========================================================================
    // 1. Happy path — CrearVigaConSnap delega al engine y crea la viga.
    // ===========================================================================
    [Fact]
    public void GridAutoria3DService_CrearVigaConSnap_DelegaAlEngine_CreaViga()
    {
        IAutoria3DService service = new GridAutoria3DService();
        var nivel = new Nivel { Nombre = "Test", Cota = 0.0 };
        var grilla = GrillaEstructural.Default();

        var viga = service.CrearVigaConSnap(nivel, grilla, 0.0, 0.0, 6.0, 0.0);

        Assert.NotNull(viga);
        Assert.Single(nivel.Vigas);
        Assert.Equal(6.0, viga!.Tramos[0].Longitud, 6);
    }

    // ===========================================================================
    // 2. Directiva 1 — coordenadas NaN rechazadas sin throw.
    // ===========================================================================
    [Fact]
    public void GridAutoria3DService_CrearVigaConSnap_PayloadCoordenadasNaN_DevuelveNull()
    {
        IAutoria3DService service = new GridAutoria3DService();
        var nivel = new Nivel { Nombre = "Test", Cota = 0.0 };
        var grilla = GrillaEstructural.Default();

        // No throw garantizado — Assert no requiere try/catch.
        var viga = service.CrearVigaConSnap(nivel, grilla, double.NaN, 0.0, 6.0, 0.0);

        Assert.Null(viga);
        Assert.Empty(nivel.Vigas);
    }

    // ===========================================================================
    // 3. Directiva 1 — longitud degenerada rechazada sin throw.
    // ===========================================================================
    [Fact]
    public void GridAutoria3DService_CrearVigaConSnap_LongitudDegenerada_DevuelveNull()
    {
        IAutoria3DService service = new GridAutoria3DService();
        var nivel = new Nivel { Nombre = "Test", Cota = 0.0 };
        var grilla = GrillaEstructural.Default();

        // P1 == P2 → longitud cero → rechazo.
        var viga = service.CrearVigaConSnap(nivel, grilla, 3.0, 4.0, 3.0, 4.0);

        Assert.Null(viga);
        Assert.Empty(nivel.Vigas);
    }

    // ===========================================================================
    // 4. Directiva 2 — Ghost Deletion: Id inexistente retorna false sin throw.
    // ===========================================================================
    [Fact]
    public void GridAutoria3DService_BorrarElemento_IdHuerfano_FalseSilencioso()
    {
        IAutoria3DService service = new GridAutoria3DService();
        var proyecto = ConstruirProyectoConNivel();
        // Proyecto sin columnas → cualquier key huérfana.

        bool removido = service.BorrarElementoPorKey(
            proyecto, new DomainKey(TipoElemento.Columna, 999));

        Assert.False(removido);
        // Sin excepciones propagadas — el test pasa si llegamos aquí.
    }

    // ===========================================================================
    // 5. Directiva 2 + inmutabilidad — Muro es no-op declarado, NO se borra.
    // ===========================================================================
    [Fact]
    public void GridAutoria3DService_BorrarElemento_TipoMuro_NoOpInmutabilidadRespetada()
    {
        IAutoria3DService service = new GridAutoria3DService();
        var proyecto = ConstruirProyectoConNivel();
        // Inserta un muro en el sistema del nivel.
        var nivel = PrimerNivel(proyecto);
        var sistema = new Sistema();
        nivel.Sistemas.Add(sistema);
        var muro = new Muro
        {
            Id          = 1,
            PuntoInicio = new PuntoCad(0, 0),
            PuntoFin    = new PuntoCad(5, 0),
            Espesor     = 0.15,
            Altura      = 3.0,
        };
        sistema.Muros.Add(muro);

        bool removido = service.BorrarElementoPorKey(
            proyecto, new DomainKey(TipoElemento.Muro, 1));

        Assert.False(removido);
        Assert.Single(sistema.Muros);   // Inmutabilidad respetada — muro intacto.
    }

    // ===========================================================================
    // 6. Consistencia M2C — borrar columna en nivel base también borra zapata.
    // ===========================================================================
    [Fact]
    public void GridAutoria3DService_BorrarElemento_Columna_TambienBorraZapataPareada()
    {
        IAutoria3DService service = new GridAutoria3DService();
        var proyecto = ConstruirProyectoConNivel(cota: 0.0);
        var nivel = PrimerNivel(proyecto);
        nivel.Columnas.Add(new Columna { Id = 7, Nombre = "C-7" });
        nivel.Zapatas.Add(new ZapataAislada { Id = 7, Nombre = "Z-7" });   // pareada por Id

        bool removido = service.BorrarElementoPorKey(
            proyecto, new DomainKey(TipoElemento.Columna, 7));

        Assert.True(removido);
        Assert.Empty(nivel.Columnas);
        Assert.Empty(nivel.Zapatas);   // Zapata pareada también removida.
    }

    // ===========================================================================
    // 7. Directiva 3 — la interface es intercambiable por un mock testeable.
    // ===========================================================================
    [Fact]
    public void IAutoria3DService_PermiteSwapMock_CountInvocacionesCorrectas()
    {
        var mock = new MockAutoria3DService();
        IAutoria3DService service = mock;   // contrato satisfecho

        var nivel = new Nivel { Nombre = "Test", Cota = 0.0 };
        var grilla = GrillaEstructural.Default();
        var proyecto = ConstruirProyectoConNivel();

        service.CrearVigaConSnap(nivel, grilla, 0, 0, 6, 0);
        service.CrearVigaConSnap(nivel, grilla, 0, 6, 6, 6);
        service.BorrarElementoPorKey(proyecto, new DomainKey(TipoElemento.Columna, 1));

        Assert.Equal(2, mock.CountCrearViga);
        Assert.Equal(1, mock.CountBorrar);
    }

    // ===== Mock testeable que confirma el contrato de la interface =====
    private sealed class MockAutoria3DService : IAutoria3DService
    {
        public int CountCrearViga { get; private set; }
        public int CountBorrar    { get; private set; }

        public Viga? CrearVigaConSnap(
            Nivel nivel, GrillaEstructural grilla,
            double x1, double y1, double x2, double y2)
        {
            CountCrearViga++;
            return null;   // mock: no muta
        }

        public bool BorrarElementoPorKey(Proyecto proyecto, DomainKey key)
        {
            CountBorrar++;
            return false;   // mock: no muta
        }
    }
}
