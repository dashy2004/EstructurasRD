using System.Linq;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Sincroniza la geometría en planta entre el modelo 2D (Planta 2D / Lienzo) y
/// el 3D (<see cref="LosasPlus.Render3D.EscenaEdificio"/>). Ambas vistas leen
/// <see cref="Losa.CoordenadaX"/>/<see cref="Losa.CoordenadaY"/>, pero esas
/// coordenadas sólo se asignan al arrastrar/soltar en Planta 2D: una losa creada
/// en el grid del Editor o importada de un <c>.DL</c>/<c>.TXT</c> queda en (0,0),
/// y entonces <b>todas las losas se apilan en el origen</b> ("una encima de otra")
/// tanto en 2D como en 3D.
///
/// <para>
/// Este servicio <b>hornea</b> el layout topológico no-solapado de
/// <see cref="LayoutSolver"/> (derivado de las adyacencias <c>BordesX</c>/
/// <c>BordesY</c>) en <see cref="Losa.CoordenadaX"/>/<see cref="Losa.CoordenadaY"/>,
/// de modo que ambas vistas muestran la misma distribución. Es conservador: sólo
/// actúa cuando el sistema está <i>sin posicionar</i> (todas las losas en (0,0)),
/// para no pisar las posiciones que el usuario ya movió a mano.
/// </para>
/// </summary>
public static class SincronizadorPlanta
{
    /// <summary>
    /// True si el sistema necesita un layout por defecto: tiene ≥2 losas y todas
    /// están en la posición de planta sin asignar (0,0) → se solaparían.
    /// </summary>
    public static bool RequiereSincronizacion(Sistema? sistema)
    {
        if (sistema is null || sistema.Losas.Count < 2) return false;
        return sistema.Losas.All(l => l.CoordenadaX == 0.0 && l.CoordenadaY == 0.0);
    }

    /// <summary>
    /// Aplica el layout de <see cref="LayoutSolver"/> a <see cref="Losa.CoordenadaX"/>/
    /// <see cref="Losa.CoordenadaY"/>. Devuelve <c>true</c> si modificó algo.
    /// </summary>
    /// <param name="sistema">Sistema a sincronizar.</param>
    /// <param name="forzar">Si es true, reposiciona aunque ya haya coordenadas (recalcula desde adyacencias).</param>
    public static bool Sincronizar(Sistema? sistema, bool forzar = false)
    {
        if (sistema is null || sistema.Losas.Count == 0) return false;
        if (!forzar && !RequiereSincronizacion(sistema)) return false;

        var layout = LayoutSolver.Solve(sistema);
        foreach (var p in layout.Placements)
        {
            p.Losa.CoordenadaX = p.X;
            p.Losa.CoordenadaY = p.Y;
        }
        return layout.Placements.Count > 0;
    }

    /// <summary>Sincroniza todos los sistemas de todos los niveles de un edificio.</summary>
    public static void SincronizarEdificio(Edificio? edificio, bool forzar = false)
    {
        if (edificio is null) return;
        foreach (var nivel in edificio.Niveles)
            foreach (var sistema in nivel.Sistemas)
                Sincronizar(sistema, forzar);
    }
}
