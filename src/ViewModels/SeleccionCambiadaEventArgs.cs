using System;
using LosasPlus.Topologia;

namespace LosasPlus.ViewModels;

/// <summary>
/// Payload del evento estándar <c>EventHandler&lt;SeleccionCambiadaEventArgs&gt;</c>
/// expuesto por <see cref="SeleccionService.SeleccionCambiada"/> (Liga B paso B2).
/// Es el espejo CLR-standard del legado <c>Action&lt;DomainKey?&gt;
/// OnSeleccionCambiada</c> — agregado, no sustituto: ambos eventos se
/// disparan simultáneamente dentro del mismo bloque <c>try/finally</c>
/// anti-recursión de <see cref="SeleccionService.FijarSeleccion"/>.
///
/// <para>
/// Motivo de existencia: <c>WeakEventManager&lt;TSource, TEventArgs&gt;</c>
/// (built-in en WPF, <c>System.Windows</c>) sólo soporta eventos con
/// la firma estándar <c>EventHandler&lt;TEventArgs&gt;</c>. El <c>Action&lt;T&gt;</c>
/// legado es incompatible. La suscripción débil del shell al servicio
/// — exigida por la directiva de prevención de fugas de memoria de
/// B2 — pasa exclusivamente por este nuevo evento.
/// </para>
///
/// <para>Tipo inmutable: ambas propiedades son <c>get</c>-only.</para>
/// </summary>
public sealed class SeleccionCambiadaEventArgs : EventArgs
{
    /// <summary>
    /// Nuevo <see cref="DomainKey"/> seleccionado, o <c>null</c> si la
    /// llamada deselecciona. Coincide exactamente con el argumento
    /// publicado por el <c>Action&lt;DomainKey?&gt;</c> legado.
    /// </summary>
    public DomainKey? Seleccion { get; }

    /// <summary>
    /// Objeto que originó la mutación (viewport 3D, panel 2D, comando
    /// de teclado, etc.). Se propaga sin interpretarse — los consumidores
    /// pueden usarlo para auditoría o para filtrar eventos según su
    /// propia fuente (evitar ecos).
    /// </summary>
    public object? Origen { get; }

    public SeleccionCambiadaEventArgs(DomainKey? seleccion, object? origen)
    {
        Seleccion = seleccion;
        Origen    = origen;
    }
}
