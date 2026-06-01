using System;
using System.IO;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="SalidaPerdomoAdapter"/>: convierte el output del
/// <see cref="TxtParser"/> en una <see cref="SalidaPerdomo"/> que el
/// generador de memorias consume para renderear las tablas de momentos y
/// armaduras dentro del bloque NIVEL.
/// </summary>
public class SalidaPerdomoAdapterTests
{
    private const string TxtFixture = "fixtures/sistema_demo_27_losas.TXT";

    // =================================================================
    // Smoke
    // =================================================================

    [Fact]
    public void FromFile_lee_el_txt_real_sin_lanzar()
    {
        var s = SalidaPerdomoAdapter.FromFile(TxtFixture, Enumerable.Empty<int>());
        Assert.NotNull(s);
        Assert.Equal(TxtFixture, s.ArchivoTxt);
    }

    [Fact]
    public void From_propaga_VersionMotor_desde_el_encabezado()
    {
        var s = SalidaPerdomoAdapter.FromFile(TxtFixture, Enumerable.Empty<int>());
        Assert.Equal("5.20", s.VersionMotor);
    }

    // =================================================================
    // Momentos
    // =================================================================

    [Fact]
    public void Momentos_se_pueblan_para_las_27_losas_del_fixture()
    {
        var s = SalidaPerdomoAdapter.FromFile(TxtFixture, Enumerable.Empty<int>());
        Assert.Equal(27, s.Momentos.Count);
    }

    [Fact]
    public void Momentos_de_la_losa_1_matchean_el_txt()
    {
        var s = SalidaPerdomoAdapter.FromFile(TxtFixture, Enumerable.Empty<int>());
        var m = s.Momentos.Single(x => x.LosaId == 1);

        Assert.Equal(60,    m.Tipo);
        Assert.Equal(1.550, m.Carga, precision: 3);
        Assert.Equal(25.0,  m.H,     precision: 1);
        Assert.Equal(4.000, m.Lx,    precision: 3);
        Assert.Equal(7.800, m.Ly,    precision: 3);
        Assert.Equal(1.603, m.Mfx,   precision: 3);
        Assert.Equal(0.422, m.Mfy,   precision: 3);
        Assert.Equal(1.933, m.NMSx,  precision: 3);
        Assert.Equal(1.343, m.NMSy,  precision: 3);
    }

    [Fact]
    public void Momentos_de_la_losa_27_matchean_el_txt()
    {
        var s = SalidaPerdomoAdapter.FromFile(TxtFixture, Enumerable.Empty<int>());
        var m = s.Momentos.Single(x => x.LosaId == 27);

        Assert.Equal(21,    m.Tipo);
        Assert.Equal(1.200, m.Carga, precision: 3);
        Assert.Equal(2.900, m.Lx,    precision: 3);
        Assert.Equal(1.000, m.Ly,    precision: 3);
        Assert.Equal(0.117, m.Mfy,   precision: 3);
    }

    // =================================================================
    // Armaduras de vano X / Y (centro)
    // =================================================================

    [Fact]
    public void ArmadurasXCentro_se_pueblan()
    {
        var s = SalidaPerdomoAdapter.FromFile(TxtFixture, Enumerable.Empty<int>());
        Assert.NotEmpty(s.ArmadurasXCentro);
    }

    [Fact]
    public void ArmadurasYCentro_se_pueblan()
    {
        var s = SalidaPerdomoAdapter.FromFile(TxtFixture, Enumerable.Empty<int>());
        Assert.NotEmpty(s.ArmadurasYCentro);
    }

    [Fact]
    public void ArmadurasXCentro_tienen_LosaId_unico_por_fila()
    {
        var s = SalidaPerdomoAdapter.FromFile(TxtFixture, Enumerable.Empty<int>());
        var ids = s.ArmadurasXCentro.Select(a => a.LosaId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    // =================================================================
    // Armaduras sobre apoyos X / Y
    // =================================================================

    [Fact]
    public void ArmadurasXApoyos_se_pueblan_con_pares_borde_I_J()
    {
        var s = SalidaPerdomoAdapter.FromFile(TxtFixture, Enumerable.Empty<int>());
        Assert.NotEmpty(s.ArmadurasXApoyos);
        // Cada apoyo debe tener BordeI y BordeJ válidos (>0).
        Assert.All(s.ArmadurasXApoyos, a =>
        {
            Assert.True(a.BordeI > 0);
            Assert.True(a.BordeJ > 0);
        });
    }

    [Fact]
    public void ArmadurasYApoyos_se_pueblan_con_pares_borde_I_J()
    {
        var s = SalidaPerdomoAdapter.FromFile(TxtFixture, Enumerable.Empty<int>());
        Assert.NotEmpty(s.ArmadurasYApoyos);
    }

    // =================================================================
    // Losas no parseadas
    // =================================================================

    [Fact]
    public void LosasNoParseadas_lista_los_IDs_esperados_que_faltan()
    {
        // Esperamos losas 1..30 pero el .txt solo tiene 1..27 → faltan 28, 29, 30.
        var esperadas = Enumerable.Range(1, 30);
        var s = SalidaPerdomoAdapter.FromFile(TxtFixture, esperadas);

        Assert.Contains(28, s.LosasNoParseadas);
        Assert.Contains(29, s.LosasNoParseadas);
        Assert.Contains(30, s.LosasNoParseadas);
        Assert.DoesNotContain(1,  s.LosasNoParseadas);
        Assert.DoesNotContain(27, s.LosasNoParseadas);
    }

    [Fact]
    public void LosasNoParseadas_es_vacio_si_todos_los_IDs_aparecen()
    {
        var esperadas = Enumerable.Range(1, 27);  // exactos
        var s = SalidaPerdomoAdapter.FromFile(TxtFixture, esperadas);
        Assert.Empty(s.LosasNoParseadas);
    }

    [Fact]
    public void LosasNoParseadas_es_vacio_si_no_se_pasan_esperadas()
    {
        var s = SalidaPerdomoAdapter.FromFile(TxtFixture, Enumerable.Empty<int>());
        Assert.Empty(s.LosasNoParseadas);
    }

    // =================================================================
    // Asignar a Sistema (use case real)
    // =================================================================

    [Fact]
    public void SalidaPerdomo_se_puede_asignar_a_Sistema_y_TieneSalidaPerdomo_es_true()
    {
        var sistema = new Sistema { Nombre = "E1" };
        for (int i = 1; i <= 27; i++)
            sistema.Losas.Add(new Losa { Id = i });

        var ids = sistema.Losas.Select(l => l.Id);
        var salida = SalidaPerdomoAdapter.FromFile(TxtFixture, ids);

        sistema.SalidaPerdomo = salida;

        Assert.True(sistema.TieneSalidaPerdomo);
        Assert.Equal(27, sistema.SalidaPerdomo.Momentos.Count);
        Assert.Empty(sistema.SalidaPerdomo.LosasNoParseadas);
    }

    // =================================================================
    // Edge cases
    // =================================================================

    [Fact]
    public void FromFile_lanza_si_archivo_no_existe()
    {
        Assert.Throws<FileNotFoundException>(() =>
            SalidaPerdomoAdapter.FromFile("fixtures/no-existe.TXT", Enumerable.Empty<int>()));
    }

    [Fact]
    public void From_lanza_si_parsed_es_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SalidaPerdomoAdapter.From(null!, "x.txt", Enumerable.Empty<int>()));
    }

    [Fact]
    public void From_lanza_si_losasEsperadas_es_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SalidaPerdomoAdapter.From(new ParsedOutput(), "x.txt", null!));
    }

    [Fact]
    public void From_acepta_ParsedOutput_vacio_y_devuelve_SalidaPerdomo_vacia()
    {
        var s = SalidaPerdomoAdapter.From(new ParsedOutput(), "fake.txt", Enumerable.Empty<int>());
        Assert.Empty(s.Momentos);
        Assert.Empty(s.ArmadurasXCentro);
        Assert.Empty(s.ArmadurasYCentro);
        Assert.Empty(s.ArmadurasXApoyos);
        Assert.Empty(s.ArmadurasYApoyos);
    }
}
