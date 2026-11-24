using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using LosasPlus.Calculo.PieperMartens;
using LosasPlus.Generation;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// El exportador lleva el proyecto de LosasPlus al contrato <c>data.json</c> del
/// motor <c>memoria-diseno</c> y arma un <c>memoria.json</c> inicial con los
/// datos de portada, para no volver a escribirlos.
/// </summary>
public class MemoriaDisenoExporterTests
{
    private static Proyecto ProyectoConRestaurante2(bool calcular = true)
    {
        var sistema = DLFileService.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "restaurante_2.DL"));
        if (calcular) SistemaPieperMartensCalculator.Crear().CalcularYAplicar(sistema);
        var nivel = new Nivel { Nombre = "E1" };
        nivel.Sistemas.Add(sistema);
        var edificio = new Edificio();
        edificio.Niveles.Add(nivel);
        var p = new Proyecto { Nombre = "Edificio de prueba" };
        p.Edificios.Clear();
        p.Edificios.Add(edificio);
        return p;
    }

    private static JsonElement Datos(Proyecto p) => JsonDocument.Parse(MemoriaDisenoExporter.ExportarDatos(p)).RootElement;

    [Fact]
    public void Los_datos_declaran_que_no_vienen_de_un_pdf_y_no_traen_capitulos_ajenos_a_losas()
    {
        var d = Datos(ProyectoConRestaurante2());

        Assert.False(d.GetProperty("referencia_pdf").GetBoolean());
        Assert.Equal(0, d.GetProperty("sobrecarga").GetArrayLength());
        Assert.Equal(0, d.GetProperty("bloques").GetArrayLength());
        Assert.Equal(0, d.GetProperty("avisos").GetArrayLength());
    }

    [Fact]
    public void Cada_nivel_lleva_momentos_armaduras_y_apoyos_de_todas_sus_losas()
    {
        var nivel = Datos(ProyectoConRestaurante2()).GetProperty("niveles")[0];

        Assert.Equal("E1", nivel.GetProperty("nombre").GetString());
        Assert.Equal(5, nivel.GetProperty("mom").EnumerateObject().Count());
        Assert.Equal(5, nivel.GetProperty("ax").EnumerateObject().Count());
        Assert.Equal(5, nivel.GetProperty("ay").EnumerateObject().Count());
        Assert.Equal(2, nivel.GetProperty("apx").GetArrayLength());
        Assert.Equal(4, nivel.GetProperty("apy").GetArrayLength());
    }

    [Fact]
    public void Los_valores_van_como_texto_con_las_unidades_del_listado_de_Losas_exe()
    {
        var nivel = Datos(ProyectoConRestaurante2()).GetProperty("niveles")[0];
        var m1 = nivel.GetProperty("mom").GetProperty("1");
        var e1 = nivel.GetProperty("esp").GetProperty("1");

        Assert.Equal("40", m1.GetProperty("tipo").GetString());
        Assert.Equal("20.0", m1.GetProperty("H").GetString());          // cm
        Assert.Equal("6.850", m1.GetProperty("Lx").GetString());        // m
        Assert.Equal(1.280, double.Parse(m1.GetProperty("Mfx").GetString()!, System.Globalization.CultureInfo.InvariantCulture), 2);
        Assert.Equal("0.20", e1.GetProperty("h").GetString());          // m
        Assert.Equal("2D", e1.GetProperty("cond").GetString());
        Assert.Equal("0.72", nivel.GetProperty("carga").GetProperty("1").GetProperty("qu").GetString());
    }

    [Fact]
    public void Un_sistema_con_el_txt_importado_no_duplica_la_conversion_a_centimetros()
    {
        // Al importar el .TXT, SalidaPerdomo guarda H y d tal como los imprime
        // Losas.exe (cm); el cálculo nativo los guarda en metros.
        var p = ProyectoConRestaurante2(calcular: false);
        var sistema = p.Edificios[0].Niveles[0].Sistemas[0];
        string txt = Path.Combine(AppContext.BaseDirectory, "fixtures", "restaurante_2.TXT");
        sistema.SalidaPerdomo = SalidaPerdomoAdapter.FromFile(txt, sistema.Losas.Select(l => l.Id));

        var nivel = Datos(p).GetProperty("niveles")[0];

        Assert.Equal("20.0", nivel.GetProperty("mom").GetProperty("1").GetProperty("H").GetString());
        Assert.Equal("14.50", nivel.GetProperty("ax").GetProperty("1").GetProperty("d").GetString());
        Assert.Equal("16.5", nivel.GetProperty("apx")[0].GetProperty("d").GetString());
        Assert.Equal("restaurante_2.TXT", nivel.GetProperty("meta").GetProperty("archivo").GetString());
    }

    [Fact]
    public void Niveles_con_el_mismo_nombre_en_edificios_distintos_no_se_confunden()
    {
        var p = ProyectoConRestaurante2();
        p.Edificios[0].Nombre = "Torre A";
        var otro = new Edificio { Nombre = "Torre B" };
        var nivel = new Nivel { Nombre = "E1" };
        nivel.Sistemas.Add(DLFileService.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "restaurante_2.DL")));
        otro.Niveles.Add(nivel);
        p.Edificios.Add(otro);

        var nombres = Datos(p).GetProperty("niveles").EnumerateArray().Select(n => n.GetProperty("nombre").GetString()).ToList();

        Assert.Equal(new[] { "Torre A — E1", "Torre B — E1" }, nombres);
    }

    [Fact]
    public void Los_materiales_salen_del_sistema_y_no_de_un_valor_fijo()
    {
        var p = ProyectoConRestaurante2(calcular: false);
        var sistema = p.Edificios[0].Niveles[0].Sistemas[0];
        sistema.Fc = 0.280;
        sistema.Fy = 2.800;
        SistemaPieperMartensCalculator.Crear().CalcularYAplicar(sistema);

        var meta = Datos(p).GetProperty("niveles")[0].GetProperty("meta");

        Assert.Equal("280", meta.GetProperty("fc").GetString());
        Assert.Equal("2.8", meta.GetProperty("fy").GetString());
    }

    [Fact]
    public void Un_sistema_sin_resultados_se_exporta_vacio_y_con_aviso()
    {
        var d = Datos(ProyectoConRestaurante2(calcular: false));

        Assert.Equal(0, d.GetProperty("niveles")[0].GetProperty("mom").EnumerateObject().Count());
        Assert.Contains("sin calcular", d.GetProperty("avisos")[0].GetString());
    }

    [Fact]
    public void Las_losas_que_el_motor_no_pudo_calcular_quedan_nombradas_en_los_avisos()
    {
        var p = ProyectoConRestaurante2(calcular: false);
        var sistema = p.Edificios[0].Niveles[0].Sistemas[0];
        sistema.Losas[0].Tipo = 13;                      // tres apoyos: sin tabla verificada
        SistemaPieperMartensCalculator.Crear().CalcularYAplicar(sistema);

        var d = Datos(p);

        Assert.Equal(4, d.GetProperty("niveles")[0].GetProperty("mom").EnumerateObject().Count());
        Assert.Contains("1", d.GetProperty("avisos")[0].GetString());
        Assert.Contains("no se calcularon", d.GetProperty("avisos")[0].GetString());
    }

    [Fact]
    public void Dos_sistemas_en_un_mismo_nivel_se_distinguen_por_nombre()
    {
        var p = ProyectoConRestaurante2();
        var otro = DLFileService.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "restaurante_2.DL"));
        otro.Nombre = "Ala B";
        p.Edificios[0].Niveles[0].Sistemas.Add(otro);

        var nombres = Datos(p).GetProperty("niveles").EnumerateArray().Select(n => n.GetProperty("nombre").GetString()).ToList();

        Assert.Equal(2, nombres.Count);
        Assert.Equal(2, nombres.Distinct().Count());
        Assert.Contains(nombres, n => n!.Contains("Ala B"));
    }

    [Fact]
    public void La_configuracion_inicial_toma_la_portada_y_el_responsable_del_proyecto()
    {
        var p = ProyectoConRestaurante2();
        p.Autor = "Ing. de Prueba";
        p.Codia = "00000";
        p.Ciudad = "Ciudad X";
        p.MesAno = "Enero 2030";
        p.Uso = "Residencial";

        var c = JsonDocument.Parse(MemoriaDisenoExporter.ConfiguracionInicial(p)).RootElement;

        Assert.Equal("Edificio de prueba", c.GetProperty("portada").GetProperty("titulo").GetString());
        Assert.Equal("Ciudad X · Enero 2030", c.GetProperty("portada").GetProperty("lugar_fecha").GetString());
        Assert.Equal("Ing. de Prueba", c.GetProperty("responsable").GetProperty("nombre").GetString());
        Assert.Equal("CODIA 00000", c.GetProperty("responsable").GetProperty("credencial").GetString());
        Assert.Contains(c.GetProperty("proyecto").EnumerateArray(), f => f[0].GetString() == "Uso" && f[1].GetString() == "Residencial");
    }

    [Fact]
    public void Los_materiales_de_la_portada_son_los_del_calculo_y_no_los_valores_por_defecto_del_proyecto()
    {
        var p = ProyectoConRestaurante2();          // el sistema usa 0.210 y 4.200 ton/cm²
        p.FcKgCm2 = 280;
        p.FyKgCm2 = 2800;

        var materiales = JsonDocument.Parse(MemoriaDisenoExporter.ConfiguracionInicial(p)).RootElement
            .GetProperty("materiales").EnumerateArray().Select(f => f[1].GetString()).ToList();

        Assert.Equal(new[] { "f'c = 210 kg/cm²", "fy = 4,200 kg/cm²" }, materiales);
    }

    [Fact]
    public void La_configuracion_inicial_omite_los_campos_que_el_proyecto_no_tiene()
    {
        var p = ProyectoConRestaurante2();
        p.Codia = "";
        p.Uso = "";

        var c = JsonDocument.Parse(MemoriaDisenoExporter.ConfiguracionInicial(p)).RootElement;

        Assert.Equal("", c.GetProperty("responsable").GetProperty("credencial").GetString());
        Assert.DoesNotContain(c.GetProperty("proyecto").EnumerateArray(), f => f[0].GetString() == "Uso");
    }
}
