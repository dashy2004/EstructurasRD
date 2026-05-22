using System;
using System.Collections.Generic;
using LosasPlus.Rc;
using LosasPlus.Vigas;

namespace LosasPlus.Services;

/// <summary>
/// Motor de diseño de secciones de concreto reforzado a flexión simple por el
/// <b>Bloque Equivalente de Presiones de Whitney</b> (ACI 318-19) — Fase 4 de
/// la suite estructural.
///
/// <para>
/// Clase estática <b>pura</b> — sin estado ni dependencias de UI. Calcula la
/// capacidad nominal de una <see cref="SeccionRC"/>: profundidad del eje
/// neutro, bloque de compresión, cuantías, momento nominal Mn y momento de
/// diseño φMn, con el factor de reducción φ determinado por la deformación
/// neta del acero de tracción εt.
/// </para>
///
/// <para>
/// El dominio almacena la sección en m y MPa; el motor opera internamente en
/// N-mm-MPa y devuelve longitudes en m y momentos en kN·m (consistentes con el
/// motor de vigas de la Fase 3). El área de acero entra en m².
/// </para>
/// </summary>
public static class RcDesignEngine
{
    /// <summary>Módulo de elasticidad del acero de refuerzo Es, en MPa.</summary>
    private const double ModuloAcero = 200_000.0;

    /// <summary>Deformación última útil del concreto εcu (ACI 318-19 §22.2.2.1).</summary>
    private const double DeformacionUltimaConcreto = 0.003;

    /// <summary>Deformación neta mínima de ductilidad para flexión (ACI 318-19 §9.3.3.1).</summary>
    private const double DeformacionMinimaDuctilidad = 0.004;

    /// <summary>Tolerancia geométrica para localizar una estación dentro de un tramo (m).</summary>
    private const double ToleranciaTramo = 1e-6;

    /// <summary>
    /// Calcula la capacidad nominal a flexión de la <paramref name="seccion"/>
    /// armada con un área <paramref name="areaAceroTraccion"/> (en m²) de acero
    /// del lado en tracción.
    /// </summary>
    public static ResultadoCapacidadRC CalcularCapacidad(SeccionRC seccion, double areaAceroTraccion)
    {
        ArgumentNullException.ThrowIfNull(seccion);

        // --- Unidades internas: N-mm-MPa ---
        double b = seccion.Base * 1000.0;                                 // mm
        double d = (seccion.Peralte - seccion.Recubrimiento) * 1000.0;    // mm (peralte efectivo)
        double areaAcero = areaAceroTraccion * 1e6;                       // mm²
        double fc = seccion.Fc;                                           // MPa
        double fy = seccion.Fy;                                           // MPa

        // --- Guardas de entrada degenerada ---
        if (areaAceroTraccion <= 0.0)
            return ResultadoCapacidadRC.Invalida("El área de acero de tracción debe ser positiva.");
        if (b <= 0.0 || d <= 0.0)
            return ResultadoCapacidadRC.Invalida(
                "La geometría de la sección es inválida (base o peralte efectivo no positivos).");
        if (fc <= 0.0 || fy <= 0.0)
            return ResultadoCapacidadRC.Invalida("Las resistencias f'c y fy deben ser positivas.");

        // --- β1 del bloque equivalente (ACI 318-19 §22.2.2.4.3) ---
        double beta1 = fc <= 28.0 ? 0.85
                     : fc < 56.0 ? 0.85 - 0.05 * (fc - 28.0) / 7.0
                     : 0.65;

        double es = ModuloAcero;
        double ecu = DeformacionUltimaConcreto;
        double ey = fy / es;   // deformación de fluencia del acero

        // --- Equilibrio C = T suponiendo que el acero fluye ---
        double a = areaAcero * fy / (0.85 * fc * b);   // mm — bloque de compresión
        double c = a / beta1;                          // mm — eje neutro
        double et = ecu * (d - c) / c;                 // deformación neta de tracción
        bool aceroFluye = et >= ey;
        double traccion;                               // T, en N

        if (aceroFluye)
        {
            traccion = areaAcero * fy;
        }
        else
        {
            // El acero no fluye: equilibrio con fs = Es·εcu·(d−c)/c. Sustituyendo
            // a = β1·c en 0.85·fc·a·b = As·fs queda la cuadrática en c:
            //   (0.85·fc·β1·b)·c² + (As·Es·εcu)·c − (As·Es·εcu·d) = 0
            double ca = 0.85 * fc * beta1 * b;
            double cb = areaAcero * es * ecu;
            double cc = areaAcero * es * ecu * d;
            c = (-cb + Math.Sqrt(cb * cb + 4.0 * ca * cc)) / (2.0 * ca);   // raíz positiva
            a = beta1 * c;
            et = ecu * (d - c) / c;
            traccion = areaAcero * es * et;   // As·fs, con fs = Es·εt < fy
        }

        // --- Momento nominal y de diseño ---
        double brazoPalanca = d - a / 2.0;                       // mm
        double momentoNominal = traccion * brazoPalanca / 1e6;   // N·mm → kN·m
        double phi = FactorReduccion(et, ey);
        double momentoDiseno = phi * momentoNominal;             // kN·m

        // --- Cuantías (ACI 318-19) ---
        double rho = areaAcero / (b * d);
        double rhoMinima = Math.Max(0.25 * Math.Sqrt(fc) / fy, 1.4 / fy);
        double rhoMaxima = 0.85 * beta1 * (fc / fy)
                           * (ecu / (ecu + DeformacionMinimaDuctilidad));

        // --- Clasificación, banderas y advertencias ---
        bool cumpleCuantiaMinima = rho >= rhoMinima;
        bool cumpleLimiteDeformacion = et >= DeformacionMinimaDuctilidad;

        var advertencias = new List<string>();
        if (!cumpleCuantiaMinima)
            advertencias.Add($"La cuantía ρ = {rho:0.#####} es menor que ρ_min = {rhoMinima:0.#####}.");
        if (!cumpleLimiteDeformacion)
            advertencias.Add(
                $"La deformación neta εt = {et:0.#####} no alcanza el límite de ductilidad 0.004.");
        if (!aceroFluye)
            advertencias.Add("El acero de tracción no alcanza la fluencia (sección sobre-reforzada).");

        return new ResultadoCapacidadRC(
            EjeNeutro: c / 1000.0,
            BloqueCompresion: a / 1000.0,
            BrazoPalanca: brazoPalanca / 1000.0,
            Beta1: beta1,
            Cuantia: rho,
            CuantiaMinima: rhoMinima,
            CuantiaMaxima: rhoMaxima,
            MomentoNominal: momentoNominal,
            MomentoDiseno: momentoDiseno,
            FactorPhi: phi,
            DeformacionNetaTraccion: et,
            AceroFluye: aceroFluye,
            Clasificacion: Clasificar(et, ey),
            CumpleCuantiaMinima: cumpleCuantiaMinima,
            CumpleLimiteDeformacion: cumpleLimiteDeformacion,
            Advertencias: advertencias);
    }

    /// <summary>
    /// Calcula la capacidad usando el <paramref name="refuerzo"/> de la sección:
    /// para momento positivo trabaja el acero inferior; para momento negativo,
    /// el superior.
    /// </summary>
    public static ResultadoCapacidadRC CalcularCapacidad(
        SeccionRC seccion, RefuerzoLongitudinal refuerzo, bool momentoPositivo)
    {
        ArgumentNullException.ThrowIfNull(seccion);
        ArgumentNullException.ThrowIfNull(refuerzo);
        double area = momentoPositivo ? refuerzo.AreaAceroBot : refuerzo.AreaAceroTop;
        return CalcularCapacidad(seccion, area);
    }

    /// <summary>
    /// Verifica la demanda/capacidad a flexión de la <paramref name="viga"/>
    /// completa: en cada estación de la <paramref name="envolvente"/> de diseño
    /// compara el momento último gobernante contra la capacidad minorada φMn de
    /// la sección del tramo que contiene esa estación, y devuelve el perfil de
    /// ratios D/C a lo largo del eje longitudinal junto con el veredicto global
    /// (Fase 4, Iteración 3).
    ///
    /// <para>
    /// En cada estación se evalúan las dos direcciones: la demanda positiva
    /// (momento de tracción inferior) contra el φMn del acero inferior y la
    /// demanda negativa (tracción superior) contra el φMn del acero superior.
    /// El ratio de la estación es el mayor de los dos D/C; la dirección
    /// gobernante aporta el <see cref="PuntoVerificacionRc.MomentoUltimo"/> con
    /// signo y las banderas normativas. Si la capacidad de diseño es nula con
    /// demanda no nula, el ratio es <see cref="double.PositiveInfinity"/>.
    /// </para>
    ///
    /// <para>
    /// La capacidad se precalcula una sola vez por tramo y dirección — la
    /// sección es constante dentro de cada tramo. Una estación que cae
    /// exactamente sobre la frontera entre dos tramos se asigna al tramo
    /// inferior. La verificación cubre sólo flexión: no incluye cortante ni
    /// torsión.
    /// </para>
    /// </summary>
    /// <param name="viga">La viga cuyos tramos aportan las secciones de capacidad.</param>
    /// <param name="envolvente">
    /// La envolvente de diseño de la Fase 3 — la demanda última por estación.
    /// </param>
    /// <returns>
    /// El perfil de ratios D/C; <see cref="VerificacionViga.Vacia"/> si la viga
    /// no tiene tramos o la envolvente no tiene estaciones.
    /// </returns>
    public static VerificacionViga VerificarVigaCompleta(Viga viga, EnvolventeViga envolvente)
    {
        ArgumentNullException.ThrowIfNull(viga);
        ArgumentNullException.ThrowIfNull(envolvente);

        var tramos = viga.Tramos;
        if (tramos.Count == 0 || envolvente.Puntos.Count == 0)
            return VerificacionViga.Vacia;

        // --- Fronteras de tramo: sumas acumuladas de la longitud ---
        var fronteras = new double[tramos.Count + 1];
        for (int i = 0; i < tramos.Count; i++)
            fronteras[i + 1] = fronteras[i] + tramos[i].Longitud;

        // --- Capacidad φMn por tramo y dirección: la sección es constante en
        //     cada tramo, así que se calcula una sola vez cada una ---
        var capacidadPositiva = new ResultadoCapacidadRC[tramos.Count];
        var capacidadNegativa = new ResultadoCapacidadRC[tramos.Count];
        for (int i = 0; i < tramos.Count; i++)
        {
            capacidadPositiva[i] = CalcularCapacidad(
                tramos[i].Seccion, tramos[i].Refuerzo, momentoPositivo: true);
            capacidadNegativa[i] = CalcularCapacidad(
                tramos[i].Seccion, tramos[i].Refuerzo, momentoPositivo: false);
        }

        // --- Verificación estación a estación de la envolvente ---
        var puntos = new List<PuntoVerificacionRc>(envolvente.Puntos.Count);
        double ratioMaximo = 0.0;
        foreach (var punto in envolvente.Puntos)
        {
            int tramo = IndiceTramoEn(fronteras, punto.X);
            var capPositiva = capacidadPositiva[tramo];
            var capNegativa = capacidadNegativa[tramo];

            // Demanda gobernante por dirección, en magnitud no negativa:
            // positiva a tracción inferior, negativa a tracción superior.
            double demandaPositiva = Math.Max(0.0, punto.MomentoMaximo);
            double demandaNegativa = Math.Max(0.0, -punto.MomentoMinimo);

            double dcPositivo = Ratio(demandaPositiva, capPositiva.MomentoDiseno);
            double dcNegativo = Ratio(demandaNegativa, capNegativa.MomentoDiseno);

            // La dirección de mayor D/C gobierna; en empate gobierna la positiva.
            bool gobiernaPositivo = dcPositivo >= dcNegativo;
            double ratio = gobiernaPositivo ? dcPositivo : dcNegativo;
            var capGobernante = gobiernaPositivo ? capPositiva : capNegativa;

            puntos.Add(new PuntoVerificacionRc(
                X: punto.X,
                MomentoUltimo: gobiernaPositivo ? demandaPositiva : -demandaNegativa,
                MomentoResistente: capGobernante.MomentoDiseno,
                RatioDemandaCapacidad: ratio,
                CumpleCuantiaMinima: capGobernante.CumpleCuantiaMinima,
                CumpleLimiteDeformacion: capGobernante.CumpleLimiteDeformacion));

            if (ratio > ratioMaximo) ratioMaximo = ratio;
        }

        return new VerificacionViga(puntos, ratioMaximo, ratioMaximo <= 1.0);
    }

    /// <summary>
    /// Factor de reducción de resistencia φ en función de la deformación neta
    /// εt (ACI 318-19 §21.2): 0.65 en control de compresión, 0.90 en control de
    /// tracción, interpolación lineal en la transición.
    /// </summary>
    private static double FactorReduccion(double et, double ey)
    {
        double limiteTraccion = ey + 0.003;
        if (et >= limiteTraccion) return 0.90;
        if (et <= ey) return 0.65;
        return 0.65 + 0.25 * (et - ey) / (limiteTraccion - ey);
    }

    /// <summary>Clasifica la sección según la deformación neta de tracción εt.</summary>
    private static ClasificacionSeccion Clasificar(double et, double ey)
    {
        double limiteTraccion = ey + 0.003;
        if (et >= limiteTraccion) return ClasificacionSeccion.TensionControlada;
        if (et <= ey) return ClasificacionSeccion.CompresionControlada;
        return ClasificacionSeccion.Transicion;
    }

    /// <summary>
    /// Ratio demanda/capacidad de una dirección: la <paramref name="demanda"/>
    /// dividida entre el momento de diseño <paramref name="momentoDiseno"/>. Es
    /// 0 cuando no hay demanda y <see cref="double.PositiveInfinity"/> cuando
    /// hay demanda pero la sección no aporta capacidad de diseño.
    /// </summary>
    private static double Ratio(double demanda, double momentoDiseno)
    {
        if (demanda <= 0.0) return 0.0;
        if (momentoDiseno <= 0.0) return double.PositiveInfinity;
        return demanda / momentoDiseno;
    }

    /// <summary>
    /// Índice del tramo que contiene la abscisa <paramref name="x"/>. Una
    /// estación sobre la frontera entre dos tramos se asigna al tramo inferior.
    /// Réplica local de la localización de tramo de <c>VigaContinuaEngine</c>
    /// —cuyo método equivalente es privado— para no acoplar los dos motores.
    /// </summary>
    private static int IndiceTramoEn(double[] fronteras, double x)
    {
        for (int i = 0; i < fronteras.Length - 1; i++)
            if (x <= fronteras[i + 1] + ToleranciaTramo)
                return i;
        return fronteras.Length - 2;
    }
}
