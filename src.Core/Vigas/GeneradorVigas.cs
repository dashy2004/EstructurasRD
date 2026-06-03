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
}
