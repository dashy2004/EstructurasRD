using LosasPlus.Models;
using LosasPlus.Transmision;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del reparto de carga losa→viga por áreas tributarias (Fase J.1).
/// Verifica las cargas de borde triangular/trapezoidal y la conservación de la
/// carga total del paño.
/// </summary>
public class RepartoCargaLosaTests
{
    [Fact]
    public void Pano_cuadrado_reparte_por_igual()
    {
        var r = RepartoCargaLosa.Calcular(5, 5, 10);
        Assert.Equal(62.5, r.BordeCorto.FuerzaTotal, 6);
        Assert.Equal(62.5, r.BordeLargo.FuerzaTotal, 6);
        Assert.Equal(12.5, r.BordeCorto.LineaUniformeEquivalente, 6);
        Assert.Equal(25.0, r.BordeCorto.IntensidadPico, 6);
        Assert.Equal(250.0, r.CargaTotal, 6);
    }

    [Fact]
    public void Pano_rectangular_da_triangulo_y_trapecio()
    {
        var r = RepartoCargaLosa.Calcular(4, 8, 10);
        Assert.Equal(4, r.LadoCorto, 6);
        Assert.Equal(8, r.LadoLargo, 6);

        // Borde corto (len 4): triángulo F = q·a²/4 = 40; w = 10; pico = 20.
        Assert.Equal(40.0, r.BordeCorto.FuerzaTotal, 6);
        Assert.Equal(10.0, r.BordeCorto.LineaUniformeEquivalente, 6);

        // Borde largo (len 8): trapecio F = q·(a/4)(2b−a) = 120; w = 15; pico = 20.
        Assert.Equal(120.0, r.BordeLargo.FuerzaTotal, 6);
        Assert.Equal(15.0, r.BordeLargo.LineaUniformeEquivalente, 6);
        Assert.Equal(20.0, r.BordeLargo.IntensidadPico, 6);
    }

    [Fact]
    public void Conserva_la_carga_total_del_pano()
    {
        var r = RepartoCargaLosa.Calcular(4, 8, 10);
        double suma = 2 * r.BordeCorto.FuerzaTotal + 2 * r.BordeLargo.FuerzaTotal;
        Assert.Equal(r.CargaTotal, suma, 6);
        Assert.Equal(320.0, r.CargaTotal, 6); // q·lx·ly = 10·4·8
    }

    [Fact]
    public void Es_simetrico_en_el_orden_de_lados()
    {
        var a = RepartoCargaLosa.Calcular(4, 8, 10);
        var b = RepartoCargaLosa.Calcular(8, 4, 10);
        Assert.Equal(a.BordeCorto.FuerzaTotal, b.BordeCorto.FuerzaTotal, 6);
        Assert.Equal(a.BordeLargo.FuerzaTotal, b.BordeLargo.FuerzaTotal, 6);
    }

    [Fact]
    public void Dimensiones_o_carga_no_positivas_dan_reparto_nulo()
    {
        Assert.Equal(0, RepartoCargaLosa.Calcular(0, 5, 10).CargaTotal, 6);
        Assert.Equal(0, RepartoCargaLosa.Calcular(5, 5, 0).CargaTotal, 6);
    }

    [Fact]
    public void La_sobrecarga_por_Losa_usa_Lx_Ly_y_Carga()
    {
        var r = RepartoCargaLosa.Calcular(new Losa { Lx = 4, Ly = 8, Carga = 10 });
        Assert.Equal(320.0, r.CargaTotal, 6);
        Assert.Equal(120.0, r.BordeLargo.FuerzaTotal, 6);
    }
}
