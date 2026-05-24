using System.Linq;
using LosasPlus.Grillas;
using LosasPlus.Models;
using LosasPlus.Persistence;
using Xunit;

namespace LosasPlus.Tests.Persistence;

/// <summary>
/// Tests del salvavidas v3→v4 de <see cref="ProyectoSerializer"/>:
/// las grillas estructurales son aditivas y los archivos anteriores
/// se cargan con la grilla default 3×3 inyectada automáticamente
/// (Módulo 2 Parte A Fase 3D-II).
/// </summary>
public class ProyectoSerializerGrillasTests
{
    [Fact]
    public void FormatVersion_TrasModulo2A_EsCuatro()
    {
        // Centinela: si alguien revierte el bump por error, esto rompe.
        Assert.Equal(4, ProyectoSerializer.FormatVersion);
    }

    [Fact]
    public void RoundtripV4_PreservaGrillaPersonalizada()
    {
        // Arrange: proyecto con grilla personalizada (no default).
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        var edif = new Edificio { Nombre = "Edificio Custom" };
        edif.Niveles.Add(new Nivel { Nombre = "PB", Cota = 0.0 });
        edif.Grillas = new GrillaEstructural(
            new System.Collections.Generic.List<EjeNombrado>
            {
                new("X1", 0.0),
                new("X2", 8.5),
            },
            new System.Collections.Generic.List<EjeNombrado>
            {
                new("Y1", 0.0),
                new("Y2", 3.5),
                new("Y3", 7.0),
            });
        proyecto.Edificios.Add(edif);

        // Act: serializar → deserializar.
        string json = ProyectoSerializer.ToJson(proyecto);
        var roundtrip = ProyectoSerializer.FromJson(json);

        // Assert: la grilla custom se preserva 1:1 — el salvavidas no se
        // activa porque la grilla NO está vacía.
        var edifRT = roundtrip.Edificios.Last();
        Assert.Equal(2, edifRT.Grillas.EjesX.Count);
        Assert.Equal(3, edifRT.Grillas.EjesY.Count);
        Assert.Equal("X1", edifRT.Grillas.EjesX[0].Nombre);
        Assert.Equal( 0.0, edifRT.Grillas.EjesX[0].PosicionMetros);
        Assert.Equal("X2", edifRT.Grillas.EjesX[1].Nombre);
        Assert.Equal( 8.5, edifRT.Grillas.EjesX[1].PosicionMetros);
        Assert.Equal("Y3", edifRT.Grillas.EjesY[2].Nombre);
        Assert.Equal( 7.0, edifRT.Grillas.EjesY[2].PosicionMetros);
    }

    [Fact]
    public void ProyectoSerializer_CargaJsonV3_InyectaGrillaPorDefecto()
    {
        // Arrange: JSON simulado de un archivo .lpx.json v3 antiguo —
        // edificios + niveles + sistemas pero SIN el campo `grillas`.
        const string jsonV3 = """
        {
          "version": 3,
          "creadoUtc": "2024-01-15T10:00:00Z",
          "modificadoUtc": "2024-01-15T10:00:00Z",
          "appVersion": "LosasPlus 0.5.0",
          "proyecto": {
            "nombre": "Proyecto Legacy v3",
            "edificios": [
              {
                "nombre": "Edif 1",
                "niveles": [
                  { "nombre": "PB", "cota": 0, "sistemas": [] }
                ]
              }
            ]
          }
        }
        """;

        // Act
        var p = ProyectoSerializer.FromJson(jsonV3);

        // Assert: salvavidas inyectó la grilla default 3×3 a 6m.
        Assert.Single(p.Edificios);
        var grilla = p.Edificios[0].Grillas;
        Assert.False(grilla.EstaVacia);
        Assert.Equal(3, grilla.EjesX.Count);
        Assert.Equal(3, grilla.EjesY.Count);
        Assert.Equal("A", grilla.EjesX[0].Nombre);
        Assert.Equal( 0.0, grilla.EjesX[0].PosicionMetros);
        Assert.Equal("C", grilla.EjesX[2].Nombre);
        Assert.Equal(12.0, grilla.EjesX[2].PosicionMetros);
        Assert.Equal("1", grilla.EjesY[0].Nombre);
        Assert.Equal("3", grilla.EjesY[2].Nombre);
    }
}
