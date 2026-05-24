using System;
using System.Threading;
using System.Threading.Tasks;
using LosasPlus.Models;
using LosasPlus.Topologia;

namespace LosasPlus.ViewModels.Mensajeria;

/// <summary>
/// Coordinator del cómputo asíncrono de Ratio D/C (Liga B paso B4).
/// Encapsula las 4 directivas de resiliencia:
/// <list type="number">
///   <item><b>UI thread non-blocking</b>: el cálculo SIEMPRE se delega
///   al ThreadPool vía <see cref="Task.Run(Action)"/> con
///   <c>ConfigureAwait(false)</c>.</item>
///   <item><b>Cancelación secuencial</b>: cada invocación de
///   <see cref="ComputarAsync"/> cancela y dispone el
///   <see cref="CancellationTokenSource"/> de la operación previa
///   antes de lanzar la nueva. Resultados tardíos son descartados —
///   el callback se invoca SOLO si el token no fue cancelado.</item>
///   <item><b>Aislamiento de excepciones</b>: la responsabilidad de
///   "no lanzar" recae en <see cref="IRatioCalculatorService"/>
///   (debe retornar NaN ante error). El coordinator suprime
///   <see cref="OperationCanceledException"/> silenciosamente.</item>
///   <item><b>Memory eviction</b>: el <c>CTS</c> previo se
///   <c>Dispose()</c> en cada mutación. El método <see cref="Dispose"/>
///   cancela y libera el pendiente. El coordinator NO retiene
///   referencias fuertes a entidades de dominio ni al ViewModel
///   — el callback es inyectado externamente.</item>
/// </list>
///
/// <para>
/// Thread-safe: la mutación de <c>_ctsActual</c> está protegida por
/// un lock interno. Múltiples invocaciones concurrentes de
/// <see cref="ComputarAsync"/> serializan correctamente.
/// </para>
/// </summary>
public sealed class RatioComputacionCoordinator : IDisposable
{
    private readonly IRatioCalculatorService _calculator;
    private CancellationTokenSource? _ctsActual;
    private readonly object _lock = new();
    private bool _disposed;

    public RatioComputacionCoordinator(IRatioCalculatorService calculator)
        => _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));

    /// <summary>
    /// Lanza la computación asíncrona del ratio para
    /// <paramref name="key"/>. Si hay una operación previa en vuelo,
    /// la cancela y dispone su <c>CTS</c> antes de empezar. El
    /// <paramref name="onCompletado"/> es invocado SOLO si la
    /// operación no fue cancelada — los callbacks de operaciones
    /// canceladas son descartados sin notificar.
    /// </summary>
    /// <param name="proyecto">Proyecto al que pertenece la entidad.</param>
    /// <param name="key">Identificador del elemento estructural.</param>
    /// <param name="onCompletado">
    /// Callback (DomainKey, double) → void, ejecutado en el thread
    /// donde el await retorna (típicamente el ThreadPool — el caller
    /// es responsable del marshaling al UI thread si es necesario).
    /// </param>
    public async Task ComputarAsync(
        Proyecto proyecto, DomainKey key,
        Action<DomainKey, double> onCompletado)
    {
        ArgumentNullException.ThrowIfNull(proyecto);
        ArgumentNullException.ThrowIfNull(onCompletado);
        if (_disposed) return;

        // Directiva 4: cancelar y disponer el CTS anterior antes de crear el nuevo.
        CancellationTokenSource nuevo;
        lock (_lock)
        {
            if (_disposed) return;
            _ctsActual?.Cancel();
            _ctsActual?.Dispose();
            nuevo = new CancellationTokenSource();
            _ctsActual = nuevo;
        }
        var token = nuevo.Token;

        double ratio;
        try
        {
            // Directiva 1: cómputo SIEMPRE en ThreadPool — UI libre.
            // ConfigureAwait(false) evita atar el continuation al
            // SynchronizationContext del caller — el caller marshala
            // dentro del callback si necesita el UI thread.
            ratio = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return _calculator.CalcularRatio(proyecto, key);
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Directiva 2: resultado descartado — no invocar callback.
            return;
        }

        // Directiva 2 (segunda barrera): re-check post-await en caso
        // de que otra invocación haya cancelado este token mientras
        // el await estaba pendiente.
        if (token.IsCancellationRequested) return;

        onCompletado(key, ratio);
    }

    /// <summary>
    /// Cancela y libera el <see cref="CancellationTokenSource"/>
    /// pendiente. Idempotente — segundas invocaciones son no-op.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _ctsActual?.Cancel();
            _ctsActual?.Dispose();
            _ctsActual = null;
        }
    }
}
