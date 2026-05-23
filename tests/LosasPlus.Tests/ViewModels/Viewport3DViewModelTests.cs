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
}
