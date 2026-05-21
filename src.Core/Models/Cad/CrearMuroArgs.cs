namespace LosasPlus.Models.Cad;

/// <summary>
/// DTO inmutable que viaja del lienzo CAD (<c>CadCanvasHost</c>) al
/// <c>CadEditorViewModel</c> cuando el usuario termina de <b>dibujar</b> un
/// muro nuevo con la herramienta de dibujo (Epic v1.4.0, Módulo Muros).
///
/// <para>
/// Hermano de <see cref="CrearLosaArgs"/>: describe un muro que aún no
/// existe — el comando que lo recibe toma el snapshot de Undo, instancia el
/// <see cref="Muro"/> y lo agrega al SSOT.
/// </para>
/// </summary>
/// <param name="Inicio">Extremo inicial del muro, en metros de lienzo.</param>
/// <param name="Fin">Extremo final del muro, en metros de lienzo.</param>
public sealed record CrearMuroArgs(PuntoCad Inicio, PuntoCad Fin);
