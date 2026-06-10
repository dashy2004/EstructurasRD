using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Bug reportado (2026-06-10): al estirar una viga en el lienzo de planta los
/// apoyos quedaban en su CoordenadaX absoluta original — fuera de la viga o en
/// proporciones incorrectas. ReescalarApoyos mantiene la posición RELATIVA de
/// cada apoyo (un apoyo al 50% sigue al 50%; los extremos siguen en los extremos).
/// </summary>
public class VigaReescalarApoyosTests
{
    private static Viga VigaConApoyos(double longitud, params double[] apoyosX)
    {
        var v = new Viga { Id = 1, Nombre = "V-1" };
        v.Tramos.Add(new TramoViga { Longitud = longitud });
        foreach (var x in apoyosX) v.Apoyos.Add(new ApoyoViga(x, TipoApoyo.Fijo));
        return v;
    }

    [Fact]
    public void Estirar_la_viga_mantiene_los_apoyos_en_su_posicion_relativa()
    {
        var v = VigaConApoyos(6.0, 0.0, 3.0, 6.0);

        v.Tramos[0].Longitud = 8.0;     // gesto de estirar en el lienzo
        v.ReescalarApoyos(longitudAnterior: 6.0);

        Assert.Equal(0.0, v.Apoyos[0].CoordenadaX, 9);
        Assert.Equal(4.0, v.Apoyos[1].CoordenadaX, 9);  // 50% de 8
        Assert.Equal(8.0, v.Apoyos[2].CoordenadaX, 9);  // extremo sigue en el extremo
    }

    [Fact]
    public void Encoger_la_viga_tambien_reescala()
    {
        var v = VigaConApoyos(8.0, 0.0, 8.0);
        v.Tramos[0].Longitud = 5.0;
        v.ReescalarApoyos(longitudAnterior: 8.0);
        Assert.Equal(0.0, v.Apoyos[0].CoordenadaX, 9);
        Assert.Equal(5.0, v.Apoyos[1].CoordenadaX, 9);
    }

    [Fact]
    public void Longitud_anterior_no_positiva_no_hace_nada()
    {
        var v = VigaConApoyos(6.0, 0.0, 6.0);
        v.ReescalarApoyos(longitudAnterior: 0.0);
        Assert.Equal(6.0, v.Apoyos[1].CoordenadaX, 9);  // intacto, sin división por cero
    }
}
