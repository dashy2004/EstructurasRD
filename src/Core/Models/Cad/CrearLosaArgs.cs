namespace LosasPlus.Models.Cad;

/// <summary>
/// DTO inmutable que viaja del lienzo CAD (<c>CadCanvasHost</c>) al
/// <c>CadEditorViewModel</c> cuando el usuario termina de <b>dibujar</b> una
/// losa rectangular nueva con la herramienta de dibujo (Iteración 3, FASE C).
///
/// <para>
/// Hermano de <see cref="ActualizacionLosaArgs"/>, pero para una losa que aún
/// no existe: no lleva referencia a una <c>Losa</c>. El comando que lo recibe
/// toma el snapshot de Undo, instancia la losa y la agrega al SSOT.
/// </para>
/// </summary>
/// <param name="PosX">Esquina superior-izquierda X, en metros de lienzo.</param>
/// <param name="PosY">Esquina superior-izquierda Y, en metros de lienzo.</param>
/// <param name="Lx">Ancho de la losa, en metros.</param>
/// <param name="Ly">Alto de la losa, en metros.</param>
public sealed record CrearLosaArgs(double PosX, double PosY, double Lx, double Ly);
