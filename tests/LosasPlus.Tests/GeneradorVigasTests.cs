using System.Linq;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
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

    private static Nivel NivelConUnaLosa()
    {
        var nivel = new Nivel { Cota = 0, Nombre = "N1" };
        var sistema = new Sistema();
        sistema.Losas.Add(new Losa { Lx = 4.0, Ly = 5.0, Carga = 1.0 });
        nivel.Sistemas.Add(sistema);
        return nivel;
    }

    [Fact]
    public void MaterializarVigas_agrega_las_vigas_generadas_al_nivel()
    {
        var nivel = NivelConUnaLosa();

        var generadas = GeneradorVigas.MaterializarVigas(nivel);

        Assert.Equal(4, generadas.Count);
        Assert.Equal(4, nivel.Vigas.Count);   // se incorporaron al nivel
    }

    [Fact]
    public void MaterializarVigas_es_idempotente_y_preserva_las_vigas_manuales()
    {
        var nivel = NivelConUnaLosa();
        var manual = new Viga { Nombre = "Mi viga manual" };
        nivel.Vigas.Add(manual);

        GeneradorVigas.MaterializarVigas(nivel);
        GeneradorVigas.MaterializarVigas(nivel);   // segunda vez: no debe duplicar

        // 1 manual + 4 generadas (no 8): regenerar limpia sólo las auto-generadas.
        Assert.Equal(5, nivel.Vigas.Count);
        Assert.Contains(manual, nivel.Vigas);
    }

    [Fact]
    public void MaterializarVigas_edificio_recorre_todos_los_niveles()
    {
        var edificio = new Edificio();
        var niveles = new[] { NivelConUnaLosa(), NivelConUnaLosa() };
        foreach (var n in niveles) edificio.Niveles.Add(n);

        var generadas = GeneradorVigas.MaterializarVigas(edificio);

        // 4 vigas por losa × 2 niveles = 8; cada nivel recibe sus 4.
        Assert.Equal(8, generadas.Count);
        Assert.All(niveles, n => Assert.Equal(4, n.Vigas.Count));
    }

    // ----- Viga continua (multi-tramo) — WS1-B -----

    [Fact]
    public void VigaContinua_arma_tramos_apoyos_acumulados_y_carga_por_tramo()
    {
        var luces = new[] { 4.0, 5.0, 4.0 };
        var cargas = new[] { 10.0, 12.0, 10.0 };

        var viga = GeneradorVigas.VigaContinua(luces, cargas, "D");

        // 3 tramos con su luz y su carga distribuida.
        Assert.Equal(3, viga.Tramos.Count);
        Assert.Equal(5.0, viga.Tramos[1].Longitud, 6);
        var cargaTramo2 = Assert.Single(viga.Tramos[1].Cargas);
        Assert.Equal(TipoCargaElemento.Distribuida, cargaTramo2.Tipo);
        Assert.Equal(12.0, cargaTramo2.Magnitud, 6);
        Assert.Equal("D", cargaTramo2.CodigoCaso);

        // 4 apoyos fijos en posiciones acumuladas 0, 4, 9, 13.
        Assert.Equal(4, viga.Apoyos.Count);
        Assert.Equal(new[] { 0.0, 4.0, 9.0, 13.0 },
            viga.Apoyos.Select(a => a.CoordenadaX).ToArray());
        Assert.All(viga.Apoyos, a => Assert.Equal(TipoApoyo.Fijo, a.Tipo));
    }

    [Fact]
    public void VigaContinuaDeLosas_arma_la_continua_desde_una_fila_de_panos()
    {
        // 2 paños 4×5 (q=1 t/m²). Viga a lo largo de Lx (=4). El borde de longitud 4
        // es el corto → línea equiv = q·a/4 = 1·4/4 = 1.0 t/m → 9.80665 kN/m.
        var losas = new List<Losa>
        {
            new Losa { Lx = 4, Ly = 5, Carga = 1.0 },
            new Losa { Lx = 4, Ly = 5, Carga = 1.0 },
        };

        var viga = GeneradorVigas.VigaContinuaDeLosas(losas, "D");

        Assert.Equal(2, viga.Tramos.Count);
        Assert.All(viga.Tramos, t => Assert.Equal(4.0, t.Longitud, 6));
        Assert.All(viga.Tramos, t => Assert.Equal(1.0 * 9.80665, t.Cargas[0].Magnitud, 4));
        Assert.Equal(new[] { 0.0, 4.0, 8.0 }, viga.Apoyos.Select(a => a.CoordenadaX).ToArray());
    }

    [Fact]
    public void VigaContinuaDelEje_toma_las_losas_en_seccion_ordenadas_por_proyeccion()
    {
        // Eje horizontal en y=0. Dos paños en línea (centros x=6 y x=2) dados FUERA
        // de orden, más uno fuera del eje → la continua debe tener sólo los 2, ordenados.
        var eje = new EjeEstructural
        {
            PuntoInicio = new PuntoCad(0, 0),
            PuntoFin = new PuntoCad(10, 0),
        };
        var losas = new List<Losa>
        {
            new Losa { Id = 2, CoordenadaX = 4, CoordenadaY = -2, Lx = 4, Ly = 4, Carga = 1.0 }, // centro (6,0)
            new Losa { Id = 1, CoordenadaX = 0, CoordenadaY = -2, Lx = 4, Ly = 4, Carga = 1.0 }, // centro (2,0)
            new Losa { Id = 9, CoordenadaX = 0, CoordenadaY = 20, Lx = 4, Ly = 4, Carga = 1.0 }, // fuera del eje
        };

        var viga = GeneradorVigas.VigaContinuaDelEje(eje, losas, tolerancia: 0.1, codigoCaso: "D");

        Assert.Equal(2, viga.Tramos.Count);
        Assert.Equal(new[] { 0.0, 4.0, 8.0 }, viga.Apoyos.Select(a => a.CoordenadaX).ToArray());
    }
}
