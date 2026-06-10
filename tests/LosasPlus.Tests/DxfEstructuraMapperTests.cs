using System.Collections.Generic;
using LosasPlus.Models.Cad;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Mapeador determinista DXF → losas/vigas/columnas. Verifica las reglas por
/// geometría + capa: rectángulo cerrado → losa; círculo/rect en capa Columna →
/// columna; línea/polilínea en capa Viga → vigas; y que lo no-estructural se
/// ignore. Todo con entidades construidas en memoria (sin red ni archivos).
/// </summary>
public class DxfEstructuraMapperTests
{
    private static PuntoCad P(double x, double y) => new(x, y);

    private static PolilineaCad RectCerrado(string capa, double x, double y, double w, double h) =>
        new()
        {
            Capa = capa,
            Cerrada = true,
            Vertices = new List<PuntoCad> { P(x, y), P(x + w, y), P(x + w, y + h), P(x, y + h) },
        };

    private static ArcoCad Circulo(string capa, double cx, double cy, double r) =>
        new() { Capa = capa, Centro = P(cx, cy), Radio = r, AnguloInicioGrados = 0, AnguloFinGrados = 360 };

    private static LineaCad Linea(string capa, double x1, double y1, double x2, double y2) =>
        new() { Capa = capa, Inicio = P(x1, y1), Fin = P(x2, y2) };

    [Fact]
    public void Rectangulo_en_capa_LOSA_genera_una_losa()
    {
        var prop = DxfEstructuraMapper.Mapear(new EntidadCad[] { RectCerrado("LOSAS", 0, 0, 4, 5) });

        var l = Assert.Single(prop.Losas);
        Assert.Equal(0.0, l.XMetros, 3);
        Assert.Equal(0.0, l.YMetros, 3);
        Assert.Equal(4.0, l.LxM, 3);
        Assert.Equal(5.0, l.LyM, 3);
        Assert.Empty(prop.Columnas);
        Assert.Empty(prop.Vigas);
    }

    [Fact]
    public void Circulo_en_capa_COL_genera_columna_con_seccion_diametro()
    {
        var prop = DxfEstructuraMapper.Mapear(new EntidadCad[] { Circulo("COL", 3.0, 3.0, 0.25) });

        var c = Assert.Single(prop.Columnas);
        Assert.Equal(3.0, c.XMetros, 3);
        Assert.Equal(3.0, c.YMetros, 3);
        Assert.Equal(0.5, c.BaseM, 3);
        Assert.Equal(0.5, c.PeralteM, 3);
    }

    [Fact]
    public void Rectangulo_en_capa_COL_genera_columna_en_su_centro()
    {
        var prop = DxfEstructuraMapper.Mapear(new EntidadCad[] { RectCerrado("COLUMNAS", 10.0, 10.0, 0.4, 0.6) });

        var c = Assert.Single(prop.Columnas);
        Assert.Equal(10.2, c.XMetros, 3);
        Assert.Equal(10.3, c.YMetros, 3);
        Assert.Equal(0.4, c.BaseM, 3);
        Assert.Equal(0.6, c.PeralteM, 3);
        Assert.Empty(prop.Losas);   // un rect en capa Columna NO es losa
    }

    [Fact]
    public void Linea_en_capa_VIGA_genera_una_viga()
    {
        var prop = DxfEstructuraMapper.Mapear(new EntidadCad[] { Linea("VIGAS", 0, 0, 6, 0) });

        var v = Assert.Single(prop.Vigas);
        Assert.Equal(0.0, v.X1Metros, 3);
        Assert.Equal(6.0, v.X2Metros, 3);
    }

    [Fact]
    public void Polilinea_abierta_en_capa_VIGA_genera_un_segmento_por_tramo()
    {
        var poli = new PolilineaCad
        {
            Capa = "VIGAS",
            Cerrada = false,
            Vertices = new List<PuntoCad> { P(0, 0), P(4, 0), P(8, 0) },
        };

        var prop = DxfEstructuraMapper.Mapear(new EntidadCad[] { poli });

        Assert.Equal(2, prop.Vigas.Count);   // 3 vértices → 2 segmentos
    }

    [Fact]
    public void Linea_y_circulo_fuera_de_capas_estructurales_se_ignoran()
    {
        var prop = DxfEstructuraMapper.Mapear(new EntidadCad[]
        {
            Linea("MUROS", 0, 0, 3, 0),      // no es capa Viga → ignorada
            Circulo("DETALLES", 1, 1, 0.2),  // círculo fuera de capa Columna → ignorado
        });

        Assert.Empty(prop.Vigas);
        Assert.Empty(prop.Columnas);
        Assert.Empty(prop.Losas);
    }

    [Fact]
    public void Contorno_cerrado_no_rectangular_en_LOSA_genera_advertencia()
    {
        var triangulo = new PolilineaCad
        {
            Capa = "LOSAS",
            Cerrada = true,
            Vertices = new List<PuntoCad> { P(0, 0), P(4, 0), P(2, 3) },
        };

        var prop = DxfEstructuraMapper.Mapear(new EntidadCad[] { triangulo });

        Assert.Empty(prop.Losas);
        Assert.NotNull(prop.Advertencias);
    }

    [Fact]
    public void Rectangulo_en_capa_VIGA_no_se_pierde_en_silencio()
    {
        // F2 C2: podría ser una viga con ancho o un anillo de 4 vigas — no se
        // interpreta a ciegas, pero TAMPOCO se descarta sin aviso.
        var prop = DxfEstructuraMapper.Mapear(new EntidadCad[] { RectCerrado("VIGAS", 0, 0, 6, 0.3) });

        Assert.Empty(prop.Losas);
        Assert.Empty(prop.Vigas);
        Assert.Empty(prop.Columnas);
        Assert.NotNull(prop.Advertencias);
        Assert.Contains("Viga", prop.Advertencias);
        Assert.Contains("rect", prop.Advertencias!.ToLowerInvariant());
    }

    [Fact]
    public void Escena_mixta_cuenta_cada_tipo()
    {
        var prop = DxfEstructuraMapper.Mapear(new EntidadCad[]
        {
            RectCerrado("LOSAS", 0, 0, 4, 5),     // losa
            RectCerrado("LOSAS", 4, 0, 4, 5),     // losa
            Circulo("COL", 0, 0, 0.2),            // columna
            Circulo("COL", 4, 0, 0.2),            // columna
            Linea("VIGAS", 0, 0, 8, 0),           // viga
            Linea("MUROS", 0, 0, 8, 5),           // ignorada
        });

        Assert.Equal(2, prop.Losas.Count);
        Assert.Equal(2, prop.Columnas.Count);
        Assert.Single(prop.Vigas);
    }
}
