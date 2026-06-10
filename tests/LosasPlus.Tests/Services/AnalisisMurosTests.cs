using System.Linq;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de <see cref="AnalisisMuros"/> — la asociación muro→losa y el
/// desglose de longitudes para la leyenda «Suma de Colores» (Epic v1.4.0,
/// Módulo Muros v0.9.0).
/// </summary>
public class AnalisisMurosTests
{
    private static Losa LosaEn(int id, double x, double y, double lx = 4.0, double ly = 4.0)
        => new() { Id = id, Lx = lx, Ly = ly, CoordenadaX = x, CoordenadaY = y, Anclada = true };

    private static Muro MuroDe(double x1, double y1, double x2, double y2, double espesor = 0.15)
        => new()
        {
            PuntoInicio = new PuntoCad(x1, y1),
            PuntoFin = new PuntoCad(x2, y2),
            Espesor = espesor,
        };

    [Fact]
    public void Sistema_null_o_sin_muros_devuelve_resumen_vacio()
    {
        Assert.Empty(AnalisisMuros.Resumir(null).Entradas);

        var s = new Sistema();
        s.Losas.Add(LosaEn(1, 0, 0));
        Assert.Empty(AnalisisMuros.Resumir(s).Entradas);
    }

    [Fact]
    public void Muro_sobre_arista_de_una_losa_se_asocia_a_esa_losa()
    {
        var s = new Sistema();
        s.Losas.Add(LosaEn(1, 0, 0));          // rect [0,4]×[0,4]
        s.Muros.Add(MuroDe(0, 0, 0, 4));        // arista izquierda de la losa 1

        var r = AnalisisMuros.Resumir(s);

        Assert.Single(r.Entradas);
        Assert.Equal(1, r.Entradas[0].LosaId);
        Assert.Equal(4.0, r.Entradas[0].LongitudTotal, precision: 6);
    }

    [Fact]
    public void Muro_sobre_arista_compartida_cuenta_para_ambas_losas()
    {
        var s = new Sistema();
        s.Losas.Add(LosaEn(1, 0, 0));          // rect [0,4]×[0,4]
        s.Losas.Add(LosaEn(2, 4, 0));          // rect [4,8]×[0,4] — comparte la arista x=4
        s.Muros.Add(MuroDe(4, 0, 4, 4));        // muro sobre la arista compartida

        var r = AnalisisMuros.Resumir(s);

        Assert.Equal(2, r.Entradas.Count);
        Assert.Contains(r.Entradas, e => e.LosaId == 1);
        Assert.Contains(r.Entradas, e => e.LosaId == 2);
        Assert.All(r.Entradas, e => Assert.Equal(4.0, e.LongitudTotal, precision: 6));
    }

    [Fact]
    public void Muro_lejano_de_toda_losa_va_al_bucket_sin_asignar()
    {
        var s = new Sistema();
        s.Losas.Add(LosaEn(1, 0, 0));
        s.Muros.Add(MuroDe(100, 100, 100, 104));   // lejos de cualquier losa

        var r = AnalisisMuros.Resumir(s);

        Assert.Single(r.Entradas);
        Assert.Null(r.Entradas[0].LosaId);
    }

    [Fact]
    public void Dos_muros_del_mismo_espesor_en_una_losa_suman_longitudes()
    {
        var s = new Sistema();
        s.Losas.Add(LosaEn(1, 0, 0));
        s.Muros.Add(MuroDe(0, 0, 0, 4, espesor: 0.15));   // arista izquierda
        s.Muros.Add(MuroDe(0, 0, 4, 0, espesor: 0.15));   // arista superior

        var r = AnalisisMuros.Resumir(s);

        Assert.Single(r.Entradas);
        Assert.Equal(1, r.Entradas[0].LosaId);
        Assert.Equal(0.15, r.Entradas[0].Espesor, precision: 6);
        Assert.Equal(8.0, r.Entradas[0].LongitudTotal, precision: 6);
        Assert.Equal(2, r.Entradas[0].Cantidad);
    }

    [Fact]
    public void Distintos_espesores_en_la_misma_losa_dan_entradas_separadas()
    {
        var s = new Sistema();
        s.Losas.Add(LosaEn(1, 0, 0));
        s.Muros.Add(MuroDe(0, 0, 0, 4, espesor: 0.15));
        s.Muros.Add(MuroDe(0, 0, 4, 0, espesor: 0.20));

        var r = AnalisisMuros.Resumir(s);

        Assert.Equal(2, r.Entradas.Count);
        Assert.Contains(r.Entradas, e => e.Espesor == 0.15);
        Assert.Contains(r.Entradas, e => e.Espesor == 0.20);
    }

    [Fact]
    public void Longitud_del_muro_es_la_distancia_euclidea()
    {
        var muro = MuroDe(0, 0, 3, 4);   // triángulo 3-4-5
        Assert.Equal(5.0, muro.Longitud, precision: 9);
    }
}
