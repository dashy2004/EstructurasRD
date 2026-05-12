using System;
using System.IO;
using LosasPlus.Persistence;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de los servicios de configuración (perfil del ingeniero + apariencia).
/// Ambos persisten en <c>%APPDATA%/MemoriaPlus/</c> en producción; aquí cada
/// test usa <c>PathOverride</c> para un temp file aislado.
/// </summary>
public class ConfiguracionTests : IDisposable
{
    private readonly string _perfilPath;
    private readonly string _aparienciaPath;

    public ConfiguracionTests()
    {
        _perfilPath     = Path.Combine(Path.GetTempPath(), $"perfil_test_{Guid.NewGuid():N}.json");
        _aparienciaPath = Path.Combine(Path.GetTempPath(), $"apariencia_test_{Guid.NewGuid():N}.json");
        PerfilIngenieroService.PathOverride = _perfilPath;
        AparienciaService.PathOverride       = _aparienciaPath;
    }

    public void Dispose()
    {
        foreach (var p in new[] { _perfilPath, _aparienciaPath })
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        PerfilIngenieroService.PathOverride = null;
        AparienciaService.PathOverride      = null;
    }

    // =================================================================
    // PerfilIngenieroService
    // =================================================================

    [Fact]
    public void Perfil_Load_devuelve_objeto_vacio_si_no_existe_archivo()
    {
        var p = PerfilIngenieroService.Load();
        Assert.NotNull(p);
        Assert.Empty(p.Nombre);
        Assert.Empty(p.Codia);
        Assert.False(PerfilIngenieroService.Existe());
    }

    [Fact]
    public void Perfil_RoundTrip_preserva_todos_los_campos()
    {
        var p1 = new PerfilIngeniero
        {
            Nombre          = "Ing. Test García",
            Codia           = "99999",
            Especialidad    = "Estructural",
            TelefonoFijo    = "(809) 000-0000",
            TelefonoCelular = "(809) 000-0001",
            Email           = "test@example.com",
            Ciudad          = "Santo Domingo",
            FirmaPath       = @"C:\firmas\firma.png",
            SelloPath       = @"C:\firmas\sello.png",
            Universidad     = "PUCMM",
            AnoGraduacion   = "2015",
            PostGrado       = "UNPHU - Estructuras 2020",
        };

        PerfilIngenieroService.Save(p1);
        Assert.True(PerfilIngenieroService.Existe());

        var p2 = PerfilIngenieroService.Load();
        Assert.Equal(p1.Nombre,           p2.Nombre);
        Assert.Equal(p1.Codia,            p2.Codia);
        Assert.Equal(p1.Especialidad,     p2.Especialidad);
        Assert.Equal(p1.TelefonoFijo,     p2.TelefonoFijo);
        Assert.Equal(p1.TelefonoCelular,  p2.TelefonoCelular);
        Assert.Equal(p1.Email,            p2.Email);
        Assert.Equal(p1.Ciudad,           p2.Ciudad);
        Assert.Equal(p1.FirmaPath,        p2.FirmaPath);
        Assert.Equal(p1.SelloPath,        p2.SelloPath);
        Assert.Equal(p1.Universidad,      p2.Universidad);
        Assert.Equal(p1.AnoGraduacion,    p2.AnoGraduacion);
        Assert.Equal(p1.PostGrado,        p2.PostGrado);
    }

    [Fact]
    public void Perfil_Load_resiliente_a_json_corrupto()
    {
        File.WriteAllText(_perfilPath, "{ esto no es json");
        var p = PerfilIngenieroService.Load();
        Assert.NotNull(p);
        Assert.Empty(p.Nombre);
    }

    [Fact]
    public void Perfil_Save_lanza_si_perfil_null()
    {
        Assert.Throws<ArgumentNullException>(() => PerfilIngenieroService.Save(null!));
    }

    [Fact]
    public void Perfil_Clear_borra_el_archivo()
    {
        PerfilIngenieroService.Save(new PerfilIngeniero { Nombre = "X" });
        Assert.True(PerfilIngenieroService.Existe());

        PerfilIngenieroService.Clear();
        Assert.False(PerfilIngenieroService.Existe());
    }

    // =================================================================
    // AparienciaService
    // =================================================================

    [Fact]
    public void Apariencia_Load_devuelve_defaults_si_no_existe_archivo()
    {
        var c = AparienciaService.Load();
        Assert.Equal("Claro",          c.Tema);
        Assert.Equal("JetBrains Mono", c.TipografiaDatos);
        Assert.Equal("Medio",          c.Densidad);
        Assert.Equal(28,               c.RowHeight);
        Assert.False(c.EsTemaOscuro);
    }

    [Fact]
    public void Apariencia_RoundTrip_preserva_preferencias()
    {
        var c1 = new AparienciaConfig
        {
            Tema             = "Oscuro",
            TipografiaDatos  = "Iosevka",
            Densidad         = "Compacto",
        };

        AparienciaService.Save(c1);
        var c2 = AparienciaService.Load();
        Assert.Equal("Oscuro",   c2.Tema);
        Assert.Equal("Iosevka",  c2.TipografiaDatos);
        Assert.Equal("Compacto", c2.Densidad);
        Assert.Equal(24,         c2.RowHeight);
        Assert.True(c2.EsTemaOscuro);
    }

    [Theory]
    [InlineData("Compacto", 24)]
    [InlineData("Medio",    28)]
    [InlineData("Cómodo",   32)]
    [InlineData("Cualquier_otro_valor", 28)]   // default fallback
    public void Apariencia_RowHeight_se_deriva_de_Densidad(string densidad, double esperado)
    {
        var c = new AparienciaConfig { Densidad = densidad };
        Assert.Equal(esperado, c.RowHeight);
    }

    [Fact]
    public void Apariencia_EsTemaOscuro_case_insensitive()
    {
        Assert.False(new AparienciaConfig { Tema = "Claro"  }.EsTemaOscuro);
        Assert.True (new AparienciaConfig { Tema = "Oscuro" }.EsTemaOscuro);
        Assert.True (new AparienciaConfig { Tema = "oscuro" }.EsTemaOscuro);
        Assert.True (new AparienciaConfig { Tema = "OSCURO" }.EsTemaOscuro);
    }

    [Fact]
    public void Apariencia_Reset_borra_el_archivo()
    {
        AparienciaService.Save(new AparienciaConfig { Tema = "Oscuro" });
        AparienciaService.Reset();

        // Tras reset, Load() devuelve los defaults (no recuerda "Oscuro").
        var c = AparienciaService.Load();
        Assert.Equal("Claro", c.Tema);
    }
}
