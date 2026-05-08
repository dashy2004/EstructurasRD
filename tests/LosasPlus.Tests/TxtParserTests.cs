using System.IO;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del parser de .TXT contra fixtures reales producidos por Losas.exe (motor: F. Perdomo).
/// Si se agregan más fixtures, agregar el correspondiente método [Fact] o usar [Theory] / [InlineData].
/// </summary>
public class TxtParserTests
{
    private const string FIX_ADN = "fixtures/sistema_demo_27_losas.TXT";

    private static ParsedOutput LoadFixture(string relPath)
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, relPath);
        Assert.True(File.Exists(path), $"Fixture no encontrada: {path}");
        return TxtParser.ParseFile(path);
    }

    // ---------- Header / metadata ----------

    [Fact]
    public void Encabezado_extrae_sistema_y_metadatos()
    {
        var p = LoadFixture(FIX_ADN);
        Assert.Equal("SISTEMA DEMO 27 LOSAS", p.Sistema);
        Assert.Equal("SISTEMA DEMO 27 LOSAS.DL", p.Archivo);
        Assert.Equal("Apr 29, 2026", p.Fecha);
        Assert.Equal("5.20", p.Version);
        Assert.Equal("ACI 318-08", p.Reglamento);
        Assert.NotNull(p.FcRaw);
        Assert.Contains("210", p.FcRaw);
        Assert.NotNull(p.FyRaw);
        Assert.Contains("4.2", p.FyRaw);
    }

    // ---------- Momentos ----------

    [Fact]
    public void Momentos_27_losas_detectadas()
    {
        var p = LoadFixture(FIX_ADN);
        Assert.Equal(27, p.PorLosa.Count);
        Assert.Equal(Enumerable.Range(1, 27).ToArray(),
                     p.PorLosa.Select(l => l.Id).ToArray());
    }

    [Fact]
    public void Momentos_losa1_valores_exactos()
    {
        var p = LoadFixture(FIX_ADN);
        var l = p.PorLosa.First(x => x.Id == 1);
        Assert.Equal(60, l.Tipo);
        Assert.Equal(1.550, l.Carga);
        Assert.Equal(25.0, l.H);
        Assert.Equal(4.000, l.Lx);
        Assert.Equal(7.800, l.Ly);
        Assert.Equal(1.603, l.Mfx);
        Assert.Equal(0.422, l.Mfy);
        Assert.Equal(1.933, l.MSx);
        Assert.Equal(1.343, l.MSy);
    }

    [Fact]
    public void Momentos_losa7_valores_exactos()
    {
        // Losa 7 tiene los momentos más altos del sistema, buen caso de test.
        var p = LoadFixture(FIX_ADN);
        var l = p.PorLosa.First(x => x.Id == 7);
        Assert.Equal(60, l.Tipo);
        Assert.Equal(2.629, l.Mfx);
        Assert.Equal(4.424, l.Mfy);
        Assert.Equal(4.307, l.MSx);
        Assert.Equal(6.365, l.MSy);
    }

    [Fact]
    public void Momentos_losa_con_ceros_se_parsean_correctamente()
    {
        // Losa 5 tiene Mfx=0 (es un voladizo según la matriz original); que no falle.
        var p = LoadFixture(FIX_ADN);
        var l = p.PorLosa.First(x => x.Id == 5);
        Assert.Equal(0.0, l.Mfx);
        Assert.Equal(3.019, l.Mfy);
        Assert.Equal(0.0, l.MSx);
        Assert.Equal(3.020, l.MSy);
    }

    // ---------- Armaduras X y Y (vano) ----------

    [Fact]
    public void Armaduras_X_tiene_27_entradas_con_disponer()
    {
        var p = LoadFixture(FIX_ADN);
        var conX = p.PorLosa.Where(l => l.DisponerX != null).ToList();
        Assert.Equal(27, conX.Count);
        Assert.All(conX, l => Assert.NotNull(l.AsxReq));
        Assert.All(conX, l => Assert.NotNull(l.AsxProv));
    }

    [Fact]
    public void Armaduras_X_losa1_disposicion_correcta()
    {
        var p = LoadFixture(FIX_ADN);
        var l = p.PorLosa.First(x => x.Id == 1);
        Assert.Equal(21.75, l.Dx);
        Assert.Equal(1.603, l.Mux);
        Assert.Equal(4.500, l.AsxReq);
        Assert.Equal("Ø3/8\" @ 15 cm", l.DisponerX);
        Assert.Equal(4.733, l.AsxProv);
    }

    [Fact]
    public void Armaduras_Y_losa7_es_la_unica_con_diametro_distinto()
    {
        // Losa 7, Y, exige Ø1/2" @ 22 cm — es el único caso del fixture con Ø distinto a 3/8".
        var p = LoadFixture(FIX_ADN);
        var l = p.PorLosa.First(x => x.Id == 7);
        Assert.Equal(21.75, l.Dy);
        Assert.Equal(4.424, l.Muy);
        Assert.Equal(5.547, l.AsyReq);
        Assert.Equal("Ø1/2\" @ 22 cm", l.DisponerY);
        Assert.Equal(5.773, l.AsyProv);
    }

    [Fact]
    public void Armaduras_X_y_Y_pueden_tener_d_distintos()
    {
        // Losa 1: Dx=21.75 (paralelo al lado largo), Dy=19.25.
        var p = LoadFixture(FIX_ADN);
        var l = p.PorLosa.First(x => x.Id == 1);
        Assert.NotEqual(l.Dx, l.Dy);
        Assert.Equal(19.25, l.Dy);
    }

    // ---------- Apoyos sobre soportes ----------

    [Fact]
    public void Apoyos_X_tiene_13_entradas_y_Apoyos_Y_tiene_9()
    {
        var p = LoadFixture(FIX_ADN);
        var x = p.Apoyos.Where(a => a.Direccion == "X").ToList();
        var y = p.Apoyos.Where(a => a.Direccion == "Y").ToList();
        Assert.Equal(13, x.Count);
        Assert.Equal(9, y.Count);
    }

    [Fact]
    public void Apoyo_X_primer_borde_11_12_valores_exactos()
    {
        var p = LoadFixture(FIX_ADN);
        var ap = p.Apoyos.First(a => a.Direccion == "X" && a.BordeI == 11 && a.BordeJ == 12);
        Assert.Equal(0.466, ap.MuI);
        Assert.Equal(0.503, ap.MuJ);
        Assert.Equal(0.484, ap.MuIJ);
        Assert.Equal(21.8, ap.D);
        Assert.Equal(4.500, ap.As);
        Assert.Equal(0.0, ap.AsI);
        Assert.Equal(0.0, ap.AsJ);
        Assert.Equal(4.500, ap.DAs);
        Assert.Equal("Ø3/8\" @ 15 cm", ap.Disponer);
    }

    [Fact]
    public void Apoyo_Y_borde_5_7_disponer_es_diametro_grande()
    {
        // Borde 5-7 según Y: el de mayor demanda — Ø1/2" @ 21 cm.
        var p = LoadFixture(FIX_ADN);
        var ap = p.Apoyos.First(a => a.Direccion == "Y" && a.BordeI == 5 && a.BordeJ == 7);
        Assert.Equal(3.020, ap.MuI);
        Assert.Equal(6.365, ap.MuJ);
        Assert.Equal(4.774, ap.MuIJ);
        Assert.Equal(6.001, ap.As);
        Assert.Equal("Ø1/2\" @ 21 cm", ap.Disponer);
    }

    [Fact]
    public void Apoyo_X_borde_7_21_separacion_minima_9_cm()
    {
        // Borde 7-21 (X): caso extremo — Ø3/8" @ 9 cm, AsReq=7.518.
        var p = LoadFixture(FIX_ADN);
        var ap = p.Apoyos.First(a => a.Direccion == "X" && a.BordeI == 7 && a.BordeJ == 21);
        Assert.Equal(7.518, ap.As);
        Assert.Equal(7.518, ap.DAs);
        Assert.Equal("Ø3/8\" @ 9 cm", ap.Disponer);
    }

    // ---------- Apply() — integración con el modelo ----------

    [Fact]
    public void Apply_inyecta_Mfx_Mfy_y_disposiciones_en_Losa_existente()
    {
        var p = LoadFixture(FIX_ADN);
        var losas = new[]
        {
            new Losa { Id = 1, Tipo = 60, Carga = 1.55, Espesor = 0.25, Lx = 4, Ly = 7.8, Rec = 0.02 },
            new Losa { Id = 7, Tipo = 60, Carga = 1.35, Espesor = 0.25, Lx = 11.35, Ly = 8.75, Rec = 0.02 },
        };
        TxtParser.Apply(p, losas);
        Assert.Equal(1.603, losas[0].Mfx);
        Assert.Equal(0.422, losas[0].Mfy);
        Assert.Equal(1.933, losas[0].MSx);
        Assert.Equal(1.343, losas[0].MSy);
        Assert.Equal("Ø3/8\" @ 15 cm", losas[0].AsxVano);

        Assert.Equal(2.629, losas[1].Mfx);
        Assert.Equal(4.424, losas[1].Mfy);
        Assert.Equal("Ø1/2\" @ 22 cm", losas[1].AsyVano);
    }

    [Fact]
    public void Apply_no_pisa_losas_no_presentes_en_el_TXT()
    {
        var p = LoadFixture(FIX_ADN);
        var l = new Losa { Id = 999, Mfx = 42.0 };
        TxtParser.Apply(p, new[] { l });
        Assert.Equal(42.0, l.Mfx);  // sin cambios
    }

    // ---------- Robustness ----------

    [Fact]
    public void Parse_string_vacio_retorna_output_vacio_sin_excepcion()
    {
        var p = TxtParser.Parse("");
        Assert.Empty(p.PorLosa);
        Assert.Empty(p.Apoyos);
    }

    [Fact]
    public void Parse_texto_no_relacionado_no_falsea_secciones()
    {
        var p = TxtParser.Parse("Hello world\nLine 2\nNothing structural here.");
        Assert.Empty(p.PorLosa);
        Assert.Empty(p.Apoyos);
    }

    [Fact]
    public void ParseRaw_es_idempotente_al_reparsear_su_propio_Raw()
    {
        var first = LoadFixture(FIX_ADN);
        var second = TxtParser.ParseRaw(first.Raw);
        Assert.Equal(first.PorLosa.Count, second.PorLosa.Count);
        Assert.Equal(first.Apoyos.Count, second.Apoyos.Count);
        Assert.Equal(first.Sistema, second.Sistema);
    }
}
