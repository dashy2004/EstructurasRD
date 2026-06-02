using System;

namespace LosasPlus.Transmision;

/// <summary>
/// Predimensionado de una zapata aislada: el área y el lado requeridos para que
/// la <see cref="CargaServicio"/> no exceda la <see cref="PresionAdmisible"/>
/// del terreno.
/// </summary>
/// <param name="CargaServicio">Carga vertical que baja a la zapata (en t, consistente con la presión).</param>
/// <param name="PresionAdmisible">Capacidad portante admisible del terreno (en t/m²).</param>
/// <param name="AreaRequerida">Área mínima de contacto = carga / presión (en m²).</param>
/// <param name="LadoCuadrada">Lado de una zapata cuadrada de esa área = √área (en m).</param>
public readonly record struct ZapataPredim(
    double CargaServicio,
    double PresionAdmisible,
    double AreaRequerida,
    double LadoCuadrada);

/// <summary>
/// Predimensionado de zapatas (Fase J — paso terminal de la bajada de cargas
/// losa→viga→columna→<b>zapata</b>). Dada la carga vertical que llega a la
/// cimentación (p. ej. <c>BajadaCargas.CargaEnBase</c>) y la presión admisible
/// del terreno, estima el área de contacto y el lado de una zapata cuadrada por
/// el criterio de presión de servicio: <c>A = P / q_adm</c>.
///
/// <para>
/// Tipo <b>puro de dominio</b> — multiplataforma y testeable. <b>Unidades:</b>
/// carga y presión deben ser consistentes (p. ej. t y t/m² → área en m²).
/// <b>Nota de ingeniería:</b> el dimensionado por capacidad portante usa cargas
/// de <i>servicio</i> (no factorizadas); si la carga disponible es última (Wu),
/// el consumidor debe convertirla a servicio antes de llamar. Es un
/// predimensionado preliminar, no un diseño definitivo.
/// </para>
/// </summary>
public static class PredimZapata
{
    /// <summary>
    /// Predimensiona una zapata cuadrada para <paramref name="cargaServicio"/>
    /// sobre un terreno de presión admisible <paramref name="presionAdmisible"/>.
    /// Una carga o presión no positiva devuelve dimensiones nulas.
    /// </summary>
    public static ZapataPredim Cuadrada(double cargaServicio, double presionAdmisible)
    {
        if (cargaServicio <= 0 || presionAdmisible <= 0)
            return new ZapataPredim(Math.Max(0, cargaServicio), Math.Max(0, presionAdmisible), 0, 0);

        double area = cargaServicio / presionAdmisible;
        double lado = Math.Sqrt(area);
        return new ZapataPredim(cargaServicio, presionAdmisible, area, lado);
    }

    /// <summary>Factor por defecto de conversión carga última → servicio (≈ 1.2D+1.6L sobre D+L).</summary>
    public const double FactorUltimaServicioPorDefecto = 1.5;

    /// <summary>
    /// Predimensiona la zapata para una carga <b>última</b> (Wu, p. ej. la que
    /// baja del descenso de cargas), convirtiéndola a servicio dividiéndola por
    /// <paramref name="factorUltima"/> antes de aplicar el criterio de presión
    /// admisible (que usa cargas de servicio). Un factor no positivo usa la
    /// carga tal cual.
    /// </summary>
    public static ZapataPredim CuadradaDesdeUltima(
        double cargaUltima, double presionAdmisible, double factorUltima = FactorUltimaServicioPorDefecto)
    {
        double servicio = factorUltima > 0 ? cargaUltima / factorUltima : cargaUltima;
        return Cuadrada(servicio, presionAdmisible);
    }
}
