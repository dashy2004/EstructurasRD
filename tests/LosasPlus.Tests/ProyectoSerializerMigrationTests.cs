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
/// Tests de la migración del formato <c>.lpx.json</c>: v1 → v2 (jerarquía
/// <see cref="Edificio"/> → <see cref="Nivel"/>) y v2 → v3 (casos y
/// combinaciones de carga, con «salvavidas retroactivo»).
///
/// <para>
/// Los fixtures v1 y v2 se generan en el propio test: se construye un proyecto,
/// se serializa al formato actual y se transforma el JSON crudo a la forma
/// heredada. Así quedan garantizado-coherentes sin commitear archivos a mano.
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
    /// <c>edificios</c> ni <c>combinaciones</c>) a partir de un proyecto.
    /// </summary>
    private static string BuildV1Json(Proyecto p)
    {
        var root = JsonNode.Parse(ProyectoSerializer.ToJson(p))!.AsObject();
        root["version"] = 1;

        var proyecto = root["proyecto"]!.AsObject();
        var sistemas = proyecto["edificios"]![0]!["niveles"]![0]!["sistemas"]!.DeepClone();
        proyecto.Remove("edificios");
        proyecto.Remove("combinaciones");
        proyecto["sistemas"] = sistemas;

        return root.ToJsonString();
    }

    /// <summary>
    /// Construye un JSON con el formato v2 (con <c>edificios</c>, sin
    /// <c>combinaciones</c>) a partir de un proyecto.
    /// </summary>
    private static string BuildV2Json(Proyecto p)
    {
        var root = JsonNode.Parse(ProyectoSerializer.ToJson(p))!.AsObject();
        root["version"] = 2;
        root["proyecto"]!.AsObject().Remove("combinaciones");
        return root.ToJsonString();
    }

    // =====================================================================
    // Migración v1 → v2 (jerarquía Edificio → Nivel)
    // =====================================================================

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
    public void Migracion_v1_a_formato_actual_es_roundtrip_estable()
    {
        // v1 en disco → Load (migra) → Save (escribe el formato actual).
        var v1Path = TempFile();
        File.WriteAllText(v1Path, BuildV1Json(BuildProyecto()));
        var migrado = ProyectoSerializer.Load(v1Path);

        var actualPath = TempFile();
        ProyectoSerializer.Save(migrado, actualPath);

        var root = JsonNode.Parse(File.ReadAllText(actualPath))!.AsObject();
        Assert.Equal(ProyectoSerializer.FormatVersion, (int)root["version"]!);

        var proyecto = root["proyecto"]!.AsObject();
        Assert.NotNull(proyecto["edificios"]);
        Assert.Null(proyecto["sistemas"]);   // ya no hay sistemas planos

        // Recargar y comparar.
        var recargado = ProyectoSerializer.Load(actualPath);
        Assert.Equal(2, recargado.Sistemas.Count);
        Assert.Equal("E1", recargado.Sistemas[0].Nombre);
    }

    [Fact]
    public void Load_del_formato_actual_no_necesita_migracion()
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
        // Usar `FormatVersion + 1` para que el test sobreviva bumps
        // futuros del esquema (v3→v4 en Módulo 2 Parte A Fase 3D-II:
        // el literal "4" pasó de ser versión futura a versión vigente).
        var path = TempFile();
        int futura = ProyectoSerializer.FormatVersion + 1;
        File.WriteAllText(path, $"{{ \"version\": {futura}, \"proyecto\": {{ \"nombre\": \"X\" }} }}");

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
    public void ReadMetadata_cuenta_sistemas_igual_en_v1_y_formato_actual()
    {
        var v1Path = TempFile();
        File.WriteAllText(v1Path, BuildV1Json(BuildProyecto()));

        var actualPath = TempFile();
        ProyectoSerializer.Save(BuildProyecto(), actualPath);

        var metaV1 = ProyectoSerializer.ReadMetadata(v1Path);
        var metaActual = ProyectoSerializer.ReadMetadata(actualPath);

        Assert.Equal(2, metaV1.CantidadNiveles);
        Assert.Equal(2, metaActual.CantidadNiveles);
    }

    // =====================================================================
    // Migración v2 → v3 (casos y combinaciones — salvavidas retroactivo)
    // =====================================================================

    [Fact]
    public void Load_de_v2_inyecta_la_semilla_de_combinaciones()
    {
        // Un archivo v2 no tiene casos ni combinaciones — el salvavidas
        // retroactivo le inyecta la semilla por defecto al cargar.
        var path = TempFile();
        File.WriteAllText(path, BuildV2Json(BuildProyecto()));

        var p = ProyectoSerializer.Load(path);

        Assert.Equal(4, p.Combinaciones.Casos.Count);
        Assert.Equal(8, p.Combinaciones.Combinaciones.Count);
    }

    [Fact]
    public void Load_de_v1_tambien_inyecta_la_semilla_de_combinaciones()
    {
        var path = TempFile();
        File.WriteAllText(path, BuildV1Json(BuildProyecto()));

        var p = ProyectoSerializer.Load(path);

        Assert.Equal(4, p.Combinaciones.Casos.Count);
        Assert.Equal(8, p.Combinaciones.Combinaciones.Count);
    }

    [Fact]
    public void Load_de_v3_conserva_sus_propias_combinaciones()
    {
        // Un archivo v3 ya trae sus combinaciones — el salvavidas NO debe
        // re-sembrarlo (la inyección está condicionada a versión < 3).
        var p = BuildProyecto();
        p.Combinaciones.Combinaciones.Clear();
        p.Combinaciones.Casos.Clear();

        var path = TempFile();
        ProyectoSerializer.Save(p, path);          // v3 con combinaciones vacías

        var recargado = ProyectoSerializer.Load(path);

        Assert.Empty(recargado.Combinaciones.Combinaciones);
        Assert.Empty(recargado.Combinaciones.Casos);
    }

    [Fact]
    public void RoundTrip_v3_preserva_las_combinaciones_y_sus_factores()
    {
        var path = TempFile();
        ProyectoSerializer.Save(BuildProyecto(), path);

        var p = ProyectoSerializer.Load(path);
        var combo = p.Combinaciones.Combinaciones[1];   // "1.2D + 1.6L + 0.5Lr"

        Assert.Equal("1.2D + 1.6L + 0.5Lr", combo.Nombre);
        Assert.Equal(3, combo.Terminos.Count);
        Assert.Equal(1.6, combo.Terminos[1].Factor);
    }
}
