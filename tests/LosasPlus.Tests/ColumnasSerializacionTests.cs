using System;
using LosasPlus.Models;
using LosasPlus.Persistence;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de serialización de las columnas/zapatas en el .lpx.json (Fase J.6):
/// round-trip, exclusión de propiedades computadas y carga aditiva.
/// </summary>
public class ColumnasSerializacionTests
{
    private static Proyecto ProyectoConColumna()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.AsegurarEstructura();
        p.Edificios[0].Niveles[0].Columnas.Add(new Columna
        {
            Id = 1, Nombre = "C-1", CoordenadaX = 2, CoordenadaY = 3,
            Base = 0.4, Peralte = 0.5, Altura = 3.2,
            Zapata = new Zapata { Nombre = "Z-1", Ancho = 2, Largo = 2.5, Peralte = 0.5 }
        });
        return p;
    }

    [Fact]
    public void RoundTrip_persiste_columna_y_zapata()
    {
        var json = ProyectoSerializer.ToJson(ProyectoConColumna());
        var n = ProyectoSerializer.FromJson(json).Edificios[0].Niveles[0];

        Assert.Single(n.Columnas);
        var c = n.Columnas[0];
        Assert.Equal("C-1", c.Nombre);
        Assert.Equal(0.4, c.Base, 9);
        Assert.Equal(0.5, c.Peralte, 9);
        Assert.Equal(2, c.CoordenadaX, 9);
        Assert.Equal(3.2, c.Altura, 9);
        Assert.NotNull(c.Zapata);
        Assert.Equal("Z-1", c.Zapata!.Nombre);
        Assert.Equal(2.5, c.Zapata.Largo, 9);
        Assert.Equal(0.2, c.AreaSeccion, 9); // recomputada
    }

    [Fact]
    public void Las_propiedades_computadas_no_se_serializan()
    {
        var json = ProyectoSerializer.ToJson(ProyectoConColumna());
        Assert.DoesNotContain("AreaSeccion", json);
        Assert.DoesNotContain("AreaContacto", json);
    }

    [Fact]
    public void Proyecto_sin_columnas_carga_con_coleccion_vacia()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.AsegurarEstructura();
        var recargado = ProyectoSerializer.FromJson(ProyectoSerializer.ToJson(p));
        Assert.Empty(recargado.Edificios[0].Niveles[0].Columnas);
    }
}
