using System.IO;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del soporte multi-sistema en .DL: parser, writer y roundtrip.
/// </summary>
public class MultiSistemaTests
{
    private static string FixturePath(string rel) =>
        Path.Combine(System.AppContext.BaseDirectory, "fixtures", rel);

    [Fact]
    public void ReadAll_extrae_los_dos_sistemas_del_fixture()
    {
        var sistemas = DLFileService.ReadAll(FixturePath("multi_sistema.DL"));
        Assert.Equal(2, sistemas.Count);

        Assert.Equal("Sistema No 1", sistemas[0].Nombre);
        Assert.Equal(2, sistemas[0].Losas.Count);
        Assert.Single(sistemas[0].BordesX);
        Assert.Empty(sistemas[0].BordesY);
        Assert.Equal(0, sistemas[0].Adicionales);

        Assert.Equal("Cubierta techo", sistemas[1].Nombre);
        Assert.Single(sistemas[1].Losas);
        Assert.Equal(50, sistemas[1].Losas[0].Tipo);
        Assert.Equal(1.500, sistemas[1].Losas[0].Carga);
        Assert.Equal(1, sistemas[1].Adicionales);
    }

    [Fact]
    public void Read_legacy_devuelve_solo_el_primer_sistema()
    {
        var s = DLFileService.Read(FixturePath("multi_sistema.DL"));
        Assert.Equal("Sistema No 1", s.Nombre);
        Assert.Equal(2, s.Losas.Count);
    }

    [Fact]
    public void WriteAll_serializa_dos_sistemas_con_separador()
    {
        var sistemas = DLFileService.ReadAll(FixturePath("multi_sistema.DL"));
        var written = DLFileService.WriteAll(sistemas);
        Assert.Contains("Sistema No 1", written);
        Assert.Contains("Cubierta techo", written);
        Assert.Contains("siguiente sistema", written);
    }

    [Fact]
    public void Roundtrip_multi_sistema_preserva_todo()
    {
        var original = DLFileService.ReadAll(FixturePath("multi_sistema.DL"));
        var tmp = Path.Combine(Path.GetTempPath(), $"multi_rt_{System.Guid.NewGuid():N}.DL");
        try
        {
            DLFileService.SaveAll(original, tmp);
            var reparsed = DLFileService.ReadAll(tmp);

            Assert.Equal(original.Count, reparsed.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i].Nombre, reparsed[i].Nombre);
                Assert.Equal(original[i].Losas.Count, reparsed[i].Losas.Count);
                Assert.Equal(original[i].BordesX.Count, reparsed[i].BordesX.Count);
                Assert.Equal(original[i].BordesY.Count, reparsed[i].BordesY.Count);
                Assert.Equal(original[i].Adicionales, reparsed[i].Adicionales);
            }
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    [Fact]
    public void Sistema_unico_sigue_funcionando_via_Read_legacy()
    {
        var s = DLFileService.Read(FixturePath("ADN_TORRE_PS2_S_SEMISOTANO.DL"));
        Assert.Equal("ADN TORRE PS2 S SEMISOTANO", s.Nombre);
        Assert.Equal(27, s.Losas.Count);
    }

    [Fact]
    public void Proyecto_tiene_coleccion_observable_de_sistemas()
    {
        var p = new Proyecto();
        Assert.Empty(p.Sistemas);

        var s = new Sistema { Nombre = "Test" };
        p.Sistemas.Add(s);
        Assert.Single(p.Sistemas);
        Assert.Equal("Test", p.Sistemas[0].Nombre);
    }
}
