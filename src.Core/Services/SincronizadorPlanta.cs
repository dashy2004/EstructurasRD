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
    /// True si el sistema necesita un layout por defecto: tiene ≥2 losas y al
    /// menos una LIBRE (no <see cref="Losa.Anclada"/>) sigue en el origen (0,0)
    /// → se apilaría sobre las demás. Las ancladas jamás disparan: su posición
    /// es verdad explícita del usuario/import aunque sea (0,0) (UI1.1).
    /// </summary>
    public static bool RequiereSincronizacion(Sistema? sistema)
    {
        if (sistema is null || sistema.Losas.Count < 2) return false;
        return sistema.Losas.Any(l => !l.Anclada && l.CoordenadaX == 0.0 && l.CoordenadaY == 0.0);
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
            // Bake-once (UI1.1): lo horneado queda explícito. Sin esto, anclar
            // una losa después haría que las libres se re-coloquen relativas a
            // ella ("teletransporte") en la siguiente sincronización.
            p.Losa.Anclada = true;
        }
        return layout.Placements.Count > 0;
    }

    /// <summary>Separación por defecto de la grilla de columnas, en metros.</summary>
    public const double EspaciadoColumnasDefault = 5.0;

    /// <summary>
    /// Distribuye las columnas del nivel en una grilla cuadrada cuando están todas
    /// sin posicionar (en (0,0)) — evita que se apilen (y con ellas sus zapatas,
    /// que se dibujan en la huella de cada columna). Devuelve <c>true</c> si modificó.
    /// </summary>
    public static bool SincronizarColumnas(Nivel? nivel, double espaciado = EspaciadoColumnasDefault, bool forzar = false)
    {
        if (nivel is null || nivel.Columnas.Count < 2) return false;
        if (!forzar && !nivel.Columnas.All(c => c.CoordenadaX == 0.0 && c.CoordenadaY == 0.0))
            return false;

        int porFila = (int)System.Math.Ceiling(System.Math.Sqrt(nivel.Columnas.Count));
        for (int i = 0; i < nivel.Columnas.Count; i++)
        {
            nivel.Columnas[i].CoordenadaX = (i % porFila) * espaciado;
            nivel.Columnas[i].CoordenadaY = (i / porFila) * espaciado;
        }
        return true;
    }

    /// <summary>
    /// Distribuye las vigas del nivel cuando están todas sin posicionar (Origen en
    /// (0,0)) — evita que arranquen apiladas en el origen, igual que las columnas.
    /// Devuelve <c>true</c> si modificó.
    /// </summary>
    public static bool SincronizarVigas(Nivel? nivel, double espaciado = EspaciadoColumnasDefault, bool forzar = false)
    {
        if (nivel is null || nivel.Vigas.Count < 2) return false;
        if (!forzar && !nivel.Vigas.All(v => v.OrigenX == 0.0 && v.OrigenY == 0.0))
            return false;

        for (int i = 0; i < nivel.Vigas.Count; i++)
        {
            nivel.Vigas[i].OrigenX = 0.0;
            nivel.Vigas[i].OrigenY = i * espaciado;   // separadas en Y para no solaparse en el origen
        }
        return true;
    }

    /// <summary>Sincroniza losas (por sistema), columnas y vigas (por nivel) de todo el edificio.</summary>
    public static void SincronizarEdificio(Edificio? edificio, bool forzar = false)
    {
        if (edificio is null) return;
        foreach (var nivel in edificio.Niveles)
        {
            foreach (var sistema in nivel.Sistemas)
                Sincronizar(sistema, forzar);
            SincronizarColumnas(nivel, forzar: forzar);
            SincronizarVigas(nivel, forzar: forzar);
        }
    }
}
