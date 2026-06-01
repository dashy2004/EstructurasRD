using System.Collections.Generic;

namespace LosasPlus.Models.Cad;

/// <summary>
/// Una entrada del DTO de movimiento de grupo: la Id de una losa del SSOT y la
/// nueva posición (esquina superior-izquierda, en metros de lienzo) a la que
/// debe moverse.
/// </summary>
/// <param name="LosaId">Id de la losa del <c>Sistema.Losas</c> a actualizar.</param>
/// <param name="PosX">Nueva esquina superior-izquierda X (m).</param>
/// <param name="PosY">Nueva esquina superior-izquierda Y (m).</param>
public sealed record MovimientoLosaEntry(int LosaId, double PosX, double PosY);

/// <summary>
/// DTO inmutable que viaja del lienzo CAD (<c>CadCanvasHost</c>) al
/// <c>CadEditorViewModel</c> cuando el usuario termina de arrastrar un bloque
/// conectado con «Mover Conectadas» activo (Iteración 3, Epic v1.2). El comando
/// que lo recibe toma <b>un único</b> snapshot de Undo y aplica las nuevas
/// posiciones a toda la componente conexa, anclando las losas que estaban
/// flotantes en (posición resuelta + delta).
/// </summary>
/// <param name="Movimientos">Una entrada por cada losa del grupo (incluida la líder).</param>
public sealed record MovimientoGrupoArgs(IReadOnlyList<MovimientoLosaEntry> Movimientos);
