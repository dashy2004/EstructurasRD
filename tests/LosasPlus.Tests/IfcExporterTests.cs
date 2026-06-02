using System;
using System.IO;
using System.Text.RegularExpressions;
using LosasPlus.Interop;
using LosasPlus.Models;
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
}
