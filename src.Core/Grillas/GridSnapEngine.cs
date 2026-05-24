using System;

namespace LosasPlus.Grillas;

/// <summary>
/// Motor estático puro de magnetización del cursor del mouse a las
/// intersecciones de ejes de una <see cref="GrillaEstructural"/>
/// (Módulo 2 Parte B Fase 3D-II del Plan Maestro de Expansión 3D).
///
/// <para>
/// La función <see cref="CalcularSnapAGrilla"/> opera sobre coordenadas
/// planas <c>(X, Y)</c> en metros — la <c>Z</c> es responsabilidad del
/// llamador (típicamente <c>Nivel.Cota</c>). El motor vive en
/// <c>src.Core/Grillas/</c>, capa de dominio sin dependencias de WPF,
/// HelixToolkit ni DirectX. Es completamente testeable en xUnit del
/// proyecto core sin instanciar viewport ni hilo UI.
/// </para>
///
/// <para>
/// <b>Convención de la signatura</b>: la propuesta original del usuario
/// usaba <c>System.Windows.Media3D.Point3D</c> pero ese tipo vive en
/// PresentationCore (WPF) y violaría el aislamiento de <c>src.Core</c>.
/// Se mantiene la lógica idéntica pero la firma acepta <c>double mouseX,
/// double mouseY</c>; el code-behind del visor 3D extrae <c>Point3D.X/Y</c>
/// antes de invocar.
/// </para>
/// </summary>
public static class GridSnapEngine
{
    /// <summary>
    /// Radio por defecto de magnetización del cursor a una intersección
    /// (0.80 m). Si la distancia euclidiana 2D del cursor a la
    /// intersección más cercana es menor o igual a este radio, el
    /// cursor se "pega" a la intersección; si es mayor, se mueve
    /// libremente sobre el plano.
    /// </summary>
    public const double RadioToleranciaDefaultM = 0.80;

    /// <summary>
    /// Magnetiza un punto 2D del cursor del mouse a la intersección de
    /// ejes <c>(EjeX, EjeY)</c> más cercana de la grilla, si está dentro
    /// del <paramref name="radioToleranciaM"/>. Función pura: no muta
    /// la grilla, no produce I/O, sin afinidad de hilo UI.
    ///
    /// <para>
    /// <b>Algoritmo</b>:
    /// <list type="number">
    ///   <item>Si la grilla está vacía, retornar el punto del cursor sin
    ///   modificar (<c>Snapped = false</c>) — no hay nada a qué hacer
    ///   snap.</item>
    ///   <item>Encontrar el <see cref="EjeNombrado"/> de
    ///   <see cref="GrillaEstructural.EjesX"/> cuya <c>PosicionMetros</c>
    ///   minimice <c>|mouseX − pos|</c>. Guardar <c>x_cercano</c> y su
    ///   delta absoluto.</item>
    ///   <item>Repetir para <see cref="GrillaEstructural.EjesY"/> y
    ///   <c>mouseY</c>.</item>
    ///   <item>Calcular la distancia euclidiana 2D al punto de
    ///   intersección <c>(x_cercano, y_cercano)</c>:
    ///   <c>√(Δx² + Δy²)</c>.</item>
    ///   <item>Si la distancia ≤ <paramref name="radioToleranciaM"/>,
    ///   retornar <c>(x_cercano, y_cercano, true)</c> — magnetizado.
    ///   Si no, retornar <c>(mouseX, mouseY, false)</c> — libre.</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="mouseX">Coordenada X del cursor proyectada al plano del nivel activo (m).</param>
    /// <param name="mouseY">Coordenada Y del cursor proyectada al plano del nivel activo (m).</param>
    /// <param name="grilla">Grilla estructural del edificio activo.</param>
    /// <param name="radioToleranciaM">Distancia máxima euclidiana (m) para activar la magnetización. Default 0.80 m.</param>
    /// <returns>
    /// Tupla <c>(X, Y, Snapped)</c>: si <c>Snapped == true</c>, las
    /// coordenadas son las de la intersección magnetizada; si
    /// <c>false</c>, son las del cursor original.
    /// </returns>
    public static (double X, double Y, bool Snapped) CalcularSnapAGrilla(
        double mouseX, double mouseY,
        GrillaEstructural grilla,
        double radioToleranciaM = RadioToleranciaDefaultM)
    {
        if (grilla.EstaVacia)
            return (mouseX, mouseY, false);

        // Eje X más cercano por |Δx| (búsqueda lineal — siempre pocas
        // unidades de ejes en una grilla real, no merece estructuras
        // espaciales).
        double xCercano = grilla.EjesX[0].PosicionMetros;
        double mejorDx  = Math.Abs(mouseX - xCercano);
        for (int i = 1; i < grilla.EjesX.Count; i++)
        {
            double dx = Math.Abs(mouseX - grilla.EjesX[i].PosicionMetros);
            if (dx < mejorDx)
            {
                mejorDx  = dx;
                xCercano = grilla.EjesX[i].PosicionMetros;
            }
        }

        // Eje Y más cercano por |Δy|.
        double yCercano = grilla.EjesY[0].PosicionMetros;
        double mejorDy  = Math.Abs(mouseY - yCercano);
        for (int i = 1; i < grilla.EjesY.Count; i++)
        {
            double dy = Math.Abs(mouseY - grilla.EjesY[i].PosicionMetros);
            if (dy < mejorDy)
            {
                mejorDy  = dy;
                yCercano = grilla.EjesY[i].PosicionMetros;
            }
        }

        // Distancia euclidiana 2D al punto de intersección.
        double dist = Math.Sqrt(mejorDx * mejorDx + mejorDy * mejorDy);
        return dist <= radioToleranciaM
            ? (xCercano, yCercano, true)
            : (mouseX,   mouseY,   false);
    }
}
