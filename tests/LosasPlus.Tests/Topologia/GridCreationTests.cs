using System.Linq;
using LosasPlus.Columnas;
using LosasPlus.Grillas;
using LosasPlus.Models;
using LosasPlus.Topologia;
using Xunit;

namespace LosasPlus.Tests.Topologia;

/// <summary>
/// Tests de mutación del dominio vía <see cref="GridCreationEngine"/>
/// + integración con <see cref="GrafoProyectadoBuilder"/> (Módulo 2
/// Parte C Fase 3D-II). Cobertura: regla zapata-base, ID
/// autoincremental, snap determinista, fallback de la grilla
/// artificial.
/// </summary>
public class GridCreationTests
{
    /// <summary>
    /// Helper: construye un Nivel limpio con la cota dada. Sin
    /// dependencias del shell.
    /// </summary>
    private static Nivel ConstruirNivel(double cota) => new() { Nombre = "Test", Cota = cota };

    [Fact]
    public void CrearColumna3D_EnNivelBase_InyectaColumnaYZapataSimultaneamente()
    {
        var nivel = ConstruirNivel(0.0);
        var grilla = GrillaEstructural.Default();

        // Click en (6.0, 6.0) — intersección B-2 exacta.
        var (col, zap) = GridCreationEngine.CrearColumnaConSnap(
            nivel, grilla, clickX: 6.0, clickY: 6.0);

        Assert.Single(nivel.Columnas);
        Assert.Single(nivel.Zapatas);
        Assert.NotNull(zap);
        Assert.Equal(col.PosX, zap!.PosX);
        Assert.Equal(col.PosY, zap.PosY);
        Assert.Equal(6.0, col.PosX);
        Assert.Equal(6.0, col.PosY);
    }

    [Fact]
    public void CrearColumna3D_EnNivelSuperior_InyectaSoloColumnaSinZapata()
    {
        // Nivel a 3.0 m: regla de cota base no aplica → sin zapata.
        var nivel = ConstruirNivel(3.0);
        var grilla = GrillaEstructural.Default();

        var (col, zap) = GridCreationEngine.CrearColumnaConSnap(
            nivel, grilla, clickX: 6.0, clickY: 6.0);

        Assert.Single(nivel.Columnas);
        Assert.Empty(nivel.Zapatas);
        Assert.Null(zap);
        Assert.NotNull(col);
    }

    [Fact]
    public void CrearColumna3D_AsignaIdUnicoAutoincremental()
    {
        var nivel = ConstruirNivel(0.0);
        var grilla = GrillaEstructural.Default();

        var (c1, _) = GridCreationEngine.CrearColumnaConSnap(nivel, grilla, 0.0, 0.0);
        var (c2, _) = GridCreationEngine.CrearColumnaConSnap(nivel, grilla, 6.0, 0.0);
        var (c3, _) = GridCreationEngine.CrearColumnaConSnap(nivel, grilla, 12.0, 0.0);

        Assert.Equal(1, c1.Id);
        Assert.Equal(2, c2.Id);
        Assert.Equal(3, c3.Id);
        // Y no duplicados:
        Assert.Equal(3, nivel.Columnas.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void CrearColumna3D_AplicaSnapDeFormaDeterminista()
    {
        // Click en (0.3, 5.85) — distancia a (0, 6) ≈ 0.335 < 0.80 → snap.
        var nivel = ConstruirNivel(0.0);
        var grilla = GrillaEstructural.Default();

        var (col, _) = GridCreationEngine.CrearColumnaConSnap(
            nivel, grilla, clickX: 0.3, clickY: 5.85);

        Assert.Equal(0.0, col.PosX);
        Assert.Equal(6.0, col.PosY);
    }

    [Fact]
    public void CrearColumna3D_FueraDelRadioSnap_GuardaCoordenadasLibres()
    {
        // Click en (2.5, 3.5) — lejos de cualquier intersección → libre.
        var nivel = ConstruirNivel(0.0);
        var grilla = GrillaEstructural.Default();

        var (col, _) = GridCreationEngine.CrearColumnaConSnap(
            nivel, grilla, clickX: 2.5, clickY: 3.5);

        Assert.Equal(2.5, col.PosX);
        Assert.Equal(3.5, col.PosY);
    }

    [Fact]
    public void CrearColumna3D_NivelBase_ZapataPareadaTieneMismoId()
    {
        var nivel = ConstruirNivel(0.0);
        var grilla = GrillaEstructural.Default();

        var (col, zap) = GridCreationEngine.CrearColumnaConSnap(
            nivel, grilla, 6.0, 6.0);

        Assert.NotNull(zap);
        Assert.Equal(col.Id, zap!.Id);
    }

    [Fact]
    public void CrearColumna3D_PreservaColumnasExistentes()
    {
        var nivel = ConstruirNivel(3.0);
        nivel.Columnas.Add(new Columna { Id = 5, Nombre = "C-5 legacy" });
        nivel.Columnas.Add(new Columna { Id = 8, Nombre = "C-8 legacy" });
        var grilla = GrillaEstructural.Default();

        var (col, _) = GridCreationEngine.CrearColumnaConSnap(nivel, grilla, 6.0, 0.0);

        Assert.Equal(3, nivel.Columnas.Count);
        Assert.Equal(9, col.Id);   // Max(5, 8) + 1
        Assert.Contains(nivel.Columnas, c => c.Id == 5);
        Assert.Contains(nivel.Columnas, c => c.Id == 8);
    }

    [Fact]
    public void GrafoProyectadoBuilder_RespetaPosXPosYDeColumna()
    {
        // Proyecto con una columna en (10, 20) explícitas — el grafo
        // proyectado debe materializar nodos base/corona en esa
        // posición, NO en la grilla artificial.
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        var nivel = proyecto.Edificios[0].Niveles[0];
        nivel.Columnas.Add(new Columna
        {
            Id     = 1,
            Nombre = "C-coords",
            Altura = 3.0,
            PosX   = 10.0,
            PosY   = 20.0,
        });

        var grafo = GrafoProyectadoBuilder.Construir(proyecto);

        // Hay al menos 2 nodos asociados con la columna (base + corona).
        // Buscamos al menos uno en (10, 20, *).
        bool encontroXY = grafo.Nodos.Any(n =>
            System.Math.Abs(n.Posicion.X - 10.0f) < 1e-3f &&
            System.Math.Abs(n.Posicion.Y - 20.0f) < 1e-3f);
        Assert.True(encontroXY,
            "El grafo proyectado debe respetar las coordenadas PosX/PosY explícitas de la columna.");
    }

    [Fact]
    public void GrafoProyectadoBuilder_SinPosXPosY_UsaGrillaArtificial()
    {
        // Columna SIN PosX/PosY → el builder cae al fallback de la
        // grilla artificial (PosicionEnGrillaArtificial(Id=1) → (0, 0)
        // según la convención del builder).
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        var nivel = proyecto.Edificios[0].Niveles[0];
        nivel.Columnas.Add(new Columna
        {
            Id     = 1,
            Nombre = "C-grilla artificial",
            Altura = 3.0,
            // PosX/PosY = null → fallback.
        });

        var grafo = GrafoProyectadoBuilder.Construir(proyecto);

        // GrafoProyectadoBuilder.PosicionEnGrillaArtificial(1) → (0, 0)
        // según el cálculo `(idGrilla - 1) % 8 * 6 = 0` y `(idGrilla - 1) / 8 * 6 = 0`.
        bool encontroOrigen = grafo.Nodos.Any(n =>
            System.Math.Abs(n.Posicion.X - 0.0f) < 1e-3f &&
            System.Math.Abs(n.Posicion.Y - 0.0f) < 1e-3f);
        Assert.True(encontroOrigen,
            "Sin PosX/PosY el builder debe usar la grilla artificial — Id=1 cae en (0, 0).");
    }
}
