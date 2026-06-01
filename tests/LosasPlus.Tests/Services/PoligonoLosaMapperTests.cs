using System.Collections.Generic;
using LosasPlus.Models.Cad;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de <see cref="PoligonoLosaMapper"/> — la lógica geométrica de la
/// Fase 2 del plan CAD: detectar rectángulos ortogonales y el test
/// punto-en-polígono usado por el hit-test del lienzo.
/// </summary>
public class PoligonoLosaMapperTests
{
    private static PolilineaCad Poli(bool cerrada, params (double x, double y)[] pts)
    {
        var verts = new List<PuntoCad>();
        foreach (var (x, y) in pts) verts.Add(new PuntoCad(x, y));
        return new PolilineaCad { Vertices = verts, Cerrada = cerrada };
    }

    // =================================================================
    // TryMapearRectangulo
    // =================================================================

    [Fact]
    public void Rectangulo_ortogonal_de_4_vertices_se_mapea()
    {
        // Rectángulo 6×4 con esquina en (2, 3).
        var poli = Poli(true, (2, 3), (8, 3), (8, 7), (2, 7));
        Assert.True(PoligonoLosaMapper.TryMapearRectangulo(poli, out var rect));
        Assert.Equal(2, rect.MinX, precision: 6);
        Assert.Equal(3, rect.MinY, precision: 6);
        Assert.Equal(8, rect.MaxX, precision: 6);
        Assert.Equal(7, rect.MaxY, precision: 6);
        Assert.Equal(6, rect.Ancho, precision: 6);
        Assert.Equal(4, rect.Alto, precision: 6);
    }

    [Fact]
    public void Rectangulo_con_5to_vertice_repetido_se_acepta()
    {
        // Cierre explícito: el 5º vértice coincide con el 1º.
        var poli = Poli(true, (0, 0), (5, 0), (5, 3), (0, 3), (0, 0));
        Assert.True(PoligonoLosaMapper.TryMapearRectangulo(poli, out var rect));
        Assert.Equal(5, rect.Ancho, precision: 6);
        Assert.Equal(3, rect.Alto, precision: 6);
    }

    [Fact]
    public void Poligono_no_ortogonal_se_rechaza()
    {
        // Cuadrilátero con un lado inclinado — no es rectángulo ortogonal.
        var poli = Poli(true, (0, 0), (6, 1), (6, 5), (0, 4));
        Assert.False(PoligonoLosaMapper.TryMapearRectangulo(poli, out _));
    }

    [Fact]
    public void Triangulo_se_rechaza()
    {
        var poli = Poli(true, (0, 0), (6, 0), (3, 5));
        Assert.False(PoligonoLosaMapper.TryMapearRectangulo(poli, out _));
    }

    [Fact]
    public void Polilinea_abierta_se_rechaza()
    {
        // Aunque los vértices formen un rectángulo, si no está cerrada se rechaza.
        var poli = Poli(false, (0, 0), (5, 0), (5, 3), (0, 3));
        Assert.False(PoligonoLosaMapper.TryMapearRectangulo(poli, out _));
    }

    [Fact]
    public void Rectangulo_degenerado_de_area_cero_se_rechaza()
    {
        // Cuatro vértices colineales — ancho o alto nulo.
        var poli = Poli(true, (0, 0), (5, 0), (5, 0), (0, 0));
        Assert.False(PoligonoLosaMapper.TryMapearRectangulo(poli, out _));
    }

    // =================================================================
    // ContienePunto (ray casting)
    // =================================================================

    [Fact]
    public void ContienePunto_detecta_punto_interior()
    {
        var poli = Poli(true, (0, 0), (10, 0), (10, 8), (0, 8));
        Assert.True(PoligonoLosaMapper.ContienePunto(poli, new PuntoCad(5, 4)));
    }

    [Fact]
    public void ContienePunto_rechaza_punto_exterior()
    {
        var poli = Poli(true, (0, 0), (10, 0), (10, 8), (0, 8));
        Assert.False(PoligonoLosaMapper.ContienePunto(poli, new PuntoCad(15, 4)));
        Assert.False(PoligonoLosaMapper.ContienePunto(poli, new PuntoCad(5, -2)));
    }

    [Fact]
    public void ContienePunto_funciona_con_un_poligono_en_L()
    {
        // Polígono en forma de L: el punto en la "muesca" debe quedar afuera.
        var poliL = Poli(true,
            (0, 0), (6, 0), (6, 3), (3, 3), (3, 6), (0, 6));
        Assert.True(PoligonoLosaMapper.ContienePunto(poliL, new PuntoCad(1, 1)));   // dentro
        Assert.False(PoligonoLosaMapper.ContienePunto(poliL, new PuntoCad(5, 5))); // en la muesca, fuera
    }
}
