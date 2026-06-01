using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LosasPlus.Persistence;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="PlantillaRegistry"/>: round-trip JSON, agregar/quitar,
/// unicidad de la predeterminada, bootstrap automático y manejo de archivos
/// corruptos.
/// </summary>
public class PlantillaRegistryTests : IDisposable
{
    private readonly string _registryPath;
    private readonly List<string> _tempFiles = new();

    public PlantillaRegistryTests()
    {
        _registryPath = Path.Combine(Path.GetTempPath(), $"plantillas_test_{Guid.NewGuid():N}.json");
        PlantillaRegistry.PathOverride = _registryPath;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        try { if (File.Exists(_registryPath)) File.Delete(_registryPath); } catch { }
        PlantillaRegistry.PathOverride = null;
    }

    private string TempDocx()
    {
        var path = Path.Combine(Path.GetTempPath(), $"plantilla_test_{Guid.NewGuid():N}.docx");
        File.WriteAllText(path, "fake docx content");
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void Load_inicial_devuelve_lista_vacia()
    {
        Assert.Empty(PlantillaRegistry.Load());
    }

    [Fact]
    public void Agregar_persiste_y_genera_id_unico()
    {
        var docx = TempDocx();
        var entry = PlantillaRegistry.AgregarDesdeArchivo(docx, "Mi Plantilla");
        Assert.NotEmpty(entry.Id);
        Assert.Equal("Mi Plantilla", entry.Nombre);
        Assert.Equal(Path.GetFullPath(docx), entry.Path);
        Assert.False(entry.EsPredeterminada);

        var loaded = PlantillaRegistry.Load();
        Assert.Single(loaded);
        Assert.Equal(entry.Id, loaded[0].Id);
    }

    [Fact]
    public void Agregar_dos_veces_mismo_path_actualiza_en_lugar_de_duplicar()
    {
        var docx = TempDocx();
        var a = PlantillaRegistry.AgregarDesdeArchivo(docx, "Nombre A");
        var b = PlantillaRegistry.AgregarDesdeArchivo(docx, "Nombre B");
        Assert.Equal(a.Id, b.Id);
        var loaded = PlantillaRegistry.Load();
        Assert.Single(loaded);
        Assert.Equal("Nombre B", loaded[0].Nombre);
    }

    [Fact]
    public void Agregar_con_path_inexistente_lanza()
    {
        var fake = Path.Combine(Path.GetTempPath(), $"no_existe_{Guid.NewGuid():N}.docx");
        Assert.Throws<FileNotFoundException>(() => PlantillaRegistry.AgregarDesdeArchivo(fake, "X"));
    }

    [Fact]
    public void Agregar_con_nombre_vacio_usa_filename()
    {
        var docx = TempDocx();
        var entry = PlantillaRegistry.AgregarDesdeArchivo(docx, "");
        Assert.Equal(Path.GetFileNameWithoutExtension(docx), entry.Nombre);
    }

    [Fact]
    public void MarcarComoPredeterminada_garantiza_unicidad()
    {
        var a = PlantillaRegistry.AgregarDesdeArchivo(TempDocx(), "A");
        var b = PlantillaRegistry.AgregarDesdeArchivo(TempDocx(), "B");
        var c = PlantillaRegistry.AgregarDesdeArchivo(TempDocx(), "C");

        PlantillaRegistry.MarcarComoPredeterminada(a.Id);
        var loaded = PlantillaRegistry.Load();
        Assert.True(loaded.Single(e => e.Id == a.Id).EsPredeterminada);
        Assert.False(loaded.Single(e => e.Id == b.Id).EsPredeterminada);

        // Cambiar a otra debe limpiar la anterior.
        PlantillaRegistry.MarcarComoPredeterminada(c.Id);
        loaded = PlantillaRegistry.Load();
        Assert.False(loaded.Single(e => e.Id == a.Id).EsPredeterminada);
        Assert.True(loaded.Single(e => e.Id == c.Id).EsPredeterminada);
        Assert.Equal(1, loaded.Count(e => e.EsPredeterminada));
    }

    [Fact]
    public void MarcarComoPredeterminada_con_id_invalido_es_noop()
    {
        PlantillaRegistry.AgregarDesdeArchivo(TempDocx(), "A");
        PlantillaRegistry.MarcarComoPredeterminada("id-que-no-existe");
        var loaded = PlantillaRegistry.Load();
        Assert.All(loaded, e => Assert.False(e.EsPredeterminada));
    }

    [Fact]
    public void Quitar_elimina_la_entrada()
    {
        var a = PlantillaRegistry.AgregarDesdeArchivo(TempDocx(), "A");
        var b = PlantillaRegistry.AgregarDesdeArchivo(TempDocx(), "B");
        PlantillaRegistry.Quitar(a.Id);
        var loaded = PlantillaRegistry.Load();
        Assert.Single(loaded);
        Assert.Equal(b.Id, loaded[0].Id);
    }

    [Fact]
    public void Quitar_id_inexistente_no_falla()
    {
        PlantillaRegistry.AgregarDesdeArchivo(TempDocx(), "A");
        PlantillaRegistry.Quitar("id-fantasma");
        Assert.Single(PlantillaRegistry.Load());
    }

    [Fact]
    public void GetPredeterminada_devuelve_la_marcada()
    {
        PlantillaRegistry.AgregarDesdeArchivo(TempDocx(), "A");
        var b = PlantillaRegistry.AgregarDesdeArchivo(TempDocx(), "B");
        PlantillaRegistry.MarcarComoPredeterminada(b.Id);
        var pred = PlantillaRegistry.GetPredeterminada();
        Assert.NotNull(pred);
        Assert.Equal(b.Id, pred!.Id);
    }

    [Fact]
    public void GetPredeterminada_sin_flag_devuelve_la_primera()
    {
        var a = PlantillaRegistry.AgregarDesdeArchivo(TempDocx(), "A");
        PlantillaRegistry.AgregarDesdeArchivo(TempDocx(), "B");
        var pred = PlantillaRegistry.GetPredeterminada();
        Assert.NotNull(pred);
        Assert.Equal(a.Id, pred!.Id);
    }

    [Fact]
    public void GetPredeterminada_registry_vacio_devuelve_null()
    {
        Assert.Null(PlantillaRegistry.GetPredeterminada());
    }

    [Fact]
    public void Load_filtra_entries_con_archivo_borrado()
    {
        var docx = TempDocx();
        PlantillaRegistry.AgregarDesdeArchivo(docx, "Va a desaparecer");
        File.Delete(docx);
        // El test cleanup ya no encuentra el archivo — ok.
        var loaded = PlantillaRegistry.Load();
        Assert.Empty(loaded);
    }

    [Fact]
    public void Load_archivo_corrupto_devuelve_lista_vacia()
    {
        File.WriteAllText(_registryPath, "{ esto no es json válido");
        Assert.Empty(PlantillaRegistry.Load());
    }

    // -----------------------------------------------------------------
    // Bootstrap
    // -----------------------------------------------------------------

    [Fact]
    public void Bootstrap_registra_la_plantilla_bundleada_si_vacio()
    {
        var bundled = TempDocx();
        var bootstrapped = PlantillaRegistry.BootstrapIfNeeded(bundled);
        Assert.True(bootstrapped);
        var loaded = PlantillaRegistry.Load();
        Assert.Single(loaded);
        Assert.True(loaded[0].EsPredeterminada);
    }

    [Fact]
    public void Bootstrap_idempotente_no_duplica()
    {
        var bundled = TempDocx();
        PlantillaRegistry.BootstrapIfNeeded(bundled);
        var segundo = PlantillaRegistry.BootstrapIfNeeded(bundled);
        Assert.False(segundo);
        Assert.Single(PlantillaRegistry.Load());
    }

    [Fact]
    public void Bootstrap_sin_archivo_bundled_es_noop()
    {
        var fake = Path.Combine(Path.GetTempPath(), $"no_existe_{Guid.NewGuid():N}.docx");
        Assert.False(PlantillaRegistry.BootstrapIfNeeded(fake));
        Assert.Empty(PlantillaRegistry.Load());
    }

    [Fact]
    public void Clear_borra_todo()
    {
        PlantillaRegistry.AgregarDesdeArchivo(TempDocx(), "A");
        Assert.Single(PlantillaRegistry.Load());
        PlantillaRegistry.Clear();
        Assert.Empty(PlantillaRegistry.Load());
    }
}
