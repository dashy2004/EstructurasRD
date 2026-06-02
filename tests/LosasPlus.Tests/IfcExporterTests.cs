using System;
using System.IO;
using System.Text.RegularExpressions;
using LosasPlus.Interop;
using LosasPlus.Models;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del exportador IFC 4.3 (Fase K.2): genera el .ifc a un path temporal y
/// lo reabre para verificar la estructura STEP y el esqueleto espacial.
/// </summary>
public class IfcExporterTests
{
    private static int Count(string s, string sub) => Regex.Matches(s, Regex.Escape(sub)).Count;

    private static (string ifc, string path) Exportar()
    {
        var ed = new Edificio { Nombre = "Torre Test" };
        ed.Niveles.Add(new Nivel { Nombre = "Planta Baja", Cota = 0 });
        ed.Niveles.Add(new Nivel { Nombre = "Nivel 1", Cota = 3 });
        var path = Path.Combine(Path.GetTempPath(), $"ifc_{Guid.NewGuid():N}.ifc");
        IfcExporter.Export(ed, path, "Torre Test");
        return (File.ReadAllText(path), path);
    }

    [Fact]
    public void Genera_step_ifc4x3_con_esqueleto_espacial()
    {
        var (ifc, path) = Exportar();
        try
        {
            Assert.StartsWith("ISO-10303-21;", ifc);
            Assert.EndsWith("END-ISO-10303-21;", ifc.TrimEnd());
            Assert.Contains("FILE_SCHEMA(('IFC4X3'))", ifc);
            Assert.Contains("IFCPROJECT('", ifc);
            Assert.Contains("IFCSITE('", ifc);
            Assert.Contains("IFCBUILDING('", ifc);
            Assert.Equal(2, Count(ifc, "IFCBUILDINGSTOREY('"));         // un piso por nivel
            Assert.Equal(3, Count(ifc, "IFCRELAGGREGATES('"));          // project→site→building→storeys
            Assert.Contains("'Torre Test'", ifc);
            Assert.Contains(".ELEMENT.,3.0", ifc);                      // cota del nivel 1
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void El_globalid_tiene_22_caracteres()
    {
        var (ifc, path) = Exportar();
        try
        {
            var m = Regex.Match(ifc, @"IFCPROJECT\('([^']*)'");
            Assert.True(m.Success);
            Assert.Equal(22, m.Groups[1].Value.Length);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Exporta_los_elementos_estructurales_contenidos_en_su_piso()
    {
        var ed = new Edificio { Nombre = "Torre" };
        var nivel = new Nivel { Nombre = "Nivel 1", Cota = 0 };
        var sis = new Sistema();
        sis.Losas.Add(new Losa { Id = 1, Lx = 4, Ly = 4 });
        nivel.Sistemas.Add(sis);
        var viga = new Viga { Nombre = "V-1" };
        viga.Tramos.Add(new TramoViga { Longitud = 4 });
        nivel.Vigas.Add(viga);
        nivel.Columnas.Add(new Columna { Nombre = "C-1", Zapata = new Zapata { Nombre = "Z-1" } });
        ed.Niveles.Add(nivel);

        var path = Path.Combine(Path.GetTempPath(), $"ifc_{Guid.NewGuid():N}.ifc");
        try
        {
            IfcExporter.Export(ed, path, "Torre");
            var ifc = File.ReadAllText(path);

            Assert.Equal(1, Count(ifc, "IFCCOLUMN('"));
            Assert.Equal(1, Count(ifc, "IFCFOOTING('"));
            Assert.Equal(1, Count(ifc, "IFCBEAM('"));
            Assert.Equal(1, Count(ifc, "IFCSLAB('"));
            Assert.Equal(1, Count(ifc, "IFCRELCONTAINEDINSPATIALSTRUCTURE('"));
            Assert.Contains("'C-1'", ifc);
            Assert.Contains("'Z-1'", ifc);
            Assert.Contains("'V-1'", ifc);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Georreferencia_el_sitio_con_latitud_y_longitud()
    {
        // Santo Domingo ≈ 18.486, -69.931 → IfcCompoundPlaneAngleMeasure.
        var ed = new Edificio { Nombre = "Torre", Latitud = 18.486, Longitud = -69.931 };
        ed.Niveles.Add(new Nivel { Nombre = "N1", Cota = 0 });
        var path = Path.Combine(Path.GetTempPath(), $"ifc_{Guid.NewGuid():N}.ifc");
        try
        {
            IfcExporter.Export(ed, path, "Torre");
            var ifc = File.ReadAllText(path);
            Assert.Contains("(18,29,", ifc);       // RefLatitude
            Assert.Contains("(-69,-55,", ifc);     // RefLongitude (signo en todos los campos)
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Sin_coordenadas_el_sitio_no_lleva_lat_lon()
    {
        var (ifc, path) = Exportar(); // edificio sin georreferenciar (lat/lon 0)
        try { Assert.Contains(".ELEMENT.,$,$,$,$,$)", ifc); }  // IfcSite con RefLat/RefLon = $
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// K.6: una columna se exporta con geometría real — un IfcExtrudedAreaSolid
    /// (perfil rectangular Base×Peralte extruido por la Altura) enlazado vía
    /// IfcShapeRepresentation → IfcProductDefinitionShape como su Representation.
    /// </summary>
    [Fact]
    public void La_columna_exporta_geometria_IfcExtrudedAreaSolid()
    {
        var ed = new Edificio { Nombre = "Torre" };
        var nivel = new Nivel { Nombre = "N1", Cota = 0 };
        nivel.Columnas.Add(new Columna
        {
            Nombre = "C-1", CoordenadaX = 2, CoordenadaY = 3,
            Base = 0.40, Peralte = 0.30, Altura = 3.0,
        });
        ed.Niveles.Add(nivel);

        var path = Path.Combine(Path.GetTempPath(), $"ifc_{Guid.NewGuid():N}.ifc");
        try
        {
            IfcExporter.Export(ed, path, "Torre");
            var ifc = File.ReadAllText(path);

            Assert.Contains("IFCEXTRUDEDAREASOLID(", ifc);
            Assert.Contains("IFCRECTANGLEPROFILEDEF(.AREA.,$,", ifc);
            Assert.Contains(",0.4,0.3)", ifc);                 // perfil Base×Peralte
            Assert.Contains("'SweptSolid'", ifc);
            Assert.Contains("IFCSHAPEREPRESENTATION(", ifc);
            Assert.Contains("IFCPRODUCTDEFINITIONSHAPE(", ifc);
            // El IfcColumn referencia su representación geométrica (ya no es $ en ese slot).
            Assert.Matches(@"IFCCOLUMN\('[^']*',\$,'C-1',\$,\$,\$,#\d+,\$,\$\)", ifc);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// K.6: una losa se exporta con geometría real — un IfcExtrudedAreaSolid
    /// (perfil rectangular Lx×Ly extruido hacia −Z por su espesor) enlazado como
    /// Representation del IfcSlab.
    /// </summary>
    [Fact]
    public void La_losa_exporta_geometria_IfcExtrudedAreaSolid()
    {
        var ed = new Edificio { Nombre = "Torre" };
        var nivel = new Nivel { Nombre = "N1", Cota = 0 };
        var sis = new Sistema();
        sis.Losas.Add(new Losa { Id = 1, Lx = 4, Ly = 3, CoordenadaX = 1, CoordenadaY = 2, Espesor = 0.20 });
        nivel.Sistemas.Add(sis);
        ed.Niveles.Add(nivel);

        var path = Path.Combine(Path.GetTempPath(), $"ifc_{Guid.NewGuid():N}.ifc");
        try
        {
            IfcExporter.Export(ed, path, "Torre");
            var ifc = File.ReadAllText(path);

            Assert.Contains("IFCEXTRUDEDAREASOLID(", ifc);
            Assert.Contains(",4.0,3.0)", ifc);                 // perfil Lx×Ly
            Assert.Contains("IFCDIRECTION((0.,0.,-1.0))", ifc); // extrusión hacia abajo
            // El IfcSlab referencia su representación geométrica (ya no es $ en ese slot).
            Assert.Matches(@"IFCSLAB\('[^']*',\$,'Losa 1',\$,\$,\$,#\d+,\$,\.FLOOR\.\)", ifc);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// K.6: una zapata se exporta con geometría real — un IfcExtrudedAreaSolid
    /// (perfil Ancho×Largo centrado en la columna, extruido hacia −Z por su
    /// Peralte) enlazado como Representation del IfcFooting.
    /// </summary>
    [Fact]
    public void La_zapata_exporta_geometria_IfcExtrudedAreaSolid()
    {
        var ed = new Edificio { Nombre = "Torre" };
        var nivel = new Nivel { Nombre = "N1", Cota = 0 };
        nivel.Columnas.Add(new Columna
        {
            Nombre = "C-1", CoordenadaX = 2, CoordenadaY = 3,
            Zapata = new Zapata { Nombre = "Z-1", Ancho = 1.5, Largo = 2.0, Peralte = 0.40 },
        });
        ed.Niveles.Add(nivel);

        var path = Path.Combine(Path.GetTempPath(), $"ifc_{Guid.NewGuid():N}.ifc");
        try
        {
            IfcExporter.Export(ed, path, "Torre");
            var ifc = File.ReadAllText(path);

            Assert.Contains("IFCEXTRUDEDAREASOLID(", ifc);
            Assert.Contains(",1.5,2.0)", ifc);                 // perfil Ancho×Largo
            // El IfcFooting referencia su representación geométrica (ya no es $ en ese slot).
            Assert.Matches(@"IFCFOOTING\('[^']*',\$,'Z-1',\$,\$,\$,#\d+,\$,\$\)", ifc);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
