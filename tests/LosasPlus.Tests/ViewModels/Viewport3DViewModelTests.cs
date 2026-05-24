using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Topologia;
using LosasPlus.ViewModels.Viewport3D;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests del <see cref="Viewport3DViewModel"/> (Fase 3D-I1 del Plan
/// Maestro de Expansión 3D). Validan el ciclo de vida del presentador 3D
/// (instanciación, regeneración con proyecto vacío, dispose) sin requerir
/// un <c>Application.Current</c> de WPF activo — el VM es tolerante al
/// ambiente headless de xUnit gracias a la guarda en
/// <see cref="SyncEscenaService"/> que omite el swap si no hay dispatcher.
/// </summary>
public class Viewport3DViewModelTests
{
    [Fact]
    public void Instanciacion_Limpia_Establece_Valores_Por_Defecto()
    {
        using var vm = new Viewport3DViewModel();

        Assert.NotNull(vm.EffectsManager);
        Assert.NotNull(vm.Camera);
        Assert.NotNull(vm.ItemsEscena3D);
        Assert.Empty(vm.ItemsEscena3D);
        Assert.False(vm.CargandoEscena);
    }

    [Fact]
    public async Task RegenerarEscenaAsync_Con_Proyecto_Vacio_No_Lanza_Excepcion()
    {
        using var vm = new Viewport3DViewModel();
        var proyecto = new Proyecto();   // sin edificios, sin elementos

        // El método debe completar sin excepción y dejar la escena vacía.
        await vm.RegenerarEscenaAsync(proyecto);

        Assert.Empty(vm.ItemsEscena3D);
        Assert.False(vm.CargandoEscena,
            "CargandoEscena debe quedar en false tras el finally del método.");
    }

    [Fact]
    public async Task RegenerarEscenaAsync_Con_Proyecto_Nulo_Es_NoOp_Seguro()
    {
        using var vm = new Viewport3DViewModel();

        // Caso defensivo: el shell durante construcción puede llamar antes
        // de tener el Proyecto instanciado. El VM debe tolerarlo.
        await vm.RegenerarEscenaAsync(null);

        Assert.Empty(vm.ItemsEscena3D);
        Assert.False(vm.CargandoEscena);
    }

    [Fact]
    public void Dispose_Libera_Recursos_Sin_Lanzar_Excepcion()
    {
        var vm = new Viewport3DViewModel();

        // Una llamada y luego otra para verificar idempotencia (defensa
        // contra doble cierre del Window).
        vm.Dispose();
        vm.Dispose();

        // Tras dispose, la colección queda vacía (referencias soltadas).
        Assert.Empty(vm.ItemsEscena3D);
    }

    [Fact]
    public async Task RegenerarEscenaAsync_Despues_De_Dispose_Es_NoOp()
    {
        var vm = new Viewport3DViewModel();
        vm.Dispose();

        // Llamadas posteriores a Dispose deben ser inocuas en lugar de
        // lanzar ObjectDisposedException — el shell podría disparar el
        // setter de ModoActivo durante el cierre de la ventana.
        await vm.RegenerarEscenaAsync(new Proyecto());

        Assert.Empty(vm.ItemsEscena3D);
    }

    // ===================================================================
    // GEOMETRÍA DE EXTRUSIONES SÓLIDAS (Fase 3D-I4 — Parte C)
    // Acceden a internals de LosasPlus vía InternalsVisibleTo configurado
    // en el csproj del ensamblado de UI.
    // ===================================================================

    [Fact]
    public void ConstruirPrismaMuro_GeneraDoceTriangulosYConteoDeVerticesCorrecto()
    {
        // Muro de control 4 m × 0.20 m × 3 m a cota 0.
        var muro = new Muro
        {
            Id          = 7,
            PuntoInicio = new PuntoCad(0.0, 0.0),
            PuntoFin    = new PuntoCad(4.0, 0.0),
            Espesor     = 0.20,
            Altura      = 3.0,
        };

        var mesh = SyncEscenaService.ConstruirPrismaMuro(muro, zBase: 0.0);

        Assert.NotNull(mesh);
        Assert.Equal(8, mesh.Positions!.Count);          // 4 base + 4 corona
        Assert.Equal(8, mesh.Normals!.Count);
        Assert.Equal(36, mesh.TriangleIndices!.Count);   // 12 triángulos × 3 vértices

        // Las cotas: 4 vértices en Z=0 (base) y 4 vértices en Z=3 (corona).
        int countBase = 0, countTop = 0;
        foreach (var v in mesh.Positions)
        {
            if (Math.Abs(v.Z - 0.0f) < 1e-4f)      countBase++;
            else if (Math.Abs(v.Z - 3.0f) < 1e-4f) countTop++;
        }
        Assert.Equal(4, countBase);
        Assert.Equal(4, countTop);
    }

    [Fact]
    public void ConstruirPrisma_WindingOrderEsContrareloj_Exterior()
    {
        // Columna vertical 3 m × 0.30 × 0.30 desde origen hasta (0,0,3).
        var posI = new Vector3(0f, 0f, 0f);
        var posJ = new Vector3(0f, 0f, 3f);
        var ejes = EjesLocalesCSI.Calcular(posI, posJ);

        var mesh = SyncEscenaService.ConstruirCajaOrientada(
            posI, posJ, ejes, anchoB: 0.30f, peralteH: 0.30f);

        Assert.NotNull(mesh);
        var positions = mesh.Positions!;
        var indices   = mesh.TriangleIndices!;
        Assert.NotEmpty(positions);
        Assert.True(indices.Count >= 36, "La caja debe tener al menos 12 triángulos.");

        // Centro geométrico del volumen (promedio de los 8 vértices).
        var centro = Vector3.Zero;
        foreach (var v in positions) centro += v;
        centro /= positions.Count;

        // Para CADA triángulo: producto cruz de aristas (v1-v0) × (v2-v0)
        // debe apuntar EN LA MISMA DIRECCIÓN que (centroTriángulo - centroVolumen)
        // — eso confirma que la normal CCW apunta hacia el exterior del sólido.
        int triCount = indices.Count / 3;
        for (int t = 0; t < triCount; t++)
        {
            int i0 = indices[t * 3 + 0];
            int i1 = indices[t * 3 + 1];
            int i2 = indices[t * 3 + 2];
            var v0 = positions[i0];
            var v1 = positions[i1];
            var v2 = positions[i2];
            var normalCCW = Vector3.Cross(v1 - v0, v2 - v0);
            var centroTri = (v0 + v1 + v2) / 3f;
            var dirExterior = centroTri - centro;
            // dot > 0 ⇔ normal apunta hacia afuera (mismo semiespacio que el radial).
            Assert.True(Vector3.Dot(normalCCW, dirExterior) > 0f,
                $"Triángulo {t} viola el winding CCW exterior — Backface Culling lo haría invisible.");
        }
    }

    [Fact]
    public void CintaDeMomentos_GeneraMallaContinua_ConTagNulo()
    {
        // Viga horizontal de 6 m a lo largo de +X, a Z = 3.
        var posI = new Vector3(0f, 0f, 3f);
        var posJ = new Vector3(6f, 0f, 3f);
        var ejes = EjesLocalesCSI.Calcular(posI, posJ);

        var cinta = SyncEscenaService.ConstruirCintaMomento(posI, posJ, ejes);

        Assert.NotNull(cinta);
        // 12 estaciones × 2 vértices por estación = 24 posiciones.
        Assert.Equal(24, cinta!.Positions!.Count);
        Assert.Equal(24, cinta.Normals!.Count);
        // 11 segmentos × 2 triángulos × 3 vértices = 66 índices.
        Assert.Equal(66, cinta.TriangleIndices!.Count);

        // Verificación del perfil parabólico: la estación central (i=5 o 6)
        // debe estar desplazada en la dirección del Eje2 respecto al nodo
        // del eje correspondiente.
        var v0Centro = cinta.Positions[10];   // estación 5, vértice eje
        var v1Centro = cinta.Positions[11];   // estación 5, vértice desplazado
        var delta = v1Centro - v0Centro;
        Assert.True(delta.Length() > 0.1f,
            "El desplazamiento de la cinta en el centro debe ser visualmente significativo.");
    }

    // ===================================================================
    // RENDERIZADO VOLUMÉTRICO DE LOSAS (Módulo 1 Fase 3D-II)
    // ===================================================================

    /// <summary>
    /// Helper común para tests de losa: crea un Placement axis-aligned a
    /// partir de una losa y coordenadas (x0, y0) en planta.
    /// </summary>
    private static LosasPlus.Services.LayoutSolver.Placement BuildPlacement(
        int id, double x0, double y0, double lx, double ly, double espesor)
    {
        var losa = new LosasPlus.Models.Losa
        {
            Id      = id,
            Lx      = lx,
            Ly      = ly,
            Espesor = espesor,
        };
        return new LosasPlus.Services.LayoutSolver.Placement
        {
            Id    = id,
            Losa  = losa,
            X     = x0,
            Y     = y0,
        };
    }

    [Fact]
    public void ConstruirPrismaLosa_GeneraOchoVerticesYDoceTriangulos()
    {
        // Losa rectangular 4 × 6 × 0.15 m posicionada en origen, sobre
        // cota Z = 3.0 m.
        var p = BuildPlacement(id: 1, x0: 0.0, y0: 0.0, lx: 4.0, ly: 6.0, espesor: 0.15);

        var mesh = SyncEscenaService.ConstruirPrismaLosa(p, zNivel: 3.0);

        Assert.NotNull(mesh);
        Assert.Equal(8,  mesh.Positions!.Count);          // 8 vértices del prisma
        Assert.Equal(8,  mesh.Normals!.Count);            // normales suavizadas por vértice
        Assert.Equal(36, mesh.TriangleIndices!.Count);    // 12 triángulos × 3 índices
    }

    [Fact]
    public void ConstruirPrismaLosa_SuperficieSuperiorEnCotaNivel_BaseAEspesorPorDebajo()
    {
        // Losa 0.20 m de espesor en Z = 3.0 → superficie a 3.0, base a 2.80.
        var p = BuildPlacement(id: 1, x0: 0.0, y0: 0.0, lx: 3.0, ly: 3.0, espesor: 0.20);

        var mesh = SyncEscenaService.ConstruirPrismaLosa(p, zNivel: 3.0);

        int countTop = 0, countBot = 0;
        foreach (var v in mesh!.Positions!)
        {
            if (Math.Abs(v.Z - 3.00f) < 1e-4f) countTop++;
            if (Math.Abs(v.Z - 2.80f) < 1e-4f) countBot++;
        }
        Assert.Equal(4, countTop);
        Assert.Equal(4, countBot);
    }

    [Fact]
    public void ConstruirPrismaLosa_VerticesEnEsquinasDelRectangulo()
    {
        // Losa 5 × 8 m posicionada en (x0=2, y0=10) → esquinas esperadas
        // en (2,10), (7,10), (7,18), (2,18) tanto en base como en corona.
        var p = BuildPlacement(id: 1, x0: 2.0, y0: 10.0, lx: 5.0, ly: 8.0, espesor: 0.12);

        var mesh = SyncEscenaService.ConstruirPrismaLosa(p, zNivel: 0.0);

        bool tieneEsquina0 = false, tieneEsquina1 = false, tieneEsquina2 = false, tieneEsquina3 = false;
        foreach (var v in mesh!.Positions!)
        {
            if (Math.Abs(v.X -  2.0f) < 1e-4f && Math.Abs(v.Y - 10.0f) < 1e-4f) tieneEsquina0 = true;
            if (Math.Abs(v.X -  7.0f) < 1e-4f && Math.Abs(v.Y - 10.0f) < 1e-4f) tieneEsquina1 = true;
            if (Math.Abs(v.X -  7.0f) < 1e-4f && Math.Abs(v.Y - 18.0f) < 1e-4f) tieneEsquina2 = true;
            if (Math.Abs(v.X -  2.0f) < 1e-4f && Math.Abs(v.Y - 18.0f) < 1e-4f) tieneEsquina3 = true;
        }
        Assert.True(tieneEsquina0 && tieneEsquina1 && tieneEsquina2 && tieneEsquina3,
            "Las 4 esquinas (x0,y0)..(x0+Lx,y0+Ly) deben estar presentes en planta.");
    }

    [Fact]
    public void ConstruirPrismaLosa_EspesorRespetadoEnDistanciaVertical()
    {
        // Losa con espesor 0.25 m → distancia vertical entre base y corona
        // debe ser exactamente 0.25.
        var p = BuildPlacement(id: 1, x0: 0.0, y0: 0.0, lx: 2.0, ly: 2.0, espesor: 0.25);

        var mesh = SyncEscenaService.ConstruirPrismaLosa(p, zNivel: 0.0);

        float zMax = float.MinValue, zMin = float.MaxValue;
        foreach (var v in mesh!.Positions!)
        {
            if (v.Z > zMax) zMax = v.Z;
            if (v.Z < zMin) zMin = v.Z;
        }
        Assert.Equal(0.25f, zMax - zMin, precision: 4);
    }

    [Fact]
    public void ConstruirPrismaLosa_WindingCCW_NormalesApuntanAfuera()
    {
        // Verifica que para CADA triángulo del prisma, (v1−v0)×(v2−v0)
        // apunte hacia el exterior (mismo semiespacio que el radial desde
        // el centro del volumen). Es la condición de visibilidad bajo
        // CullMode.Back + FrontCounterClockwise=true.
        var p = BuildPlacement(id: 1, x0: 1.0, y0: 1.0, lx: 4.0, ly: 4.0, espesor: 0.15);

        var mesh = SyncEscenaService.ConstruirPrismaLosa(p, zNivel: 2.5);

        var positions = mesh!.Positions!;
        var indices   = mesh.TriangleIndices!;

        var centro = Vector3.Zero;
        foreach (var v in positions) centro += v;
        centro /= positions.Count;

        int triCount = indices.Count / 3;
        for (int t = 0; t < triCount; t++)
        {
            var v0 = positions[indices[t * 3 + 0]];
            var v1 = positions[indices[t * 3 + 1]];
            var v2 = positions[indices[t * 3 + 2]];
            var normalCCW   = Vector3.Cross(v1 - v0, v2 - v0);
            var centroTri   = (v0 + v1 + v2) / 3f;
            var dirExterior = centroTri - centro;
            Assert.True(Vector3.Dot(normalCCW, dirExterior) > 0f,
                $"Triángulo {t} del prisma de losa viola el winding CCW exterior.");
        }
    }

    [Fact]
    public async Task RegenerarEscenaAsync_ConProyectoConLosa_NoLanzaExcepcion()
    {
        // Smoke: pipeline completo de SyncEscenaService.GenerarMallasProyectoAsync
        // con un proyecto que contiene 1 losa. La hilo de UI no existe en
        // xUnit; el método debe degradar limpiamente (no crash) tras
        // construir las mallas en background.
        using var vm = new Viewport3DViewModel();
        var proyecto = new LosasPlus.Models.Proyecto();
        proyecto.AsegurarEstructura();
        // AsegurarEstructura siembra Edificio+Nivel pero NO un Sistema.
        var sistema = new LosasPlus.Models.Sistema { Nombre = "S1" };
        sistema.Losas.Add(new LosasPlus.Models.Losa
        {
            Id      = 1,
            Lx      = 4.0,
            Ly      = 5.0,
            Espesor = 0.12,
        });
        proyecto.Edificios[0].Niveles[0].Sistemas.Add(sistema);

        await vm.RegenerarEscenaAsync(proyecto);

        // En ambiente xUnit (sin Application.Current), el swap al hilo UI
        // se omite — la colección queda vacía. Lo crítico es que no haya
        // lanzado durante la generación de mallas.
        Assert.False(vm.CargandoEscena,
            "CargandoEscena debe quedar en false tras el finally del método.");
    }
}
