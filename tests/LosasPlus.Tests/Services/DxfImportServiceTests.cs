using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LosasPlus.Models.Cad;
using LosasPlus.Services;
using netDxf;
using netDxf.Entities;
using netDxf.Units;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del <see cref="DxfImportService"/> — el importador de planos .DXF de
/// la Fase 1 del plan CAD.
///
/// <para>
/// Cada test que necesita un .DXF lo genera <b>programáticamente</b> con netDxf
/// en <see cref="Path.GetTempPath"/> durante el setup, y lo borra en
/// <see cref="Dispose"/>. No dependemos de fixtures binarios en disco.
/// </para>
/// </summary>
public class DxfImportServiceTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly DxfImportService _svc = new();

    public void Dispose()
    {
        foreach (var p in _tempFiles)
            try { if (File.Exists(p)) File.Delete(p); } catch { /* best-effort */ }
    }

    // -----------------------------------------------------------------
    // Helpers — generación de .DXF sintéticos con netDxf
    // -----------------------------------------------------------------

    /// <summary>Crea un .DXF temporal aplicando <paramref name="configurar"/> al documento.</summary>
    private string CrearDxf(Action<DxfDocument> configurar, DrawingUnits? unidades = null)
    {
        var doc = new DxfDocument();
        if (unidades.HasValue) doc.DrawingVariables.InsUnits = unidades.Value;
        configurar(doc);

        var path = Path.Combine(Path.GetTempPath(), $"dxftest_{Guid.NewGuid():N}.dxf");
        doc.Save(path);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>Crea un archivo temporal con contenido arbitrario (para casos de error).</summary>
    private string CrearArchivoCrudo(string nombre, byte[] contenido)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dxftest_{Guid.NewGuid():N}_{nombre}");
        File.WriteAllBytes(path, contenido);
        _tempFiles.Add(path);
        return path;
    }

    // =================================================================
    // Caso central pedido por la Fase 1.A: 2 líneas + 1 texto
    // =================================================================

    [Fact]
    public void Importar_dxf_con_2_lineas_y_1_texto_cuenta_3_entidades()
    {
        var path = CrearDxf(doc =>
        {
            doc.Entities.Add(new Line(new Vector2(0, 0), new Vector2(4, 0)));
            doc.Entities.Add(new Line(new Vector2(4, 0), new Vector2(4, 3)));
            doc.Entities.Add(new Text("SALA", new Vector2(1, 1), 0.25));
        }, DrawingUnits.Meters);

        var plano = _svc.Importar(path);

        Assert.Equal(3, plano.CantidadEntidades);
        Assert.Equal(2, plano.Contar(TipoEntidadCad.Linea));
        Assert.Equal(1, plano.Contar(TipoEntidadCad.Texto));
        Assert.Equal(0, plano.Contar(TipoEntidadCad.Arco));
    }

    // =================================================================
    // Mapeo de coordenadas
    // =================================================================

    [Fact]
    public void Importar_mapea_coordenadas_de_lineas_correctamente()
    {
        var path = CrearDxf(doc =>
        {
            doc.Entities.Add(new Line(new Vector2(1.5, 2.5), new Vector2(6.5, 8.5)));
        }, DrawingUnits.Meters);

        var plano = _svc.Importar(path);
        var linea = plano.Entidades.OfType<LineaCad>().Single();

        Assert.Equal(1.5, linea.Inicio.X, precision: 6);
        Assert.Equal(2.5, linea.Inicio.Y, precision: 6);
        Assert.Equal(6.5, linea.Fin.X, precision: 6);
        Assert.Equal(8.5, linea.Fin.Y, precision: 6);
        // Longitud = sqrt(5^2 + 6^2) = sqrt(61) ≈ 7.8102
        Assert.Equal(Math.Sqrt(61), linea.Longitud, precision: 4);
    }

    [Fact]
    public void Importar_mapea_texto_con_posicion_contenido_y_altura()
    {
        var path = CrearDxf(doc =>
        {
            doc.Entities.Add(new Text("DORMITORIO 1", new Vector2(3.0, 4.0), 0.30));
        }, DrawingUnits.Meters);

        var plano = _svc.Importar(path);
        var texto = plano.Entidades.OfType<TextoCad>().Single();

        Assert.Equal("DORMITORIO 1", texto.Contenido);
        Assert.Equal(3.0, texto.Posicion.X, precision: 6);
        Assert.Equal(4.0, texto.Posicion.Y, precision: 6);
        Assert.Equal(0.30, texto.Altura, precision: 6);
    }

    [Fact]
    public void Importar_polilinea_cerrada_de_4_vertices_se_mapea_completa()
    {
        var path = CrearDxf(doc =>
        {
            var verts = new[]
            {
                new Polyline2DVertex(0, 0),
                new Polyline2DVertex(5, 0),
                new Polyline2DVertex(5, 4),
                new Polyline2DVertex(0, 4),
            };
            doc.Entities.Add(new Polyline2D(verts, isClosed: true));
        }, DrawingUnits.Meters);

        var plano = _svc.Importar(path);
        var poli = plano.Entidades.OfType<PolilineaCad>().Single();

        Assert.True(poli.Cerrada);
        Assert.Equal(4, poli.CantidadVertices);
        Assert.Equal(5.0, poli.Vertices[1].X, precision: 6);
        Assert.Equal(4.0, poli.Vertices[2].Y, precision: 6);
    }

    [Fact]
    public void Importar_circulo_se_mapea_como_arco_completo()
    {
        var path = CrearDxf(doc =>
        {
            doc.Entities.Add(new Circle(new Vector2(2, 2), 0.5));
        }, DrawingUnits.Meters);

        var plano = _svc.Importar(path);
        var arco = plano.Entidades.OfType<ArcoCad>().Single();

        Assert.Equal(2.0, arco.Centro.X, precision: 6);
        Assert.Equal(0.5, arco.Radio, precision: 6);
        Assert.True(arco.EsCirculoCompleto);
    }

    // =================================================================
    // Normalización de unidades a metros
    // =================================================================

    [Fact]
    public void Importar_normaliza_milimetros_a_metros()
    {
        // Una línea de 5000 mm debe llegar como 5.0 m.
        var path = CrearDxf(doc =>
        {
            doc.Entities.Add(new Line(new Vector2(0, 0), new Vector2(5000, 0)));
        }, DrawingUnits.Millimeters);

        var plano = _svc.Importar(path);
        var linea = plano.Entidades.OfType<LineaCad>().Single();

        Assert.Equal(5.0, linea.Fin.X, precision: 6);
        Assert.Equal(5.0, linea.Longitud, precision: 6);
        Assert.Equal("Millimeters", plano.UnidadOriginal);
    }

    [Fact]
    public void Importar_normaliza_centimetros_a_metros()
    {
        var path = CrearDxf(doc =>
        {
            doc.Entities.Add(new Line(new Vector2(0, 0), new Vector2(350, 0)));
        }, DrawingUnits.Centimeters);

        var plano = _svc.Importar(path);
        var linea = plano.Entidades.OfType<LineaCad>().Single();
        Assert.Equal(3.5, linea.Fin.X, precision: 6);
    }

    // =================================================================
    // Bounding box
    // =================================================================

    [Fact]
    public void Importar_calcula_el_bounding_box_del_plano()
    {
        var path = CrearDxf(doc =>
        {
            doc.Entities.Add(new Line(new Vector2(2, 1), new Vector2(8, 1)));
            doc.Entities.Add(new Line(new Vector2(2, 1), new Vector2(2, 6)));
        }, DrawingUnits.Meters);

        var plano = _svc.Importar(path);

        Assert.Equal(2, plano.MinX, precision: 6);
        Assert.Equal(1, plano.MinY, precision: 6);
        Assert.Equal(8, plano.MaxX, precision: 6);
        Assert.Equal(6, plano.MaxY, precision: 6);
        Assert.Equal(6, plano.Ancho, precision: 6);
        Assert.Equal(5, plano.Alto, precision: 6);
    }

    // =================================================================
    // Resiliencia — archivos inexistentes, vacíos, corruptos
    // =================================================================

    [Fact]
    public void Importar_ruta_vacia_lanza_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _svc.Importar(""));
        Assert.Throws<ArgumentException>(() => _svc.Importar("   "));
    }

    [Fact]
    public void Importar_archivo_inexistente_lanza_FileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(
            () => _svc.Importar(@"Z:\no\existe\plano.dxf"));
    }

    [Fact]
    public void Importar_archivo_vacio_lanza_InvalidOperationException()
    {
        var path = CrearArchivoCrudo("vacio.dxf", Array.Empty<byte>());
        var ex = Assert.Throws<InvalidOperationException>(() => _svc.Importar(path));
        Assert.Contains("vacío", ex.Message);
    }

    [Fact]
    public void Importar_archivo_corrupto_lanza_InvalidOperationException()
    {
        // Contenido que NO tiene la estructura de secciones de un DXF.
        var basura = System.Text.Encoding.ASCII.GetBytes(
            "ESTO NO ES UN ARCHIVO DXF\nsolo texto basura\nsin secciones\n");
        var path = CrearArchivoCrudo("corrupto.dxf", basura);
        Assert.Throws<InvalidOperationException>(() => _svc.Importar(path));
    }

    // =================================================================
    // Plano sin geometría soportada
    // =================================================================

    [Fact]
    public void Importar_dxf_sin_entidades_devuelve_plano_vacio()
    {
        var path = CrearDxf(_ => { /* documento sin entidades */ }, DrawingUnits.Meters);

        var plano = _svc.Importar(path);

        Assert.True(plano.EstaVacio);
        Assert.Equal(0, plano.CantidadEntidades);
        Assert.Equal(0, plano.Ancho, precision: 6);
    }
}
