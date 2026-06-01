using LosasPlus.Models;

namespace LosasPlus.Models.Cad;

/// <summary>
/// DTO inmutable que viaja del lienzo CAD (code-behind de <c>CadCanvasHost</c>)
/// al <c>CadEditorViewModel</c> cuando el usuario termina de <b>mover</b> o
/// <b>redimensionar</b> una losa (Fase 3 del <c>PLAN_CAD_V1.md</c>).
///
/// <para>
/// Existe para mantener el ViewModel libre de tipos de
/// <c>System.Windows.Input</c>: el host resuelve el gesto de mouse y entrega
/// únicamente datos de dominio ya calculados. El comando que lo recibe toma
/// el snapshot de Undo y escribe estas coordenadas en el SSOT.
/// </para>
/// </summary>
/// <param name="Losa">La losa del SSOT a actualizar.</param>
/// <param name="PosX">Nueva esquina superior-izquierda X, en metros de lienzo.</param>
/// <param name="PosY">Nueva esquina superior-izquierda Y, en metros de lienzo.</param>
/// <param name="Lx">Nuevo ancho de la losa, en metros.</param>
/// <param name="Ly">Nuevo alto de la losa, en metros.</param>
/// <param name="Tipo">Código de tipo de losa (catálogo de 23 tipos permitidos).</param>
public sealed record ActualizacionLosaArgs(
    Losa Losa, double PosX, double PosY, double Lx, double Ly, int Tipo);
