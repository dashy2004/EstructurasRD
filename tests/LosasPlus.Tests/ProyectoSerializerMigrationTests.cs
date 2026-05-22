using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using LosasPlus.Calculo;
using LosasPlus.Models;
using LosasPlus.Persistence;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de la migración del formato <c>.lpx.json</c> v1 → v2 (jerarquía
/// <see cref="Edificio"/> → <see cref="Nivel"/>).
///
/// <para>
/// El fixture v1 se genera en el propio test: se construye un proyecto, se
/// serializa a v2 y se transforma el JSON crudo a la forma plana v1. Así el
/// v1 queda garantizado-coherente sin commitear un archivo escrito a mano.
/// </para>
/// </summary>
public class ProyectoSerializerMigrationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { if (File.Exists(f)) File.Delete(f); } catch { }
    }

    private string TempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"migracion_test_{Guid.NewGuid():N}.lpx.json");
        _tempFiles.Add(path);
        return path;
    }

    private static Proyecto BuildProyecto()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Nombre = "Proyecto Migración";
        p.Autor  = "Ing. Test";
        p.Codia  = "98765";

        var e1 = new Sistema { Nombre = "E1", Uso = SistemaUso.Entrepiso, CotaMetros = 2.80 };
        e1.Losas.Add(new Losa { Id = 1, Lx = 5.00, Ly = 4.00 });
        e1.Losas.Add(new Losa { Id = 2, Lx = 3.50, Ly = 3.50 });
        p.Sistemas.Add(e1);

        var techo = new Sistema { Nombre = "Techo", Uso = SistemaUso.Techo, CotaMetros = 5.60 };
        techo.Losas.Add(new Losa { Id = 1, Lx = 4.00, Ly = 4.00 });
        p.Sistemas.Add(techo);

        CalculoEngine.RecalcularProyecto(p);
        return p;
    }

    /// <summary>
    /// Construye un JSON con el formato plano v1 (<c>proyecto.sistemas</c>, sin
    /// <c>edificios</c>) a partir de un proyecto, transformando su JSON v2.
    /// </summary>
    private static string BuildV1Json(Proyecto p)
    {
        var root = JsonNode.Parse(ProyectoSerializer.ToJson(p))!.AsObject();
        root["version"] = 1;

        var proyecto = root["proyecto"]!.AsObject();
        var sistemas = proyecto["edificios"]![0]!["niveles"]![0]!["sistemas"]!.DeepClone();
        proyecto.Remove("edificios");
        proyecto["sistemas"] = sistemas;

        return root.ToJsonString();
    }

    [Fact]
    public void Load_de_v1_envuelve_los_sistemas_en_un_Edificio_y_un_Nivel()
    {
        var path = TempFile();
        File.WriteAllText(path, BuildV1Json(BuildProyecto()));

        var p = ProyectoSerializer.Load(path);

        Assert.Single(p.Edificios);
        Assert.Single(p.Edificios[0].Niveles);
        Assert.Equal(2, p.Sistemas.Count);
        Assert.Equal("E1",    p.Sistemas[0].Nombre);
        Assert.Equal("Techo", p.Sistemas[1].Nombre);
        Assert.Equal(2, p.Sistemas[0].Losas.Count);
    }

    [Fact]
    public void Load_de_v1_preserva_metadata_y_outputs()
    {
        var path = TempFile();
        File.WriteAllText(path, BuildV1Json(BuildProyecto()));

        var p = ProyectoSerializer.Load(path);

        Assert.Equal("Proyecto Migración", p.Nombre);
        Assert.Equal("Ing. Test",          p.Autor);
        Assert.Equal("98765",              p.Codia);
        // Los outputs computados del engine sobreviven la migración.
        Assert.NotNull(p.Sistemas[0].Losas[0].HCalc);
        Assert.NotNull(p.Sistemas[0].Losas[0].Qu);
    }

    [Fact]
    public void Migracion_v1_v2_es_roundtrip_estable()
    {
        // v1 en disco → Load (migra) → Save (escribe v2) → el archivo es v2.
        var v1Path = TempFile();
        File.WriteAllText(v1Path, BuildV1Json(BuildProyecto()));
        var migrado = ProyectoSerializer.Load(v1Path);

        var v2Path = TempFile();
        ProyectoSerializer.Save(migrado, v2Path);

        var root = JsonNode.Parse(File.ReadAllText(v2Path))!.AsObject();
        Assert.Equal(2, (int)root["version"]!);

        var proyecto = root["proyecto"]!.AsObject();
        Assert.NotNull(proyecto["edificios"]);
        Assert.Null(proyecto["sistemas"]);   // ya no hay sistemas planos

        // Recargar el v2 y comparar.
        var recargado = ProyectoSerializer.Load(v2Path);
        Assert.Equal(2, recargado.Sistemas.Count);
        Assert.Equal("E1", recargado.Sistemas[0].Nombre);
    }

    [Fact]
    public void Load_de_v2_directo_no_necesita_migracion()
    {
        var path = TempFile();
        ProyectoSerializer.Save(BuildProyecto(), path);

        var p = ProyectoSerializer.Load(path);

        Assert.Single(p.Edificios);
        Assert.Equal(2, p.Sistemas.Count);
    }

    [Fact]
    public void Load_de_version_futura_lanza_excepcion()
    {
        var path = TempFile();
        File.WriteAllText(path, "{ \"version\": 3, \"proyecto\": { \"nombre\": \"X\" } }");

        Assert.Throws<InvalidProyectoFileException>(() => ProyectoSerializer.Load(path));
    }

    [Fact]
    public void FromJson_tambien_migra_v1()
    {
        var v1 = BuildV1Json(BuildProyecto());

        var p = ProyectoSerializer.FromJson(v1);

        Assert.Single(p.Edificios);
        Assert.Equal(2, p.Sistemas.Count);
    }

    [Fact]
    public void ReadMetadata_cuenta_sistemas_igual_en_v1_y_v2()
    {
        var v1Path = TempFile();
        File.WriteAllText(v1Path, BuildV1Json(BuildProyecto()));

        var v2Path = TempFile();
        ProyectoSerializer.Save(BuildProyecto(), v2Path);

        var metaV1 = ProyectoSerializer.ReadMetadata(v1Path);
        var metaV2 = ProyectoSerializer.ReadMetadata(v2Path);

        Assert.Equal(2, metaV1.CantidadNiveles);
        Assert.Equal(2, metaV2.CantidadNiveles);
    }
}
