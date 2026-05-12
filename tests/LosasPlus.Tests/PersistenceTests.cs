using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LosasPlus.Calculo;
using LosasPlus.Models;
using LosasPlus.Persistence;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="ProyectoSerializer"/> y <see cref="ProyectoRegistry"/>.
/// Cubren round-trip de serialización, preservación de outputs computados, y
/// el comportamiento del registry de proyectos recientes.
/// </summary>
public class PersistenceTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly string _registryPath;

    public PersistenceTests()
    {
        // Override del path del registry para no tocar el del usuario real.
        _registryPath = Path.Combine(Path.GetTempPath(), $"recents_test_{Guid.NewGuid():N}.json");
        ProyectoRegistry.PathOverride = _registryPath;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        try { if (File.Exists(_registryPath)) File.Delete(_registryPath); } catch { }
        ProyectoRegistry.PathOverride = null;
    }

    private string TempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"proyecto_test_{Guid.NewGuid():N}.lpx.json");
        _tempFiles.Add(path);
        return path;
    }

    private static Proyecto BuildProyectoCompleto()
    {
        // ProyectoFactory para que las Cargas arranquen con SemillaPorDefecto
        // (15 filas en la tabla h↔qd, 3 pesos propios entrepiso/techo, etc.).
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Nombre                  = "Torre Test";
        p.Autor                   = "Ing. Demo";
        p.CodigoObra              = "X-001";
        p.Ubicacion               = "Santo Domingo";
        p.Descripcion             = "Test";
        p.Codia                   = "12345";
        p.TelefonoFijo            = "(809) 000-0000";
        p.TelefonoCelular         = "(809) 000-0001";
        p.DisenadorArquitectonico = "Arq. Demo";
        p.Ciudad                  = "Santo Domingo";
        p.MesAno                  = "12/2025";
        p.UbicacionCompleta       = "Av. Demo #1";
        p.Uso                     = "Residencial";
        p.CantidadNiveles         = 3;
        p.SistemaEstructural      = "Aporticado";
        p.TipoFundaciones         = "Zapatas";
        p.EsfuerzoAdmisible       = 2.5;
        p.ProfundidadDesplante    = 1.5;
        p.OtrosParametros         = "Suelo tipo B";
        p.FcKgCm2                 = 280;
        p.FyKgCm2                 = 4200;
        p.Normas.Add("ACI 318-05");
        p.Normas.Add("R-001");

        var e1 = new Sistema { Nombre = "E1", Uso = SistemaUso.Entrepiso, CotaMetros = 2.80 };
        e1.Losas.Add(new Losa { Id = 1, Lx = 6.45, Ly = 5.40, MampO = 1.78 });
        e1.Losas.Add(new Losa { Id = 2, Lx = 4.90, Ly = 4.45 });
        p.Sistemas.Add(e1);

        var techo = new Sistema { Nombre = "Techo", Uso = SistemaUso.Techo, CotaMetros = 5.60 };
        techo.Losas.Add(new Losa { Id = 1, Lx = 4.00, Ly = 4.00 });
        p.Sistemas.Add(techo);

        // Correr el engine para tener outputs computados (HCalc, Qd, Qu).
        CalculoEngine.RecalcularProyecto(p);

        return p;
    }

    // =================================================================
    // Round-trip serialization
    // =================================================================

    [Fact]
    public void Save_crea_un_archivo_no_vacio()
    {
        var path = TempFile();
        var p = BuildProyectoCompleto();

        ProyectoSerializer.Save(p, path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 100);
    }

    [Fact]
    public void RoundTrip_preserva_metadatos_de_portada()
    {
        var path = TempFile();
        var p1 = BuildProyectoCompleto();

        ProyectoSerializer.Save(p1, path);
        var p2 = ProyectoSerializer.Load(path);

        Assert.Equal(p1.Nombre,                p2.Nombre);
        Assert.Equal(p1.Autor,                 p2.Autor);
        Assert.Equal(p1.Codia,                 p2.Codia);
        Assert.Equal(p1.TelefonoFijo,          p2.TelefonoFijo);
        Assert.Equal(p1.TelefonoCelular,       p2.TelefonoCelular);
        Assert.Equal(p1.MesAno,                p2.MesAno);
        Assert.Equal(p1.UbicacionCompleta,     p2.UbicacionCompleta);
        Assert.Equal(p1.Uso,                   p2.Uso);
        Assert.Equal(p1.CantidadNiveles,       p2.CantidadNiveles);
        Assert.Equal(p1.SistemaEstructural,    p2.SistemaEstructural);
        Assert.Equal(p1.EsfuerzoAdmisible,     p2.EsfuerzoAdmisible);
        Assert.Equal(p1.ProfundidadDesplante,  p2.ProfundidadDesplante);
        Assert.Equal(p1.FcKgCm2,               p2.FcKgCm2);
        Assert.Equal(p1.FyKgCm2,               p2.FyKgCm2);
        Assert.Equal(p1.OtrosParametros,       p2.OtrosParametros);
    }

    [Fact]
    public void RoundTrip_preserva_normas()
    {
        var path = TempFile();
        var p1 = BuildProyectoCompleto();

        ProyectoSerializer.Save(p1, path);
        var p2 = ProyectoSerializer.Load(path);

        Assert.Equal(p1.Normas.Count, p2.Normas.Count);
        Assert.Contains("ACI 318-05", p2.Normas);
        Assert.Contains("R-001",      p2.Normas);
    }

    [Fact]
    public void RoundTrip_preserva_sistemas_y_losas()
    {
        var path = TempFile();
        var p1 = BuildProyectoCompleto();

        ProyectoSerializer.Save(p1, path);
        var p2 = ProyectoSerializer.Load(path);

        Assert.Equal(2, p2.Sistemas.Count);

        var e1 = p2.Sistemas[0];
        Assert.Equal("E1",                     e1.Nombre);
        Assert.Equal(SistemaUso.Entrepiso,     e1.Uso);
        Assert.Equal(2.80,                     e1.CotaMetros, precision: 2);
        Assert.Equal(2,                        e1.Losas.Count);

        var l1 = e1.Losas[0];
        Assert.Equal(1,        l1.Id);
        Assert.Equal(6.45,     l1.Lx,    precision: 2);
        Assert.Equal(5.40,     l1.Ly,    precision: 2);
        Assert.Equal(1.78,     l1.MampO, precision: 2);

        var techo = p2.Sistemas[1];
        Assert.Equal("Techo",           techo.Nombre);
        Assert.Equal(SistemaUso.Techo,  techo.Uso);
    }

    [Fact]
    public void RoundTrip_preserva_outputs_computados_del_engine()
    {
        var path = TempFile();
        var p1 = BuildProyectoCompleto();

        // Verificamos que el engine corrió antes de guardar.
        Assert.NotNull(p1.Sistemas[0].Losas[0].HCalc);
        Assert.NotNull(p1.Sistemas[0].Losas[0].Qu);

        ProyectoSerializer.Save(p1, path);
        var p2 = ProyectoSerializer.Load(path);

        var l1_before = p1.Sistemas[0].Losas[0];
        var l1_after  = p2.Sistemas[0].Losas[0];

        Assert.Equal(l1_before.HCalc, l1_after.HCalc);
        Assert.Equal(l1_before.HEq,   l1_after.HEq);
        Assert.Equal(l1_before.Qmamp, l1_after.Qmamp);
        Assert.Equal(l1_before.Qmap,  l1_after.Qmap);
        Assert.Equal(l1_before.Qd,    l1_after.Qd);
        Assert.Equal(l1_before.Ql,    l1_after.Ql);
        Assert.Equal(l1_before.Qu,    l1_after.Qu);
    }

    [Fact]
    public void RoundTrip_preserva_cargas_globales()
    {
        var path = TempFile();
        var p1 = BuildProyectoCompleto();

        ProyectoSerializer.Save(p1, path);
        var p2 = ProyectoSerializer.Load(path);

        Assert.Equal(p1.Cargas.PesosPropiosEntrepiso.Items.Count,
                     p2.Cargas.PesosPropiosEntrepiso.Items.Count);
        Assert.Equal(p1.Cargas.PesosPropiosEntrepiso.Total,
                     p2.Cargas.PesosPropiosEntrepiso.Total, precision: 3);

        Assert.Equal(15, p2.Cargas.CargaMuertaPorEspesor.Filas.Count);
        Assert.Equal(p1.Cargas.Factores.FactorD, p2.Cargas.Factores.FactorD);
        Assert.Equal(p1.Cargas.Factores.FactorL, p2.Cargas.Factores.FactorL);
    }

    [Fact]
    public void Load_sella_el_path_en_Proyecto_Archivo()
    {
        var path = TempFile();
        var p1 = BuildProyectoCompleto();

        ProyectoSerializer.Save(p1, path);
        var p2 = ProyectoSerializer.Load(path);

        Assert.Equal(path, p2.Archivo);
    }

    // =================================================================
    // Manejo de errores
    // =================================================================

    [Fact]
    public void Save_lanza_si_proyecto_es_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ProyectoSerializer.Save(null!, TempFile()));
    }

    [Fact]
    public void Load_lanza_si_archivo_no_existe()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ProyectoSerializer.Load("no-existe.lpx.json"));
    }

    [Fact]
    public void Load_lanza_InvalidProyectoFileException_si_json_es_invalido()
    {
        var path = TempFile();
        File.WriteAllText(path, "{ esto no es json valido");
        Assert.Throws<InvalidProyectoFileException>(() => ProyectoSerializer.Load(path));
    }

    [Fact]
    public void Load_lanza_InvalidProyectoFileException_si_version_es_superior()
    {
        var path = TempFile();
        File.WriteAllText(path,
            $"{{ \"version\": 999, \"creadoUtc\": \"2025-01-01\", \"modificadoUtc\": \"2025-01-01\", \"appVersion\": \"test\", \"proyecto\": {{ \"nombre\": \"X\" }} }}");
        var ex = Assert.Throws<InvalidProyectoFileException>(() => ProyectoSerializer.Load(path));
        Assert.Contains("superior", ex.Message);
    }

    // =================================================================
    // Metadata read sin carga completa
    // =================================================================

    [Fact]
    public void ReadMetadata_devuelve_nombre_y_count_de_sistemas_sin_cargar_todo()
    {
        var path = TempFile();
        var p1 = BuildProyectoCompleto();
        ProyectoSerializer.Save(p1, path);

        var meta = ProyectoSerializer.ReadMetadata(path);

        Assert.Equal("Torre Test",         meta.NombreProyecto);
        Assert.Equal("Ing. Demo",          meta.Ingeniero);
        Assert.Equal(2,                    meta.CantidadNiveles);   // 2 sistemas
        Assert.Equal(ProyectoSerializer.FormatVersion, meta.Version);
        Assert.Equal(path,                 meta.Path);
    }

    // =================================================================
    // Registry
    // =================================================================

    [Fact]
    public void Registry_vacio_si_no_existe_archivo()
    {
        var entries = ProyectoRegistry.Load();
        Assert.Empty(entries);
    }

    [Fact]
    public void Registry_AddOrUpdate_agrega_entrada_nueva()
    {
        var path = TempFile();
        var p = BuildProyectoCompleto();
        ProyectoSerializer.Save(p, path);

        ProyectoRegistry.AddOrUpdate(path, p.Nombre, p.Autor, p.Codia, p.Sistemas.Count);

        var entries = ProyectoRegistry.Load();
        Assert.Single(entries);
        Assert.Equal("Torre Test", entries[0].NombreProyecto);
        Assert.Equal("Ing. Demo",  entries[0].Ingeniero);
        Assert.Equal(2,            entries[0].CantidadNiveles);
    }

    [Fact]
    public void Registry_AddOrUpdate_actualiza_metadata_si_path_ya_existe()
    {
        var path = TempFile();
        var p = BuildProyectoCompleto();
        ProyectoSerializer.Save(p, path);

        ProyectoRegistry.AddOrUpdate(path, "Nombre Viejo", "Ing. Viejo", "00000", 1);
        ProyectoRegistry.AddOrUpdate(path, "Nombre Nuevo", "Ing. Nuevo", "11111", 5);

        var entries = ProyectoRegistry.Load();
        Assert.Single(entries);                          // mismo path, no duplica
        Assert.Equal("Nombre Nuevo", entries[0].NombreProyecto);
        Assert.Equal("Ing. Nuevo",   entries[0].Ingeniero);
        Assert.Equal(5,              entries[0].CantidadNiveles);
    }

    [Fact]
    public void Registry_AddOrUpdate_orden_descendente_por_acceso()
    {
        var path1 = TempFile();
        var path2 = TempFile();
        File.WriteAllText(path1, "{\"version\":1, \"proyecto\":{\"nombre\":\"P1\"}}");
        File.WriteAllText(path2, "{\"version\":1, \"proyecto\":{\"nombre\":\"P2\"}}");

        ProyectoRegistry.AddOrUpdate(path1, "P1", "I1", "", 1);
        System.Threading.Thread.Sleep(10);
        ProyectoRegistry.AddOrUpdate(path2, "P2", "I2", "", 1);

        var entries = ProyectoRegistry.Load();
        Assert.Equal(2,    entries.Count);
        Assert.Equal("P2", entries[0].NombreProyecto);   // más reciente primero
        Assert.Equal("P1", entries[1].NombreProyecto);
    }

    [Fact]
    public void Registry_filtra_entradas_cuyo_archivo_ya_no_existe()
    {
        var path = TempFile();
        File.WriteAllText(path, "{\"version\":1, \"proyecto\":{\"nombre\":\"P\"}}");

        ProyectoRegistry.AddOrUpdate(path, "P", "I", "", 1);

        // Borrar el archivo del proyecto.
        File.Delete(path);

        var entries = ProyectoRegistry.Load();
        Assert.Empty(entries);
    }

    [Fact]
    public void Registry_Remove_elimina_la_entrada()
    {
        var path = TempFile();
        File.WriteAllText(path, "{\"version\":1, \"proyecto\":{\"nombre\":\"P\"}}");
        ProyectoRegistry.AddOrUpdate(path, "P", "I", "", 1);
        Assert.Single(ProyectoRegistry.Load());

        ProyectoRegistry.Remove(path);

        Assert.Empty(ProyectoRegistry.Load());
    }

    [Fact]
    public void Registry_Clear_borra_todo()
    {
        var path1 = TempFile();
        var path2 = TempFile();
        File.WriteAllText(path1, "{\"version\":1, \"proyecto\":{\"nombre\":\"P1\"}}");
        File.WriteAllText(path2, "{\"version\":1, \"proyecto\":{\"nombre\":\"P2\"}}");
        ProyectoRegistry.AddOrUpdate(path1, "P1", "I", "", 1);
        ProyectoRegistry.AddOrUpdate(path2, "P2", "I", "", 1);

        ProyectoRegistry.Clear();

        Assert.Empty(ProyectoRegistry.Load());
    }
}
