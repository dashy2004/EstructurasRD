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
    private readonly string _temasPath;

    public ConfiguracionTests()
    {
        _perfilPath     = Path.Combine(Path.GetTempPath(), $"perfil_test_{Guid.NewGuid():N}.json");
        _aparienciaPath = Path.Combine(Path.GetTempPath(), $"apariencia_test_{Guid.NewGuid():N}.json");
        _temasPath      = Path.Combine(Path.GetTempPath(), $"themes_test_{Guid.NewGuid():N}.json");
        PerfilIngenieroService.PathOverride = _perfilPath;
        AparienciaService.PathOverride       = _aparienciaPath;
        TemasPresetService.PathOverride      = _temasPath;
    }

    public void Dispose()
    {
        foreach (var p in new[] { _perfilPath, _aparienciaPath, _temasPath })
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        PerfilIngenieroService.PathOverride = null;
        AparienciaService.PathOverride      = null;
        TemasPresetService.PathOverride     = null;
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

    // =================================================================
    // ConfiguracionViewModel.EsCalculadora — flag para LosasPlus
    // =================================================================

    [Fact]
    public void ConfiguracionVM_default_muestra_DatosIngeniero_y_sub_tab_inicial_es_DatosIngeniero()
    {
        var vm = new MemoriaPlus.ViewModels.ConfiguracionViewModel();
        Assert.False(vm.EsCalculadora);
        Assert.True (vm.MostrarDatosIngeniero);
        Assert.Equal(MemoriaPlus.ViewModels.SubTabConfig.DatosIngeniero, vm.SubTabActivo);
    }

    [Fact]
    public void ConfiguracionVM_EsCalculadora_oculta_DatosIngeniero_y_redirige_a_Apariencia()
    {
        var vm = new MemoriaPlus.ViewModels.ConfiguracionViewModel();
        // El default era DatosIngeniero; al setear EsCalculadora=true, el VM debe
        // detectar que estábamos en el sub-tab oculto y saltar al primero visible.
        vm.EsCalculadora = true;

        Assert.True (vm.EsCalculadora);
        Assert.False(vm.MostrarDatosIngeniero);
        Assert.Equal(MemoriaPlus.ViewModels.SubTabConfig.Apariencia, vm.SubTabActivo);
    }

    [Fact]
    public void ConfiguracionVM_EsCalculadora_no_pisa_sub_tab_si_ya_es_otro()
    {
        var vm = new MemoriaPlus.ViewModels.ConfiguracionViewModel();
        vm.SubTabActivo = MemoriaPlus.ViewModels.SubTabConfig.Atajos;
        vm.EsCalculadora = true;
        // Como ya estaba en Atajos (no en DatosIngeniero), no debe cambiar.
        Assert.Equal(MemoriaPlus.ViewModels.SubTabConfig.Atajos, vm.SubTabActivo);
    }

    // =================================================================
    // TemasPresetService — CRUD de presets nombrados
    // =================================================================

    [Fact]
    public void Presets_LoadAll_devuelve_lista_vacia_si_no_existe_archivo()
    {
        var lista = TemasPresetService.LoadAll();
        Assert.NotNull(lista);
        Assert.Empty(lista);
    }

    [Fact]
    public void Presets_AddOrUpdate_persiste_y_LoadAll_lo_recupera()
    {
        var config = new AparienciaConfig { Tema = "Oscuro", ColorAcentoHex = "#0E639C" };
        TemasPresetService.AddOrUpdate("Estructural oscuro", config);

        var lista = TemasPresetService.LoadAll();
        Assert.Single(lista);
        Assert.Equal("Estructural oscuro", lista[0].Nombre);
        Assert.Equal("Oscuro",  lista[0].Apariencia.Tema);
        Assert.Equal("#0E639C", lista[0].Apariencia.ColorAcentoHex);
    }

    [Fact]
    public void Presets_AddOrUpdate_es_idempotente_por_nombre_case_insensitive()
    {
        TemasPresetService.AddOrUpdate("Oficina", new AparienciaConfig { Tema = "Claro" });
        TemasPresetService.AddOrUpdate("OFICINA", new AparienciaConfig { Tema = "Oscuro" });

        var lista = TemasPresetService.LoadAll();
        Assert.Single(lista);
        // El último gana — Oscuro reemplaza Claro
        Assert.Equal("Oscuro", lista[0].Apariencia.Tema);
    }

    [Fact]
    public void Presets_Remove_borra_por_nombre_case_insensitive()
    {
        TemasPresetService.AddOrUpdate("A", new AparienciaConfig());
        TemasPresetService.AddOrUpdate("B", new AparienciaConfig());

        Assert.True(TemasPresetService.Remove("a"));
        var lista = TemasPresetService.LoadAll();
        Assert.Single(lista);
        Assert.Equal("B", lista[0].Nombre);
    }

    [Fact]
    public void Presets_Remove_devuelve_false_si_no_existe()
    {
        Assert.False(TemasPresetService.Remove("inexistente"));
    }

    [Fact]
    public void Presets_Get_devuelve_null_si_no_existe()
    {
        Assert.Null(TemasPresetService.Get("inexistente"));
    }

    [Fact]
    public void Presets_Get_devuelve_el_preset_si_existe()
    {
        TemasPresetService.AddOrUpdate("Foo", new AparienciaConfig { Densidad = "Cómodo" });
        var p = TemasPresetService.Get("foo");  // case insensitive
        Assert.NotNull(p);
        Assert.Equal("Cómodo", p!.Apariencia.Densidad);
    }

    // =================================================================
    // ConfiguracionViewModel — commands de presets
    // =================================================================

    [Fact]
    public void ConfiguracionVM_GuardarComoPreset_persiste_y_refresca_lista()
    {
        var vm = new MemoriaPlus.ViewModels.ConfiguracionViewModel();
        vm.Apariencia.Tema = "Oscuro";
        vm.NombrePresetNuevo = "Test1";

        vm.GuardarComoPresetCommand.Execute(null);

        Assert.Contains("Test1", vm.PresetsDisponibles);
        Assert.Equal("Test1", vm.PresetSeleccionado);
        // Tras guardar, el nombre del input se limpia para permitir guardar otro
        Assert.Equal("", vm.NombrePresetNuevo);
    }

    [Fact]
    public void ConfiguracionVM_GuardarComoPreset_rechaza_nombre_vacio()
    {
        var vm = new MemoriaPlus.ViewModels.ConfiguracionViewModel();
        vm.NombrePresetNuevo = "   ";
        vm.GuardarComoPresetCommand.Execute(null);
        Assert.Empty(vm.PresetsDisponibles);
        Assert.Contains("nombre", vm.StatusGuardado, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfiguracionVM_AplicarColorAcentoHex_setea_y_normaliza_hex()
    {
        var vm = new MemoriaPlus.ViewModels.ConfiguracionViewModel();
        vm.AplicarColorAcentoHexCommand.Execute("0E639C");  // sin #
        Assert.Equal("#0E639C", vm.Apariencia.ColorAcentoHex);

        vm.AplicarColorAcentoHexCommand.Execute("");  // limpiar
        Assert.Equal("", vm.Apariencia.ColorAcentoHex);
    }
}
