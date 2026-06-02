namespace LosasPlus.ViewModels;

/// <summary>
/// Herramienta de interacción activa del lienzo CAD (Iteración 3, FASE C).
/// La toolbar flotante de <c>CadView</c> la cambia; <c>CadCanvasHost</c> rutea
/// los eventos de mouse según este modo.
/// </summary>
public enum ModoInteraccionCad
{
    /// <summary>Selección, arrastre, redimensión y snapping de losas (por defecto).</summary>
    Puntero,

    /// <summary>Click-drag dibuja una losa rectangular nueva en el lienzo.</summary>
    DibujarLosa,

    /// <summary>Arrastrar el lienzo (pan dedicado). No selecciona ni mueve losas.</summary>
    Mano,

    /// <summary>Click-drag traza un muro nuevo entre dos puntos (Epic v1.4.0).</summary>
    DibujarMuro,
}
