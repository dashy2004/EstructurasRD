using System;
using System.IO;
using LosasPlus.Persistence;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="AtajosConfig"/> + <see cref="AtajosService"/>:
/// defaults, round-trip JSON, override por id, restaurar defaults,
/// tolerancia a archivo corrupto.
/// </summary>
public class AtajosConfigTests : IDisposable
{
    private readonly string _path;

    public AtajosConfigTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"atajos_test_{Guid.NewGuid():N}.json");
        AtajosService.PathOverride = _path;
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        AtajosService.PathOverride = null;
    }

    [Fact]
    public void Defaults_coinciden_con_atajos_hardcoded_pre_commit_29()
    {
        var cfg = new AtajosConfig();
        Assert.Equal("Ctrl+N",       cfg.NuevoProyecto);
        Assert.Equal("Ctrl+O",       cfg.Abrir);
        Assert.Equal("Ctrl+S",       cfg.Guardar);
        Assert.Equal("Ctrl+Shift+S", cfg.GuardarComo);
        Assert.Equal("Ctrl+G",       cfg.Generar);
        Assert.Equal("Ctrl+L",       cfg.AgregarLosa);
        Assert.Equal("Ctrl+F",       cfg.Busqueda);
    }

    [Fact]
    public void Load_inicial_devuelve_defaults_si_no_existe_archivo()
    {
        var cfg = AtajosService.Load();
        Assert.Equal("Ctrl+N", cfg.NuevoProyecto);
    }

    [Fact]
    public void Save_y_Load_round_trip_preserva_overrides()
    {
        var cfg = new AtajosConfig
        {
            NuevoProyecto = "Ctrl+Alt+N",
            Busqueda      = "Ctrl+K",
        };
        AtajosService.Save(cfg);

        var loaded = AtajosService.Load();
        Assert.Equal("Ctrl+Alt+N", loaded.NuevoProyecto);
        Assert.Equal("Ctrl+K",     loaded.Busqueda);
        Assert.Equal("Ctrl+S",     loaded.Guardar);  // sin tocar = default
    }

    [Fact]
    public void Get_devuelve_gesture_por_id()
    {
        var cfg = new AtajosConfig { Busqueda = "Ctrl+K" };
        Assert.Equal("Ctrl+K", cfg.Get(AtajoIds.Busqueda));
        Assert.Equal("Ctrl+N", cfg.Get(AtajoIds.NuevoProyecto));
    }

    [Fact]
    public void Get_con_id_desconocido_devuelve_cadena_vacia()
    {
        var cfg = new AtajosConfig();
        Assert.Equal("", cfg.Get("idDesconocido"));
    }

    [Fact]
    public void Set_actualiza_la_propiedad_correcta()
    {
        var cfg = new AtajosConfig();
        cfg.Set(AtajoIds.Generar, "Ctrl+Alt+G");
        Assert.Equal("Ctrl+Alt+G", cfg.Generar);
    }

    [Fact]
    public void Set_con_id_desconocido_no_explota()
    {
        var cfg = new AtajosConfig();
        cfg.Set("idFantasma", "Ctrl+X");  // no-op
        Assert.Equal("Ctrl+N", cfg.NuevoProyecto);  // sin cambio
    }

    [Fact]
    public void RestaurarDefaults_vuelve_a_los_valores_de_fabrica()
    {
        var cfg = new AtajosConfig
        {
            NuevoProyecto = "Ctrl+M",
            Busqueda      = "F3",
            Generar       = "Alt+G",
        };
        cfg.RestaurarDefaults();
        Assert.Equal("Ctrl+N", cfg.NuevoProyecto);
        Assert.Equal("Ctrl+F", cfg.Busqueda);
        Assert.Equal("Ctrl+G", cfg.Generar);
    }

    [Fact]
    public void Load_archivo_corrupto_devuelve_defaults()
    {
        File.WriteAllText(_path, "{ esto no es json válido");
        var cfg = AtajosService.Load();
        Assert.Equal("Ctrl+N", cfg.NuevoProyecto);
    }

    [Fact]
    public void Save_null_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => AtajosService.Save(null!));
    }

    [Fact]
    public void PropertyChanged_se_dispara_en_setter()
    {
        var cfg = new AtajosConfig();
        string? raisedFor = null;
        cfg.PropertyChanged += (_, e) => raisedFor = e.PropertyName;
        cfg.NuevoProyecto = "Ctrl+M";
        Assert.Equal(nameof(AtajosConfig.NuevoProyecto), raisedFor);
    }

    [Fact]
    public void AtajoIds_Orden_lista_los_7_ids_canonicos()
    {
        Assert.Equal(7, AtajoIds.Orden.Count);
        Assert.Contains(AtajoIds.NuevoProyecto, AtajoIds.Orden);
        Assert.Contains(AtajoIds.Busqueda,      AtajoIds.Orden);
    }

    [Fact]
    public void AtajoIds_Etiqueta_devuelve_texto_legible()
    {
        Assert.Equal("Nuevo proyecto", AtajoIds.Etiqueta(AtajoIds.NuevoProyecto));
        Assert.Equal("Búsqueda global", AtajoIds.Etiqueta(AtajoIds.Busqueda));
    }
}
