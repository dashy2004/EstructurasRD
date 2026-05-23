using System;
using System.Collections.Generic;
using System.Linq;
using LosasPlus.Columnas;

namespace LosasPlus.Services;

/// <summary>
/// Motor de diseño de columnas de concreto reforzado — construye el
/// <b>diagrama de interacción P-M uniaxial 2D</b> por compatibilidad de
/// deformaciones elasto-plásticas según ACI 318-19 (Fase 5, Iteración 1 de la
/// suite estructural).
///
/// <para>
/// Clase estática <b>pura</b> — sin estado ni dependencias de UI. Recorre los
/// estados cinemáticamente admisibles de la sección parametrizados por la
/// deformación εt del acero extremo en tracción, evalúa la (Mn, Pn) por
/// equilibrio del bloque equivalente de Whitney y aporte de cada capa de
/// acero, aplica el factor de reducción φ (0.65 → 0.90 según ACI §21.2) y el
/// tope axial de diseño <c>α·φ·P0</c> con α = 0.80 (columnas con estribos,
/// ACI §22.4.2.1).
/// </para>
///
/// <para>
/// Para cubrir tanto el ramal de momento positivo como el negativo del
/// diagrama —incluso en columnas asimétricas— el barrido se ejecuta dos veces:
/// una con las capas tal cual y otra con las profundidades reflejadas
/// <c>di' = Peralte − di</c>, invirtiendo el signo del momento del ramal
/// reflejado.
/// </para>
///
/// <para>
/// El dominio almacena la sección en m y MPa; el motor opera internamente en
/// N-mm-MPa y devuelve P en kN y M en kN·m. Convención de signos del resultado:
/// P positivo = compresión; M positivo = momento que tracciona la cara
/// inferior del peralte (consistente con el motor de vigas de la Fase 3).
/// </para>
/// </summary>
public static class ColumnaDesignEngine
{
    /// <summary>Deformación última útil del concreto εcu (ACI 318-19 §22.2.2.1).</summary>
    private const double DeformacionUltimaConcreto = 0.003;

    /// <summary>Módulo de elasticidad del acero de refuerzo Es, en MPa.</summary>
    private const double ModuloAcero = 200_000.0;

    /// <summary>Factor de área efectiva máxima de compresión axial para columnas con estribos (ACI 318-19 §22.4.2.1).</summary>
    private const double AlfaTiedColumns = 0.80;

    /// <summary>Factor de reducción de resistencia φ para sección controlada por compresión (estribos).</summary>
    private const double PhiCompresionControlada = 0.65;

    /// <summary>Número de muestras del barrido εt por cada uno de los dos ramales.</summary>
    private const int MuestrasPorRama = 49;

    /// <summary>
    /// Construye el diagrama de interacción P-M uniaxial 2D de la columna
    /// definida por <paramref name="seccion"/> y <paramref name="acero"/>.
    /// Devuelve <see cref="DiagramaInteraccion2D.Vacio"/> si la entrada es
    /// inválida (geometría no positiva, materiales no positivos, sin capas o
    /// acero total nulo).
    /// </summary>
    public static DiagramaInteraccion2D ConstruirDiagramaInteraccion2D(
        SeccionColumna seccion, RefuerzoColumna acero)
    {
        ArgumentNullException.ThrowIfNull(seccion);
        ArgumentNullException.ThrowIfNull(acero);

        if (seccion.Base <= 0.0 || seccion.Peralte <= 0.0
            || seccion.Fc <= 0.0 || acero.Fy <= 0.0
            || acero.Capas.Count == 0)
            return DiagramaInteraccion2D.Vacio;

        double astM2 = acero.Capas.Sum(c => c.Area);
        if (astM2 <= 0.0) return DiagramaInteraccion2D.Vacio;

        // --- Unidades internas: N-mm-MPa ---
        double b = seccion.Base * 1000.0;             // mm
        double h = seccion.Peralte * 1000.0;          // mm
        double fc = seccion.Fc;                       // MPa
        double fy = acero.Fy;                         // MPa
        double ey = fy / ModuloAcero;
        double beta1 = Beta1(fc);
        double ag = b * h;                            // mm²
        double astMm2 = astM2 * 1e6;                  // mm²

        // --- Anclas analíticas cerradas ---
        double p0Newton = 0.85 * fc * (ag - astMm2) + fy * astMm2;   // N (compresión positiva)
        double p0Kn = p0Newton / 1000.0;
        double tnKn = -fy * astMm2 / 1000.0;                          // kN (tracción negativa)

        // --- Tope axial de diseño (ACI 318-19 §22.4.2.1) ---
        double topeDisenoKn = AlfaTiedColumns * PhiCompresionControlada * p0Kn;

        // --- Capas en unidades internas ---
        var capasOriginales = acero.Capas
            .Select(c => (Di: c.Profundidad * 1000.0, Asi: c.Area * 1e6))
            .ToList();
        var capasReflejadas = capasOriginales
            .Select(c => (Di: h - c.Di, Asi: c.Asi))
            .ToList();

        // --- Barrido de los dos ramales ---
        var (nomRama1, disRama1) = BarrerRama(
            b, h, fc, fy, ey, beta1, capasOriginales, topeDisenoKn, signoM: +1.0);
        var (nomRama2, disRama2) = BarrerRama(
            b, h, fc, fy, ey, beta1, capasReflejadas, topeDisenoKn, signoM: -1.0);

        // --- Ensamblado en orden cíclico cerrado: P0 → ramal+ → Tn → ramal−
        //     invertido → cierre en P0. Permite que el consumidor grafique las
        //     curvas como un polígono continuo sin reordenar. Tn de diseño
        //     usa φ tensión-controlada (εt → ∞): 0.90·Tn.
        double tnDisenoKn = 0.90 * tnKn;

        var nominales = new List<PuntoInteraccion>(nomRama1.Count + nomRama2.Count + 3)
        {
            new(0.0, p0Kn),
        };
        nominales.AddRange(nomRama1);
        nominales.Add(new PuntoInteraccion(0.0, tnKn));
        nominales.AddRange(((IEnumerable<PuntoInteraccion>)nomRama2).Reverse());
        nominales.Add(new PuntoInteraccion(0.0, p0Kn));

        var disenos = new List<PuntoInteraccion>(disRama1.Count + disRama2.Count + 3)
        {
            new(0.0, topeDisenoKn),
        };
        disenos.AddRange(disRama1);
        disenos.Add(new PuntoInteraccion(0.0, tnDisenoKn));
        disenos.AddRange(((IEnumerable<PuntoInteraccion>)disRama2).Reverse());
        disenos.Add(new PuntoInteraccion(0.0, topeDisenoKn));

        return new DiagramaInteraccion2D(nominales, disenos, p0Kn, tnKn);
    }

    /// <summary>
    /// Verifica un punto de demanda <c>(mu, pu)</c> contra la frontera de
    /// diseño <c>CurvaDiseno</c> del <paramref name="diagrama"/> calculando
    /// el <b>ratio de eficiencia radial D/C</b> (Fase 5, Iteración 3).
    ///
    /// <para>
    /// Traza un rayo desde el origen <c>(0, 0)</c> que pasa por
    /// <c>(mu, pu)</c>, calcula su intersección con el polígono de diseño
    /// cerrado mediante un sistema lineal 2×2 por arista, y devuelve el
    /// ratio como <c>1.0 / t</c>, donde <c>t</c> es el parámetro del rayo
    /// tal que <c>(t·mu, t·pu)</c> cae sobre la frontera.
    /// </para>
    ///
    /// <para>
    /// Casos especiales: una demanda nula devuelve
    /// <see cref="ResultadoVerificacionColumna.Seguro"/>; un diagrama sin
    /// capacidad (vacío) o un caso degenerado donde el origen cae fuera del
    /// polígono devuelve <see cref="ResultadoVerificacionColumna.SinCapacidad"/>
    /// con <see cref="double.PositiveInfinity"/>.
    /// </para>
    /// </summary>
    /// <param name="diagrama">Diagrama P-M con la frontera de diseño cerrada.</param>
    /// <param name="mu">Momento flector último Mu, en kN·m (con signo).</param>
    /// <param name="pu">Fuerza axial última Pu, en kN (positiva = compresión).</param>
    public static ResultadoVerificacionColumna VerificarPuntoDemanda(
        DiagramaInteraccion2D diagrama, double mu, double pu)
    {
        ArgumentNullException.ThrowIfNull(diagrama);

        if (diagrama.CurvaDiseno.Count < 3)
            return ResultadoVerificacionColumna.SinCapacidad;

        // Demanda nula: la columna está descargada, ratio = 0 por definición.
        const double tolDemandaNula = 1e-9;
        if (Math.Abs(mu) < tolDemandaNula && Math.Abs(pu) < tolDemandaNula)
            return ResultadoVerificacionColumna.Seguro;

        var hit = IntersectarRayoConPoligono(diagrama.CurvaDiseno, mu, pu);
        if (hit is null) return ResultadoVerificacionColumna.SinCapacidad;

        double t = hit.Value.T;
        double ratio = 1.0 / t;
        return new ResultadoVerificacionColumna(
            RatioEficiencia: ratio,
            EsSeguro: ratio <= 1.0,
            PuntoCapacidad: hit.Value.Punto);
    }

    /// <summary>
    /// Encuentra el punto donde el rayo desde el origen en dirección
    /// <c>(mu, pu)</c> intersecta el polígono cerrado
    /// <paramref name="poligono"/>. Devuelve <c>(t, punto)</c> donde
    /// <c>t > 0</c> es el parámetro del rayo y <c>punto = (t·mu, t·pu)</c>,
    /// o <c>null</c> si no hay intersección válida (origen fuera del
    /// polígono o todas las aristas paralelas al rayo).
    /// </summary>
    private static (double T, PuntoInteraccion Punto)? IntersectarRayoConPoligono(
        IReadOnlyList<PuntoInteraccion> poligono, double mu, double pu)
    {
        const double tolParalela = 1e-12;
        const double tolPositiva = 1e-12;

        for (int i = 0; i < poligono.Count - 1; i++)
        {
            var a = poligono[i];
            var b = poligono[i + 1];
            double dmAB = b.Momento - a.Momento;
            double dpAB = b.Carga - a.Carga;

            // Sistema 2×2: t·mu − s·dmAB = a.M ; t·pu − s·dpAB = a.P.
            // det = mu·(-dpAB) - (-dmAB)·pu = pu·dmAB - mu·dpAB.
            double det = pu * dmAB - mu * dpAB;
            if (Math.Abs(det) < tolParalela) continue;   // rayo paralelo a la arista

            double t = (a.Carga * dmAB - a.Momento * dpAB) / det;
            double s = (mu * a.Carga - pu * a.Momento) / det;

            if (s >= 0.0 && s <= 1.0 && t > tolPositiva)
                return (t, new PuntoInteraccion(t * mu, t * pu));
        }

        return null;
    }

    // ----- Barrido de un ramal -----

    /// <summary>
    /// Barre los estados cinemáticamente admisibles parametrizados por εt y
    /// devuelve los pares (Mn, Pn) y (φMn, φPn) capeados para un ramal del
    /// diagrama. <paramref name="signoM"/> = +1 produce el ramal positivo;
    /// −1 invierte el signo del momento (ramal reflejado).
    /// </summary>
    private static (List<PuntoInteraccion> Nominal, List<PuntoInteraccion> Diseno) BarrerRama(
        double b, double h, double fc, double fy, double ey, double beta1,
        List<(double Di, double Asi)> capas, double topeDisenoKn, double signoM)
    {
        double dExtremo = capas.Max(c => c.Di);
        double ecu = DeformacionUltimaConcreto;
        double etMin = -ecu * 0.99;       // evita la singularidad en εt = −εcu (c → ∞)
        double etMax = ey + 0.05;          // bien dentro del régimen tensión-controlada

        var nominal = new List<PuntoInteraccion>(MuestrasPorRama);
        var diseno = new List<PuntoInteraccion>(MuestrasPorRama);

        for (int i = 0; i < MuestrasPorRama; i++)
        {
            double et = etMin + (etMax - etMin) * i / (MuestrasPorRama - 1);
            double c = ecu * dExtremo / (ecu + et);   // mm; (ecu + et) > 0 en la rejilla
            double a = Math.Min(beta1 * c, h);        // mm

            // --- Aporte de cada capa de acero ---
            double sumaAsiEnBloque = 0.0;
            double sumaFsiAsi = 0.0;            // N
            double sumaFsiAsiArmL = 0.0;        // N·mm
            foreach (var (di, asi) in capas)
            {
                double eti = ecu * (di - c) / c;                       // positiva = tracción
                double fsi = Math.Clamp(ModuloAcero * eti, -fy, fy);   // MPa
                sumaFsiAsi += fsi * asi;
                sumaFsiAsiArmL += fsi * asi * (h / 2.0 - di);
                if (di <= a) sumaAsiEnBloque += asi;
            }

            // --- Bloque de concreto (descontando el acero que desplaza) ---
            double cc = 0.85 * fc * (a * b - sumaAsiEnBloque);   // N

            // --- Equilibrio axial y de momento, sobre el centroide y = h/2 ---
            double pnKn = (cc - sumaFsiAsi) / 1000.0;                                       // kN
            double mnKnm = (cc * (h / 2.0 - a / 2.0) - sumaFsiAsiArmL) / 1.0e6;               // kN·m

            // --- Factor de reducción y tope axial de diseño ---
            double etExtremo = ecu * (dExtremo - c) / c;
            double phi = FactorReduccion(etExtremo, ey);
            double phiMnKnm = phi * mnKnm;
            double phiPnKn = phi * pnKn;
            if (phiPnKn > topeDisenoKn) phiPnKn = topeDisenoKn;

            nominal.Add(new PuntoInteraccion(signoM * mnKnm, pnKn));
            diseno.Add(new PuntoInteraccion(signoM * phiMnKnm, phiPnKn));
        }

        return (nominal, diseno);
    }

    // ----- Helpers privados (réplicas de RcDesignEngine, sin acoplar motores) -----

    /// <summary>
    /// Factor β1 del bloque equivalente de Whitney (ACI 318-19 §22.2.2.4.3).
    /// Réplica local del método homónimo de <c>RcDesignEngine</c>.
    /// </summary>
    private static double Beta1(double fc) =>
        fc <= 28.0 ? 0.85
        : fc < 56.0 ? 0.85 - 0.05 * (fc - 28.0) / 7.0
        : 0.65;

    /// <summary>
    /// Factor de reducción de resistencia φ en función de la deformación neta
    /// εt (ACI 318-19 §21.2): 0.65 en control de compresión, 0.90 en control
    /// de tracción, interpolación lineal en la transición. Asume columna con
    /// estribos.
    /// </summary>
    private static double FactorReduccion(double et, double ey)
    {
        double limiteTraccion = ey + 0.003;
        if (et >= limiteTraccion) return 0.90;
        if (et <= ey) return PhiCompresionControlada;
        return PhiCompresionControlada
             + (0.90 - PhiCompresionControlada) * (et - ey) / (limiteTraccion - ey);
    }
}
