using System;
using System.IO;
using LosasPlus.Services;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Units;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Integración END-TO-END del pipeline DXF: escribe un DXF estructural REAL con
/// netDxf (capas LOSAS/VIGAS/COLUMNAS), lo importa con
/// <see cref="DxfImportService"/> y lo mapea con <see cref="DxfEstructuraMapper"/>.
/// Verifica que la cadena completa (parseo de geometría exacta → clasificación por
/// capa → elementos) produce las losas, vigas y columnas esperadas — sin red ni IA
/// (capas con nombres claros → la heurística las resuelve).
/// </summary>
public class DxfEstructuraIntegracionTests
{
    [Fact]
    public void Importar_y_mapear_un_DXF_real_genera_losas_vigas_columnas()
    {
        string path = Path.Combine(Path.GetTempPath(), $"estr_{Guid.NewGuid():N}.dxf");
        try
        {
            var doc = new DxfDocument();
            doc.DrawingVariables.InsUnits = DrawingUnits.Meters;

            var capaLosas = new Layer("LOSAS");
            var capaVigas = new Layer("VIGAS");
            var capaCol = new Layer("COLUMNAS");

            // 2 paños de 4 x 5 m (losas)
            doc.Entities.Add(RectCerrado(0, 0, 4, 5, capaLosas));
            doc.Entities.Add(RectCerrado(4, 0, 4, 5, capaLosas));
            // 1 viga horizontal
            doc.Entities.Add(new Line(new Vector2(0, 0), new Vector2(8, 0)) { Layer = capaVigas });
            // 2 columnas (círculos Ø0.4)
            doc.Entities.Add(new Circle(new Vector2(0, 0), 0.2) { Layer = capaCol });
            doc.Entities.Add(new Circle(new Vector2(8, 0), 0.2) { Layer = capaCol });

            doc.Save(path);

            var plano = new DxfImportService().Importar(path);
            var prop = DxfEstructuraMapper.Mapear(plano.Entidades);

            Assert.Equal(2, prop.Losas.Count);
            Assert.Single(prop.Vigas);
            Assert.Equal(2, prop.Columnas.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static Polyline2D RectCerrado(double x, double y, double w, double h, Layer capa) =>
        new(new[]
        {
            new Polyline2DVertex(new Vector2(x, y)),
            new Polyline2DVertex(new Vector2(x + w, y)),
            new Polyline2DVertex(new Vector2(x + w, y + h)),
            new Polyline2DVertex(new Vector2(x, y + h)),
        })
        { IsClosed = true, Layer = capa };
}
