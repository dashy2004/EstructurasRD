using System.Collections.Generic;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Transmision;

namespace LosasPlus.Vigas;

/// <summary>
/// Genera <see cref="Viga"/> ya cargadas a partir de la geometría del edificio
/// (Sesión E), para que el editor de vigas muestre sus diagramas M/V/δ y su
/// sección sin que el usuario las construya a mano. Es el puente que faltaba
/// entre el reparto de cargas losa→viga (<c>RepartoCargaLosa</c>) y el motor
/// analítico (<c>VigaContinuaEngine</c>).
///
/// <para>Tipo <b>puro de dominio</b> — sin dependencias de UI, testeable.</para>
/// </summary>
public static class GeneradorVigas
{
    /// <summary>Conversión de tonelada-fuerza a kilonewton (1 tonf = 9.80665 kN).</summary>
    public const double TonF_a_KN = 9.80665;

    /// <summary>Prefijo del <see cref="Viga.Nombre"/> de las vigas auto-generadas (para regenerar sin duplicar).</summary>
    public const string PrefijoAuto = "V-auto";

    /// <summary>
    /// Construye una viga simplemente apoyada de un solo tramo: dos apoyos
    /// <see cref="TipoApoyo.Fijo"/> en los extremos y una carga
    /// <see cref="TipoCargaElemento.Distribuida"/> uniforme sobre todo el tramo.
    /// </summary>
    /// <param name="longitud">Luz de la viga, en metros.</param>
    /// <param name="cargaDistribuida">Carga lineal uniforme (kN/m, unidad del motor de vigas).</param>
    /// <param name="codigoCaso">Caso de carga al que se vincula la carga (p. ej. «D», «L»).</param>
    public static Viga VigaSimplementeApoyada(double longitud, double cargaDistribuida, string codigoCaso)
    {
        var viga = new Viga();

        var tramo = new TramoViga { Longitud = longitud };
        tramo.Cargas.Add(new CargaElemento
        {
            Tipo = TipoCargaElemento.Distribuida,
            Magnitud = cargaDistribuida,
            CodigoCaso = codigoCaso,
        });
        viga.Tramos.Add(tramo);

        viga.Apoyos.Add(new ApoyoViga { CoordenadaX = 0.0, Tipo = TipoApoyo.Fijo });
        viga.Apoyos.Add(new ApoyoViga { CoordenadaX = longitud, Tipo = TipoApoyo.Fijo });

        return viga;
    }

    /// <summary>
    /// Construye una <b>viga continua</b> de varios tramos (WS1-B): <c>N</c> tramos
    /// con las luces <paramref name="luces"/> (m) y sus cargas distribuidas
    /// <paramref name="cargas"/> (kN/m), apoyados sobre <c>N+1</c> apoyos
    /// <see cref="TipoApoyo.Fijo"/> en posiciones acumuladas (0, L₁, L₁+L₂, …). Es
    /// la base para materializar vigas continuas reales; la analiza
    /// <c>VigaContinuaEngine</c>. Entradas nulas, vacías, de distinto largo o con
    /// alguna luz no positiva devuelven una viga vacía.
    /// </summary>
    public static Viga VigaContinua(IReadOnlyList<double> luces, IReadOnlyList<double> cargas, string codigoCaso = "D")
    {
        var viga = new Viga();
        if (luces is null || cargas is null || luces.Count == 0 || luces.Count != cargas.Count)
            return viga;
        if (luces.Any(l => l <= 0))
            return viga;

        viga.Apoyos.Add(new ApoyoViga { CoordenadaX = 0.0, Tipo = TipoApoyo.Fijo });
        double x = 0.0;
        for (int i = 0; i < luces.Count; i++)
        {
            var tramo = new TramoViga { Longitud = luces[i] };
            tramo.Cargas.Add(new CargaElemento
            {
                Tipo = TipoCargaElemento.Distribuida,
                Magnitud = cargas[i],
                CodigoCaso = codigoCaso,
            });
            viga.Tramos.Add(tramo);

            x += luces[i];
            viga.Apoyos.Add(new ApoyoViga { CoordenadaX = x, Tipo = TipoApoyo.Fijo });
        }
        return viga;
    }

    /// <summary>
    /// Genera las cuatro vigas de apoyo de un paño de losa, cargadas con la carga
    /// tributaria que cada borde recibe por áreas tributarias
    /// (<see cref="RepartoCargaLosa"/>): dos vigas de la luz corta y dos de la luz
    /// larga. La carga superficial de la losa (<see cref="Losa.Carga"/>, en
    /// ton/m²) se reparte a línea (ton/m) y se convierte a kN/m para el motor de
    /// vigas.
    ///
    /// <para>
    /// Aproximación: la carga triangular/trapezoidal real se modela como la carga
    /// <b>uniforme equivalente</b> que conserva la fuerza total (el modelo de
    /// dominio sólo soporta cargas distribuidas uniformes). Una losa con
    /// dimensiones o carga no positivas devuelve una lista vacía.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Viga> VigasDeLosa(Losa losa, string codigoCaso = "D")
    {
        var vigas = new List<Viga>();
        if (losa is null) return vigas;

        var reparto = RepartoCargaLosa.Calcular(losa);
        if (reparto.CargaTotal <= 0) return vigas;

        double wCorto = reparto.BordeCorto.LineaUniformeEquivalente * TonF_a_KN;
        double wLargo = reparto.BordeLargo.LineaUniformeEquivalente * TonF_a_KN;

        vigas.Add(VigaSimplementeApoyada(reparto.LadoCorto, wCorto, codigoCaso));
        vigas.Add(VigaSimplementeApoyada(reparto.LadoCorto, wCorto, codigoCaso));
        vigas.Add(VigaSimplementeApoyada(reparto.LadoLargo, wLargo, codigoCaso));
        vigas.Add(VigaSimplementeApoyada(reparto.LadoLargo, wLargo, codigoCaso));
        return vigas;
    }

    /// <summary>
    /// Materializa las vigas de apoyo de todas las losas de un <paramref name="nivel"/>
    /// y las incorpora a <see cref="Nivel.Vigas"/>, de modo que el editor de vigas
    /// muestre sus diagramas sin construcción manual. Es <b>idempotente</b>: cada
    /// llamada elimina primero las vigas auto-generadas de una corrida anterior
    /// (las que llevan <see cref="PrefijoAuto"/> en el nombre) y preserva las que
    /// el usuario haya creado a mano. Devuelve las vigas generadas en esta corrida.
    /// </summary>
    public static IReadOnlyList<Viga> MaterializarVigas(Nivel nivel, string codigoCaso = "D")
    {
        var generadas = new List<Viga>();
        if (nivel is null) return generadas;

        foreach (var previa in nivel.Vigas.Where(EsAutoGenerada).ToList())
            nivel.Vigas.Remove(previa);

        int n = 1;
        foreach (var sistema in nivel.Sistemas)
            foreach (var losa in sistema.Losas)
                foreach (var viga in VigasDeLosa(losa, codigoCaso))
                {
                    viga.Nombre = $"{PrefijoAuto} {n++}";
                    nivel.Vigas.Add(viga);
                    generadas.Add(viga);
                }

        return generadas;
    }

    /// <summary>
    /// Materializa las vigas de <b>todos los niveles</b> de un edificio (ver la
    /// sobrecarga por nivel, idempotente). Es lo que dispararía un único botón
    /// «Generar vigas del edificio». Devuelve todas las vigas generadas.
    /// </summary>
    public static IReadOnlyList<Viga> MaterializarVigas(Edificio edificio, string codigoCaso = "D")
    {
        var generadas = new List<Viga>();
        if (edificio is null) return generadas;

        foreach (var nivel in edificio.Niveles)
            generadas.AddRange(MaterializarVigas(nivel, codigoCaso));

        return generadas;
    }

    private static bool EsAutoGenerada(Viga viga)
        => viga.Nombre is not null && viga.Nombre.StartsWith(PrefijoAuto);
}
