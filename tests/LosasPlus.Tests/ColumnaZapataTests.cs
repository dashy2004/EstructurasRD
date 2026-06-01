using System.Collections.Generic;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de las entidades de dominio Columna y Zapata (Fase J.5): geometría
/// computada y notificación de cambios.
/// </summary>
public class ColumnaZapataTests
{
    [Fact]
    public void Columna_calcula_area_de_seccion_y_recalcula_al_cambiar()
    {
        var c = new Columna { Base = 0.4, Peralte = 0.5 };
        Assert.Equal(0.2, c.AreaSeccion, 9);
        c.Base = 0.3;
        Assert.Equal(0.15, c.AreaSeccion, 9);
    }

    [Fact]
    public void Columna_notifica_Base_y_AreaSeccion()
    {
        var c = new Columna();
        var notificadas = new List<string?>();
        c.PropertyChanged += (_, e) => notificadas.Add(e.PropertyName);
        c.Base = 0.35;
        Assert.Contains("Base", notificadas);
        Assert.Contains("AreaSeccion", notificadas);
    }

    [Fact]
    public void Zapata_calcula_area_de_contacto_y_recalcula_al_cambiar()
    {
        var z = new Zapata { Ancho = 2, Largo = 3 };
        Assert.Equal(6, z.AreaContacto, 9);
        z.Largo = 4;
        Assert.Equal(8, z.AreaContacto, 9);
    }

    [Fact]
    public void Columna_lleva_su_zapata()
    {
        var c = new Columna();
        Assert.Null(c.Zapata);
        c.Zapata = new Zapata { Ancho = 1.5, Largo = 1.5 };
        Assert.NotNull(c.Zapata);
        Assert.Equal(2.25, c.Zapata!.AreaContacto, 9);
    }
}
