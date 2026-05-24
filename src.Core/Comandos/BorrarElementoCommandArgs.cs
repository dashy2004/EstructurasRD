using System;
using LosasPlus.Topologia;

namespace LosasPlus.Comandos;

/// <summary>
/// Payload inmutable del comando <c>BorrarElementoCommand</c> (Liga B
/// paso B3). Identifica el elemento a eliminar por
/// <see cref="DomainKey"/> (Tipo + Id) y un <see cref="TransactionId"/>
/// para deduplicación idempotente.
///
/// <para>
/// Semánticas Ghost Deletion (directiva 2 de B3): si el elemento ya
/// fue eliminado por un proceso concurrente o nunca existió, el
/// comando finaliza con éxito sin propagar excepciones. El
/// <c>TransactionId</c> garantiza que reintentos accidentales no
/// produzcan efectos colaterales.
/// </para>
///
/// <para>Tipo puro de dominio sin dependencias de WPF.</para>
/// </summary>
/// <param name="TransactionId">Guid único — identificador de transacción para idempotencia.</param>
/// <param name="Target">Llave del elemento estructural a eliminar.</param>
public readonly record struct BorrarElementoCommandArgs(
    Guid TransactionId,
    DomainKey Target);
