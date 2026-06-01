using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de <see cref="TopologiaAdyacencia"/> — la detección de caras
/// compartidas y de los offsets dₙ de la FASE A v1.1. Cubre los casos extremos
/// pedidos: bordes perfectamente alineados, losas desplazadas en Y que comparten
/// un trozo de cara en X, y losas que se rozan sólo por una esquina.
/// </summary>
public class TopologiaAdyacenciaTests
{
    [Fact]
    public void Bordes_perfectamente_alineados_dan_Total()
    {
        var info = TopologiaAdyacencia.Analizar(new RectM(0, 0, 4, 3), new RectM(4, 0, 3, 3));
        Assert.Equal(ClasificacionAdyacencia.Total, info.Clasificacion);
        Assert.True(info.EsBordeX);
        Assert.Equal(3, info.LongitudCaraCompartida, precision: 6);
        Assert.Equal(0, info.DesfaseSuperior, precision: 6);
        Assert.Equal(0, info.DesfaseInferior, precision: 6);
    }

    [Fact]
    public void Losas_desplazadas_en_Y_comparten_cara_parcial_en_X()
    {
        // a cubre Y 0..3; b cubre Y 1..4 → comparten Y 1..3 (cara parcial de 2 m).
        var info = TopologiaAdyacencia.Analizar(new RectM(0, 0, 4, 3), new RectM(4, 1, 3, 3));
        Assert.Equal(ClasificacionAdyacencia.Parcial, info.Clasificacion);
        Assert.True(info.EsBordeX);
        Assert.Equal(2, info.LongitudCaraCompartida, precision: 6);
        Assert.Equal(1, info.DesfaseSuperior, precision: 6);
        Assert.Equal(1, info.DesfaseInferior, precision: 6);
    }

    [Fact]
    public void Roce_solo_por_una_esquina_no_da_cara_compartida()
    {
        // a: X 0..4, Y 0..3 ; b: X 4..7, Y 3..6 → sólo se tocan en el punto (4,3).
        var info = TopologiaAdyacencia.Analizar(new RectM(0, 0, 4, 3), new RectM(4, 3, 3, 3));
        Assert.Equal(ClasificacionAdyacencia.DesfaseInferior, info.Clasificacion);
        Assert.Equal(0, info.LongitudCaraCompartida, precision: 6);
    }

    [Fact]
    public void Losas_alineadas_pero_separadas_en_X_dan_Holgura()
    {
        var info = TopologiaAdyacencia.Analizar(new RectM(0, 0, 4, 3), new RectM(4.5, 0, 3, 3));
        Assert.Equal(ClasificacionAdyacencia.Holgura, info.Clasificacion);
        Assert.Equal(0.5, info.Holgura, precision: 6);
    }

    [Fact]
    public void Adyacencia_vertical_en_Y_se_detecta()
    {
        var info = TopologiaAdyacencia.Analizar(new RectM(0, 0, 4, 3), new RectM(0, 3, 4, 3));
        Assert.Equal(ClasificacionAdyacencia.Total, info.Clasificacion);
        Assert.False(info.EsBordeX);
    }

    [Fact]
    public void Adyacencia_en_Y_con_desfase_en_X_es_parcial()
    {
        var info = TopologiaAdyacencia.Analizar(new RectM(0, 0, 4, 3), new RectM(1, 3, 4, 3));
        Assert.Equal(ClasificacionAdyacencia.Parcial, info.Clasificacion);
        Assert.False(info.EsBordeX);
        Assert.Equal(1, info.DesfaseSuperior, precision: 6);
    }

    [Fact]
    public void Losas_lejanas_no_tienen_relacion()
    {
        var info = TopologiaAdyacencia.Analizar(new RectM(0, 0, 4, 3), new RectM(20, 20, 3, 3));
        Assert.Equal(ClasificacionAdyacencia.SinRelacion, info.Clasificacion);
    }

    [Fact]
    public void El_signo_del_desfase_distingue_arriba_de_abajo()
    {
        // b corrida hacia abajo → desfase superior positivo.
        var abajo = TopologiaAdyacencia.Analizar(new RectM(0, 0, 4, 3), new RectM(4, 1, 3, 3));
        Assert.True(abajo.DesfaseSuperior > 0);
        // b corrida hacia arriba → desfase superior negativo.
        var arriba = TopologiaAdyacencia.Analizar(new RectM(0, 0, 4, 3), new RectM(4, -1, 3, 3));
        Assert.True(arriba.DesfaseSuperior < 0);
    }

    [Fact]
    public void Un_desfase_dentro_de_la_tolerancia_se_considera_alineado()
    {
        var dentro = TopologiaAdyacencia.Analizar(new RectM(0, 0, 4, 3), new RectM(4, 0.02, 3, 3));
        Assert.Equal(ClasificacionAdyacencia.Total, dentro.Clasificacion);
        var fuera = TopologiaAdyacencia.Analizar(new RectM(0, 0, 4, 3), new RectM(4, 0.20, 3, 3));
        Assert.Equal(ClasificacionAdyacencia.Parcial, fuera.Clasificacion);
    }

    [Fact]
    public void AnalizarVecindario_evalua_cada_vecina_con_su_indice()
    {
        var activa = new RectM(0, 0, 4, 3);
        var otras = new[]
        {
            new RectM(4, 0, 3, 3),     // 0 — adyacente total en X
            new RectM(20, 20, 2, 2),   // 1 — sin relación
            new RectM(0, 3, 4, 3),     // 2 — adyacente total en Y
        };
        var infos = TopologiaAdyacencia.AnalizarVecindario(activa, otras);
        Assert.Equal(3, infos.Count);
        Assert.Equal(0, infos[0].IndiceVecina);
        Assert.Equal(ClasificacionAdyacencia.Total, infos[0].Clasificacion);
        Assert.Equal(ClasificacionAdyacencia.SinRelacion, infos[1].Clasificacion);
        Assert.Equal(2, infos[2].IndiceVecina);
        Assert.False(infos[2].EsBordeX);
    }
}
