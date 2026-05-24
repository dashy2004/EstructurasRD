using System;
using System.Runtime.CompilerServices;
using System.Windows;
using LosasPlus.Topologia;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests del par de eventos del <see cref="SeleccionService"/> (Liga B
/// paso B2). Cobertura:
/// <list type="bullet">
///   <item>El evento espejo CLR-standard <c>SeleccionCambiada</c> se
///   dispara junto al legacy <c>OnSeleccionCambiada</c> dentro del
///   mismo <c>try/finally</c> anti-recursión.</item>
///   <item>El payload del nuevo evento contiene la <see cref="DomainKey"/>
///   y el objeto emisor pasados a <c>FijarSeleccion</c>.</item>
///   <item>El <c>WeakEventManager</c> no retiene al subscriber — tras
///   dropear la única referencia fuerte y forzar GC, el subscriber es
///   recolectado.</item>
///   <item>El handler suscrito vía <c>WeakEventManager</c> es invocado
///   mientras el subscriber permanezca vivo.</item>
/// </list>
/// </summary>
public class SeleccionServiceMessagingTests
{
    [Fact]
    public void SeleccionCambiada_DisparaJuntoAlLegacyAction()
    {
        var servicio = new SeleccionService();

        int contadorLegacy = 0;
        int contadorNuevo  = 0;
        servicio.OnSeleccionCambiada += _   => contadorLegacy++;
        servicio.SeleccionCambiada   += (_, _) => contadorNuevo++;

        servicio.FijarSeleccion(new DomainKey(TipoElemento.Columna, 1), emisor: this);

        // Ambos eventos se disparan dentro del mismo bloque try/finally
        // anti-recursión — coherencia transaccional.
        Assert.Equal(1, contadorLegacy);
        Assert.Equal(1, contadorNuevo);
    }

    [Fact]
    public void SeleccionCambiada_PayloadContieneKeyYOrigenCorrectos()
    {
        var servicio = new SeleccionService();
        var keyEsperada = new DomainKey(TipoElemento.Viga, 42);
        var emisorEsperado = new object();

        SeleccionCambiadaEventArgs? capturado = null;
        servicio.SeleccionCambiada += (_, e) => capturado = e;

        servicio.FijarSeleccion(keyEsperada, emisorEsperado);

        Assert.NotNull(capturado);
        Assert.Equal(keyEsperada, capturado!.Seleccion);
        Assert.Same(emisorEsperado, capturado.Origen);
    }

    [Fact]
    public void WeakEventManager_SubscriberRecolectable_NoRetenidoTrasGC()
    {
        var servicio = new SeleccionService();

        // Crear el subscriber dentro de un método NoInlining cuya única
        // salida es la WeakReference — la referencia fuerte queda atada
        // al frame del helper y se libera al retornar.
        WeakReference weakRef = SuscribirYDevolverWeakRef(servicio);

        // Forzar 3 ciclos completos de GC para evitar dependencia del
        // colector concurrente.
        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(weakRef.IsAlive,
            "El WeakEventManager NO debe retener al subscriber. Tras dropear " +
            "la última referencia fuerte y forzar GC tres veces, el subscriber " +
            "debe ser recolectable — de lo contrario habrá fuga de memoria.");
    }

    [Fact]
    public void WeakEventManager_HandlerInvocableMientrasViveSubscriber()
    {
        var servicio = new SeleccionService();
        var subscriber = new SubscriberDummy();
        WeakEventManager<SeleccionService, SeleccionCambiadaEventArgs>.AddHandler(
            servicio,
            nameof(SeleccionService.SeleccionCambiada),
            subscriber.Handle);

        servicio.FijarSeleccion(new DomainKey(TipoElemento.Zapata, 9), emisor: this);

        // El subscriber permanece vivo en este frame → el handler se
        // invoca normalmente vía la suscripción débil.
        Assert.Equal(1, subscriber.InvocationCount);

        // Mantenerlo explícitamente vivo hasta el final del test para
        // que el JIT no lo libere de manera oportunista.
        GC.KeepAlive(subscriber);
    }

    // ===== Helpers =====

    /// <summary>
    /// Suscribe un <see cref="SubscriberDummy"/> al servicio vía
    /// <c>WeakEventManager</c> y retorna sólo una <see cref="WeakReference"/>
    /// al subscriber. Marcado <c>NoInlining</c> para garantizar que la
    /// referencia fuerte al subscriber NO sobreviva al retorno (de lo
    /// contrario el JIT podría inlinearlo y mantener la referencia
    /// viva en el frame del caller).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SuscribirYDevolverWeakRef(SeleccionService servicio)
    {
        var subscriber = new SubscriberDummy();
        WeakEventManager<SeleccionService, SeleccionCambiadaEventArgs>.AddHandler(
            servicio,
            nameof(SeleccionService.SeleccionCambiada),
            subscriber.Handle);
        return new WeakReference(subscriber);
    }

    private class SubscriberDummy
    {
        public int InvocationCount { get; private set; }
        public void Handle(object? sender, SeleccionCambiadaEventArgs e)
            => InvocationCount++;
    }
}
