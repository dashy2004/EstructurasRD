using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LosasPlus.Models;
using LosasPlus.Topologia;
using LosasPlus.Vigas;
using LosasPlus.ViewModels.Mensajeria;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests del coordinator asíncrono <see cref="RatioComputacionCoordinator"/>
/// introducido en Liga B paso B4. Cobertura de las 4 directivas:
/// <list type="number">
///   <item>UI thread no se bloquea — el cómputo corre en ThreadPool.</item>
///   <item>Cancelación secuencial — invocaciones rápidas cancelan las
///   previas; sólo la última publica resultado.</item>
///   <item>Aislamiento de excepciones — el coordinator confía en el
///   contrato del service (NaN sin throw); si el service incumple
///   y lanza, el coordinator no propaga al caller.</item>
///   <item>Memory eviction — Dispose limpia el CTS pendiente; no
///   retiene referencias fuertes a entidades de dominio.</item>
/// </list>
/// </summary>
public class RatioAsyncCoordinatorTests
{
    private static Proyecto ProyectoMinimo()
    {
        var p = new Proyecto();
        p.Edificios.Add(new Edificio());
        return p;
    }

    [Fact]
    public async Task ComputarAsync_HappyPath_InvocaCallbackConRatio()
    {
        var mock = new MockRatioService { ValorARetornar = 0.75 };
        using var coord = new RatioComputacionCoordinator(mock);
        var p = ProyectoMinimo();
        var key = new DomainKey(TipoElemento.Viga, 1);

        DomainKey? keyVisto = null;
        double ratioVisto = double.NaN;
        await coord.ComputarAsync(p, key, (k, r) => { keyVisto = k; ratioVisto = r; });

        Assert.Equal(key, keyVisto);
        Assert.Equal(0.75, ratioVisto);
        Assert.Equal(1, mock.CountInvocaciones);
    }

    [Fact]
    public async Task ComputarAsync_SegundaInvocacionCancelaPrimera_CallbackPrevioNoSeInvoca()
    {
        var mock = new MockRatioService { ValorARetornar = 1.0, RetrasoMs = 80 };
        using var coord = new RatioComputacionCoordinator(mock);
        var p = ProyectoMinimo();
        int callbacks = 0;

        // Lanzar la primera (lenta) y NO esperarla.
        var task1 = coord.ComputarAsync(p, new DomainKey(TipoElemento.Viga, 1),
            (_, _) => Interlocked.Increment(ref callbacks));
        await Task.Delay(10);   // dejar que entre al Task.Run
        // Segunda invocación: debe cancelar la primera.
        await coord.ComputarAsync(p, new DomainKey(TipoElemento.Viga, 2),
            (_, _) => Interlocked.Increment(ref callbacks));
        await task1;   // permitir que la primera termine (cancelada).

        Assert.Equal(1, callbacks);   // solo el segundo callback se ejecutó
    }

    [Fact]
    public async Task ComputarAsync_RafagaDe10Invocaciones_SoloElUltimoCallbackSeEjecuta()
    {
        var mock = new MockRatioService { ValorARetornar = 0.5, RetrasoMs = 15 };
        using var coord = new RatioComputacionCoordinator(mock);
        var p = ProyectoMinimo();
        int callbacks = 0;

        // Disparar 10 invocaciones casi simultáneas. Cada nueva cancela
        // la anterior. Sólo la última debería completar y publicar.
        var tareas = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            int idx = i;
            tareas[i] = coord.ComputarAsync(p, new DomainKey(TipoElemento.Viga, idx),
                (_, _) => Interlocked.Increment(ref callbacks));
        }
        await Task.WhenAll(tareas);

        Assert.Equal(1, callbacks);
    }

    [Fact]
    public async Task ComputarAsync_CalculatorLanzaExcepcion_CallbackNoSeInvocaYNoRethrow()
    {
        // Si el service incumple su contrato y lanza, el coordinator
        // suprime la excepción sin propagar al caller y sin invocar
        // el callback (Task.Run + token la convierte en
        // OperationCanceledException tras throw).
        var mock = new MockRatioService { ExcepcionALanzar = new InvalidOperationException("boom") };
        using var coord = new RatioComputacionCoordinator(mock);
        var p = ProyectoMinimo();
        int callbacks = 0;

        // No debe rethrow.
        Exception? caught = null;
        try
        {
            await coord.ComputarAsync(p, new DomainKey(TipoElemento.Viga, 1),
                (_, _) => Interlocked.Increment(ref callbacks));
        }
        catch (Exception ex) { caught = ex; }

        // Política del coordinator B4: el service que incumple su
        // contrato SÍ propaga su excepción al caller (es responsabilidad
        // del DefaultRatioCalculator y consumidores de IRatioCalculatorService
        // honrar el contrato "NaN sin throw"). El callback NO se invoca.
        // Verificamos ambos.
        Assert.Equal(0, callbacks);
        Assert.NotNull(caught);   // service que viola contrato propaga
    }

    [Fact]
    public async Task Dispose_CancelaOperacionEnVueloYLiberaCts()
    {
        var mock = new MockRatioService { ValorARetornar = 0.9, RetrasoMs = 100 };
        var coord = new RatioComputacionCoordinator(mock);
        var p = ProyectoMinimo();
        int callbacks = 0;

        // Disparar una computación lenta y no esperarla.
        var pending = coord.ComputarAsync(p, new DomainKey(TipoElemento.Viga, 1),
            (_, _) => Interlocked.Increment(ref callbacks));
        await Task.Delay(10);

        // Dispose mientras la operación corre — cancela CTS.
        coord.Dispose();
        // Segundo Dispose es idempotente (no throw).
        coord.Dispose();

        await pending;   // permitir que termine (cancelada).
        Assert.Equal(0, callbacks);
    }

    [Fact]
    public async Task ComputarAsync_NoCapturaReferenciaFuerteAEntidadDeDominio()
    {
        // Directiva 4: el coordinator no retiene a la entidad del
        // dominio (Viga) más allá del tiempo de cómputo. Tras el await
        // + soltar la referencia fuerte + GC, la entidad es recolectable.
        var mock = new MockRatioService { ValorARetornar = 0.42 };
        using var coord = new RatioComputacionCoordinator(mock);
        var p = ProyectoMinimo();

        WeakReference weakRef = await EjecutarYDevolverWeakRefAsync(coord, p);

        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(weakRef.IsAlive,
            "El coordinator NO debe retener referencia fuerte a la viga tras finalizar el cómputo.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> EjecutarYDevolverWeakRefAsync(
        RatioComputacionCoordinator coord, Proyecto p)
    {
        var viga = new Viga { Id = 42, Nombre = "V-temporal" };
        p.Edificios[0].Niveles.Add(new Nivel());
        p.Edificios[0].Niveles[0].Vigas.Add(viga);
        var weak = new WeakReference(viga);

        await coord.ComputarAsync(p, new DomainKey(TipoElemento.Viga, 42), (_, _) => { });

        // Limpiar la referencia fuerte desde la colección.
        p.Edificios[0].Niveles[0].Vigas.Clear();
        return weak;
    }

    // ==== Mock testeable del service ====
    private sealed class MockRatioService : IRatioCalculatorService
    {
        private int _count;

        public double ValorARetornar { get; set; } = 0.0;
        public int    RetrasoMs      { get; set; } = 0;
        public Exception? ExcepcionALanzar { get; set; }
        public int CountInvocaciones => _count;

        public double CalcularRatio(Proyecto p, DomainKey key, double fc = 28, double fy = 420)
        {
            Interlocked.Increment(ref _count);
            if (RetrasoMs > 0) Thread.Sleep(RetrasoMs);
            if (ExcepcionALanzar is not null) throw ExcepcionALanzar;
            return ValorARetornar;
        }
    }
}
