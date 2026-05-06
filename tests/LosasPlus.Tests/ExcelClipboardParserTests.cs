using System.Linq;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

public class ExcelClipboardParserTests
{
    [Fact]
    public void Parsea_TSV_con_header_estandar()
    {
        var text =
            "Lx\tLy\tEspesor\tCarga\n" +
            "4.000\t7.800\t0.250\t1.550\n" +
            "7.200\t7.800\t0.250\t1.470\n";
        var r = ExcelClipboardParser.Parse(text);
        Assert.Equal(2, r.Losas.Count);
        Assert.Equal(4.000, r.Losas[0].Lx);
        Assert.Equal(7.800, r.Losas[0].Ly);
        Assert.Equal(0.250, r.Losas[0].Espesor);
        Assert.Equal(1.550, r.Losas[0].Carga);
    }

    [Fact]
    public void Parsea_CSV_con_punto_y_coma_y_coma_decimal_espanola()
    {
        var text = "Lx;Ly;Espesor;Wu\n4,000;7,800;0,250;1,550\n";
        var r = ExcelClipboardParser.Parse(text);
        Assert.Single(r.Losas);
        Assert.Equal(4.0, r.Losas[0].Lx);
        Assert.Equal(1.55, r.Losas[0].Carga);
    }

    [Fact]
    public void Sin_header_asume_orden_posicional_Lx_Ly_H_Carga()
    {
        var text = "4.000\t7.800\t0.250\t1.550\n";
        var r = ExcelClipboardParser.Parse(text);
        Assert.Single(r.Losas);
        Assert.Equal(4.0, r.Losas[0].Lx);
        Assert.Equal(7.8, r.Losas[0].Ly);
        Assert.Equal(0.25, r.Losas[0].Espesor);
        Assert.Equal(1.55, r.Losas[0].Carga);
    }

    [Fact]
    public void Header_con_unidades_entre_parentesis_se_normaliza()
    {
        var text =
            "Lx (m)\tLy (m)\tEspesor (m)\tCarga (ton/m²)\n" +
            "4.0\t6.0\t0.12\t0.40\n";
        var r = ExcelClipboardParser.Parse(text);
        Assert.Single(r.Losas);
        Assert.Equal(4.0, r.Losas[0].Lx);
        Assert.Equal(6.0, r.Losas[0].Ly);
        Assert.Equal(0.12, r.Losas[0].Espesor);
    }

    [Fact]
    public void Filas_con_Lx_o_Ly_invalidos_se_ignoran_con_warning()
    {
        var text = "Lx\tLy\n4\t5\n0\t5\n3\t-1\n";
        var r = ExcelClipboardParser.Parse(text);
        Assert.Single(r.Losas);
        Assert.Equal(2, r.LineasIgnoradas);
        Assert.Equal(2, r.Warnings.Count);
    }

    [Fact]
    public void Defaults_aplican_cuando_faltan_columnas()
    {
        var text = "Lx\tLy\n4\t5\n";
        var def = new ExcelClipboardParser.Defaults
        { Tipo = 22, EspesorM = 0.20, RecM = 0.025, CargaTonM2 = 0.5, IdInicial = 100 };
        var r = ExcelClipboardParser.Parse(text, def);
        var l = r.Losas[0];
        Assert.Equal(100, l.Id);
        Assert.Equal(22, l.Tipo);
        Assert.Equal(0.20, l.Espesor);
        Assert.Equal(0.025, l.Rec);
        Assert.Equal(0.5, l.Carga);
    }

    [Fact]
    public void EspesorEnCm_convierte_a_metros()
    {
        var text = "Lx\tLy\tEspesor\tRec\n4\t5\t12\t2\n";
        var r = ExcelClipboardParser.Parse(text, new ExcelClipboardParser.Defaults { EspesorEnCm = true });
        Assert.Equal(0.12, r.Losas[0].Espesor);
        Assert.Equal(0.02, r.Losas[0].Rec);
    }

    [Fact]
    public void Reconoce_columna_TIPO_y_REC()
    {
        var text =
            "Tipo\tLx\tLy\tH\tCarga\tRec\n" +
            "60\t4.0\t7.8\t0.25\t1.55\t0.02\n";
        var r = ExcelClipboardParser.Parse(text);
        Assert.Equal(60, r.Losas[0].Tipo);
        Assert.Equal(0.02, r.Losas[0].Rec);
    }

    [Fact]
    public void Linea_vacia_intermedia_no_rompe_parser()
    {
        var text = "Lx\tLy\n4\t5\n\n3\t6\n";
        var r = ExcelClipboardParser.Parse(text);
        Assert.Equal(2, r.Losas.Count);
    }

    [Fact]
    public void IDs_duplicados_se_renumeran()
    {
        var text = "Id\tLx\tLy\n1\t4\t5\n1\t6\t7\n";
        var r = ExcelClipboardParser.Parse(text);
        Assert.Equal(2, r.Losas.Count);
        Assert.NotEqual(r.Losas[0].Id, r.Losas[1].Id);
        Assert.Contains("duplicados", r.Warnings.Single());
    }

    [Fact]
    public void Texto_vacio_devuelve_resultado_vacio()
    {
        var r = ExcelClipboardParser.Parse("");
        Assert.Empty(r.Losas);
    }
}
