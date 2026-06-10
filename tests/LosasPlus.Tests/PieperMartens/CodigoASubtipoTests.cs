using System;
using System.Linq;
using LosasPlus.Calculo.PieperMartens;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests.PieperMartens;

/// <summary>
/// F3: mapeo completo código .DL → sub-tipo de tabla Pieper-Martens.
/// Convención (spec 2026-06-10-f3 §3.3): d1 = nº de TABLA (1–6); d2 = 0 bloque
/// único, 1/2 = orientación a/b, 3/4 = tabla (d1+6) orientación a/b (borde
/// libre); 71/72 = voladizo one-way (fuera del diccionario, vía EsVoladizo).
/// Ancla verificada vs Losas.exe: 40 → "4". El resto pendiente de fixtures
/// del usuario en Windows (la corrección sería 1 línea del diccionario).
/// </summary>
public class CodigoASubtipoTests
{
    private static readonly TablaPieperMartens Tabla = TablaPieperMartens.Cargar();

    public static readonly TheoryData<int, string> Mapeo = new()
    {
        // 4 bordes apoyados (tablas 1–6)
        { 10, "1" }, { 21, "2a" }, { 22, "2b" }, { 31, "3a" }, { 32, "3b" },
        { 40, "4" }, { 51, "5a" }, { 52, "5b" }, { 60, "6" },
        // 3 bordes apoyados + 1 libre (tablas 7–12): código (d1)(3|4) → (d1+6) a|b
        { 13, "7a" },  { 14, "7b" },  { 23, "8a" },  { 24, "8b" },
        { 33, "9a" },  { 34, "9b" },  { 43, "10a" }, { 44, "10b" },
        { 53, "11a" }, { 54, "11b" }, { 63, "12a" }, { 64, "12b" },
    };

    [Theory]
    [MemberData(nameof(Mapeo))]
    public void SubtipoDeCodigoDL_mapea_los_21_codigos_de_tabla(int codigo, string subtipo)
        => Assert.Equal(subtipo, Tabla.SubtipoDeCodigoDL(codigo));

    [Fact]
    public void El_mapeo_es_biyectivo_y_alcanza_los_21_subtipos_del_json()
    {
        var subtipos = TipoLosa.CodigosValidos
            .Where(c => !MomentosCalculator.EsVoladizo(c, out _))
            .Select(c => Tabla.SubtipoDeCodigoDL(c))
            .ToList();
        Assert.Equal(21, subtipos.Count);
        Assert.Equal(21, subtipos.Distinct().Count());   // sin duplicados → biyección
        foreach (var st in subtipos)
            _ = Tabla.Factores(st, 1.0);                 // cada subtipo existe en el JSON
    }

    public static TheoryData<int> TodosLosCodigos()
    {
        var d = new TheoryData<int>();
        foreach (var c in TipoLosa.CodigosValidos.OrderBy(c => c)) d.Add(c);
        return d;
    }

    [Theory]
    [MemberData(nameof(TodosLosCodigos))]
    public void Calcular_no_lanza_para_ningun_codigo_del_catalogo(int codigo)
    {
        // Criterio del roadmap F3: ningún código del catálogo lanza NotSupportedException.
        var m = new MomentosCalculator(Tabla)
            .Calcular(new Losa { Id = 1, Tipo = codigo, Carga = 1.0, Lx = 6.0, Ly = 5.0 });
        Assert.True(double.IsFinite(m.Mfx) && double.IsFinite(m.Mfy)
                 && double.IsFinite(m.Msx) && double.IsFinite(m.Msy));
        Assert.True(m.Mfx >= 0 && m.Mfy >= 0 && m.Msx >= 0 && m.Msy >= 0);
    }

    [Theory]
    [InlineData("2a", "2b")] [InlineData("3a", "3b")] [InlineData("5a", "5b")]
    [InlineData("7a", "7b")] [InlineData("8a", "8b")] [InlineData("9a", "9b")]
    [InlineData("10a", "10b")] [InlineData("11a", "11b")] [InlineData("12a", "12b")]
    public void Los_pares_a_b_son_el_mismo_caso_girado_90_grados(string a, string b)
    {
        // En losa cuadrada (ε = 1.0, fila tabulada) girar 90° intercambia X↔Y.
        // Guarda contra swaps de orientación en futuras ediciones del JSON.
        var fa = Tabla.Factores(a, 1.0);
        var fb = Tabla.Factores(b, 1.0);
        Assert.Equal(fa.Fy, fb.Fx, Math.Abs(fa.Fy) * 0.02);
        Assert.Equal(fa.Fx, fb.Fy, Math.Abs(fa.Fx) * 0.02);
        IgualesEnAbs(fa.Sy, fb.Sx);
        IgualesEnAbs(fa.Sx, fb.Sy);
    }

    private static void IgualesEnAbs(double? esperado, double? actual)
    {
        Assert.Equal(esperado is null, actual is null);
        if (esperado is double e && actual is double a)
            Assert.Equal(Math.Abs(e), Math.Abs(a), Math.Abs(e) * 0.02);
    }
}
