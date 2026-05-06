using System.IO;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del lector / escritor del .DL: parser robusto contra formatos reales y
/// writer que produce un .DL byte-compatible con el motor original.
/// </summary>
public class DLFileServiceTests
{
    private static string FixturePath(string rel) =>
        Path.Combine(System.AppContext.BaseDirectory, "fixtures", rel);

    // ---------- Lectura ----------

    [Fact]
    public void Lee_DL_con_nombre_de_sistema_que_contiene_espacios()
    {
        var s = DLFileService.Read(FixturePath("sample_simple.DL"));
        Assert.Equal("ADN TORRE PS2 S SEMISOTANO", s.Nombre);
        Assert.Equal(0.210, s.Fc);
        Assert.Equal(4.200, s.Fy);
        Assert.Equal(0, s.Adicionales);
        Assert.Equal(3, s.Losas.Count);
    }

    [Fact]
    public void Lee_losas_con_valores_y_dimensiones_correctas()
    {
        var s = DLFileService.Read(FixturePath("sample_simple.DL"));
        var l1 = s.Losas[0];
        Assert.Equal(1, l1.Id);
        Assert.Equal(60, l1.Tipo);
        Assert.Equal(1.550, l1.Carga);
        Assert.Equal(0.250, l1.Espesor);
        Assert.Equal(4.000, l1.Lx);
        Assert.Equal(7.800, l1.Ly);
        Assert.Equal(0.020, l1.Rec);
    }

    [Fact]
    public void Lee_bordes_X_y_Y_con_balanceo()
    {
        var s = DLFileService.Read(FixturePath("sample_simple.DL"));
        Assert.Single(s.BordesX);
        Assert.Single(s.BordesY);
        Assert.Equal(1, s.BordesX[0].BI);
        Assert.Equal(2, s.BordesX[0].BJ);
        Assert.Equal("S", s.BordesX[0].Balanceo);
        Assert.Equal(1, s.BordesY[0].BI);
        Assert.Equal(3, s.BordesY[0].BJ);
    }

    // ---------- Roundtrip ----------

    [Fact]
    public void Roundtrip_read_write_read_preserva_todos_los_valores()
    {
        var path = FixturePath("sample_simple.DL");
        var s1 = DLFileService.Read(path);

        var tmp = Path.Combine(Path.GetTempPath(), $"losasplus_rt_{System.Guid.NewGuid():N}.DL");
        try
        {
            DLFileService.Save(s1, tmp);
            var s2 = DLFileService.Read(tmp);

            Assert.Equal(s1.Nombre, s2.Nombre);
            Assert.Equal(s1.Fc, s2.Fc);
            Assert.Equal(s1.Fy, s2.Fy);
            Assert.Equal(s1.Adicionales, s2.Adicionales);
            Assert.Equal(s1.Losas.Count, s2.Losas.Count);
            for (int i = 0; i < s1.Losas.Count; i++)
            {
                var a = s1.Losas[i]; var b = s2.Losas[i];
                Assert.Equal(a.Id, b.Id);
                Assert.Equal(a.Tipo, b.Tipo);
                Assert.Equal(a.Carga, b.Carga);
                Assert.Equal(a.Espesor, b.Espesor);
                Assert.Equal(a.Lx, b.Lx);
                Assert.Equal(a.Ly, b.Ly);
                Assert.Equal(a.Rec, b.Rec);
            }
            Assert.Equal(s1.BordesX.Count, s2.BordesX.Count);
            Assert.Equal(s1.BordesY.Count, s2.BordesY.Count);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    // ---------- Formato byte-compatible con el motor ----------

    [Fact]
    public void Linea_NLOSA_FC_FY_ADIC_tiene_formato_exacto_del_modelo()
    {
        var s = DLFileService.Read(FixturePath("sample_simple.DL"));
        var written = DLFileService.Write(s);
        // Para NLOSA=3 con anchos {0,7}{1,12:0.000}{2,11:0.000}{3,9}
        Assert.Contains("      3       0.210      4.200        0", written);
    }

    [Fact]
    public void Linea_de_losa_tiene_formato_exacto_del_modelo()
    {
        var s = DLFileService.Read(FixturePath("sample_simple.DL"));
        var written = DLFileService.Write(s);
        // Modelo del .hlp: "      1     60     1.550    0.250     4.000    7.800    0.020"
        Assert.Contains("      1     60     1.550    0.250     4.000    7.800    0.020", written);
    }

    [Fact]
    public void Linea_de_borde_tiene_formato_exacto_del_modelo()
    {
        var s = DLFileService.Read(FixturePath("sample_simple.DL"));
        var written = DLFileService.Write(s);
        Assert.Contains("         1         2         S", written);
    }

    [Fact]
    public void Output_usa_CRLF_no_LF_solo()
    {
        var s = DLFileService.Read(FixturePath("sample_simple.DL"));
        var written = DLFileService.Write(s);
        Assert.Contains("\r\n", written);
        // No debe haber líneas con LF sin precedente CR
        for (int i = 0; i < written.Length; i++)
            if (written[i] == '\n')
                Assert.True(i > 0 && written[i - 1] == '\r', $"LF sin CR en posición {i}");
    }

    [Fact]
    public void Output_NO_contiene_em_dash_ni_caracteres_no_ASCII_problematicos()
    {
        // Algunas versiones del motor original (Smalltalk) pueden tener problemas con
        // ciertos caracteres no-ASCII en líneas de comentario que ya no van.
        var s = DLFileService.Read(FixturePath("sample_simple.DL"));
        var written = DLFileService.Write(s);
        Assert.DoesNotContain("—", written);  // em-dash U+2014
    }

    [Fact]
    public void Output_se_guarda_en_cp1252_sin_BOM()
    {
        var s = DLFileService.Read(FixturePath("sample_simple.DL"));
        var tmp = Path.Combine(Path.GetTempPath(), $"losasplus_enc_{System.Guid.NewGuid():N}.DL");
        try
        {
            DLFileService.Save(s, tmp);
            var bytes = File.ReadAllBytes(tmp);
            // cp1252 no tiene BOM. Verificar que los primeros 3 bytes no sean EF BB BF (UTF-8 BOM).
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "El archivo NO debe llevar BOM (cp1252).");
            // El primer byte debe ser '$' (0x24)
            Assert.Equal((byte)'$', bytes[0]);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    // ---------- Edge cases ----------

    [Fact]
    public void Lee_DL_real_ADN_TORRE_con_27_losas_y_22_bordes()
    {
        // El .DL "ADN TORRE PS2 S SEMISOTANO.DL" es del usuario y abre correctamente
        // en Losas.exe; este test garantiza que LosasPlus también lo lee bien.
        var s = DLFileService.Read(FixturePath("ADN_TORRE_PS2_S_SEMISOTANO.DL"));
        Assert.Equal("ADN TORRE PS2 S SEMISOTANO", s.Nombre);
        Assert.Equal(0.210, s.Fc);
        Assert.Equal(4.200, s.Fy);
        Assert.Equal(0, s.Adicionales);
        Assert.Equal(27, s.Losas.Count);
        Assert.Equal(13, s.BordesX.Count);
        Assert.Equal(9, s.BordesY.Count);

        // Verificación puntual: losa 7 (la más cargada)
        var l7 = s.Losas.First(l => l.Id == 7);
        Assert.Equal(60, l7.Tipo);
        Assert.Equal(1.350, l7.Carga);
        Assert.Equal(0.250, l7.Espesor);
        Assert.Equal(11.350, l7.Lx);
        Assert.Equal(8.750, l7.Ly);

        // Verificación puntual: borde 5-7 según Y, balanceo S
        var b57 = s.BordesY.First(b => b.BI == 5 && b.BJ == 7);
        Assert.Equal("S", b57.Balanceo);
    }

    [Fact]
    public void Roundtrip_DL_real_ADN_TORRE_preserva_todo()
    {
        var s1 = DLFileService.Read(FixturePath("ADN_TORRE_PS2_S_SEMISOTANO.DL"));
        var tmp = Path.Combine(Path.GetTempPath(), $"adn_rt_{System.Guid.NewGuid():N}.DL");
        try
        {
            DLFileService.Save(s1, tmp);
            var s2 = DLFileService.Read(tmp);

            Assert.Equal(s1.Nombre, s2.Nombre);
            Assert.Equal(s1.Losas.Count, s2.Losas.Count);
            Assert.Equal(s1.BordesX.Count, s2.BordesX.Count);
            Assert.Equal(s1.BordesY.Count, s2.BordesY.Count);
            for (int i = 0; i < s1.Losas.Count; i++)
            {
                Assert.Equal(s1.Losas[i].Id, s2.Losas[i].Id);
                Assert.Equal(s1.Losas[i].Tipo, s2.Losas[i].Tipo);
                Assert.Equal(s1.Losas[i].Lx, s2.Losas[i].Lx);
                Assert.Equal(s1.Losas[i].Ly, s2.Losas[i].Ly);
            }
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    [Fact]
    public void Acepta_nombres_simples_tipo_Sistema_No_1()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"losasplus_simple_{System.Guid.NewGuid():N}.DL");
        File.WriteAllText(tmp,
            "$  ARCHIVO DE DATOS DEL PROGRAMA LOSA\r\n" +
            "$\r\n" +
            "Sistema No 1\r\n" +
            "$\r\n" +
            "    1  0.210  4.200  0\r\n" +
            "    1  10  2.0  0.12  4.0  4.0  0.02\r\n" +
            "$\r\n" +
            "    0\r\n" +
            "    0\r\n",
            System.Text.Encoding.GetEncoding(1252));
        try
        {
            var s = DLFileService.Read(tmp);
            Assert.Equal("Sistema No 1", s.Nombre);
            Assert.Single(s.Losas);
            Assert.Empty(s.BordesX);
            Assert.Empty(s.BordesY);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    [Fact]
    public void Mensaje_de_error_es_descriptivo_si_falta_un_campo()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"losasplus_bad_{System.Guid.NewGuid():N}.DL");
        File.WriteAllText(tmp,
            "Sistema malo\r\n" +
            "    2  0.21  4.2\r\n",  // falta ADIC
            System.Text.Encoding.GetEncoding(1252));
        try
        {
            var ex = Assert.Throws<System.FormatException>(() => DLFileService.Read(tmp));
            Assert.Contains("ADICIONALES", ex.Message);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
