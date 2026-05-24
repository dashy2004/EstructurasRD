using System;

namespace LosasPlus.Comandos;

/// <summary>
/// Payload inmutable del comando <c>CrearVigaCommand</c> (Liga B paso B3).
/// Contiene los puntos extremos en coordenadas del plano del nivel
/// activo (m) y un <see cref="TransactionId"/> para deduplicación
/// idempotente — si el mismo <c>Guid</c> se procesa dos veces (ráfaga
/// de clicks, doble-binding), la segunda invocación es ignorada.
///
/// <para>
/// El nivel destino NO se incluye en el DTO porque <c>Nivel</c> no
/// expone Id en el dominio actual. El handler usa
/// <c>MainViewModel.NivelActivo</c> como referencia canónica al
/// momento de la invocación — no hay race condition porque entre la
/// emisión del click y la resolución del comando pasan microsegundos.
/// </para>
///
/// <para>
/// El tipo es <c>readonly record struct</c> — igualdad por valor + cero
/// allocations al fluir entre la capa de presentación (handler XAML) y
/// el VM. Tipo puro de dominio sin dependencias de WPF.
/// </para>
/// </summary>
/// <param name="TransactionId">Guid único — identificador de transacción para idempotencia.</param>
/// <param name="X1">Coordenada X del primer endpoint en el plano del nivel (m).</param>
/// <param name="Y1">Coordenada Y del primer endpoint en el plano del nivel (m).</param>
/// <param name="X2">Coordenada X del segundo endpoint en el plano del nivel (m).</param>
/// <param name="Y2">Coordenada Y del segundo endpoint en el plano del nivel (m).</param>
public readonly record struct CrearVigaCommandArgs(
    Guid TransactionId,
    double X1, double Y1,
    double X2, double Y2);
