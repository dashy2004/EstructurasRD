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

    /// <summary>
    /// Construye un JSON con el formato v3: jerarquía Edificio→Nivel y
    /// combinaciones presentes, pero con <c>Uso</c>/<c>Cota</c> SÓLO en el sistema
    /// (los niveles aún no los traen — estado real de un archivo pre-B3). Borra
    /// cualquier <c>uso</c>/<c>cota</c> de los niveles para simular el formato
    /// antiguo, dejando un primer sistema con <c>cotaMetros</c>/<c>uso</c>.
    /// </summary>
    private static string BuildV3Json(Proyecto p)
    {
        var root = JsonNode.Parse(ProyectoSerializer.ToJson(p))!.AsObject();
        root["version"] = 3;

        var edificios = root["proyecto"]!["edificios"]!.AsArray();
        foreach (var ed in edificios)
        foreach (var niv in ed!["niveles"]!.AsArray())
        {
            var n = niv!.AsObject();
            n.Remove("uso");          // un v3 no tenía uso/cota en el nivel…
            n["cota"] = 0;            // …la cota del nivel quedaba en 0 (sin reconciliar)
        }
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
        var path = TempFile();
        // Una versión por encima de la soportada (FormatVersion = 5 → usar 6).
        File.WriteAllText(path, "{ \"version\": 6, \"proyecto\": { \"nombre\": \"X\" } }");

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
    public void ReadMetadata_cuenta_niveles_no_sistemas_en_v1_y_formato_actual()
    {
        // El fixture tiene 2 SISTEMAS bajo un único NIVEL: CantidadNiveles debe
        // ser 1, no 2 (B3 — el metadato cuenta plantas, no sistemas). Un archivo
        // v1 plano representa también una única planta implícita.
        var v1Path = TempFile();
        File.WriteAllText(v1Path, BuildV1Json(BuildProyecto()));

        var actualPath = TempFile();
        ProyectoSerializer.Save(BuildProyecto(), actualPath);

        var metaV1 = ProyectoSerializer.ReadMetadata(v1Path);
        var metaActual = ProyectoSerializer.ReadMetadata(actualPath);

        Assert.Equal(1, metaV1.CantidadNiveles);
        Assert.Equal(1, metaActual.CantidadNiveles);
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

    // =====================================================================
    // Migración v3 → v4 (B3): Uso/Cota pasan del Sistema al Nivel
    // =====================================================================

    [Fact]
    public void Load_de_v3_copia_Uso_y_Cota_del_primer_sistema_al_Nivel()
    {
        // El fixture tiene un sistema E1 con Uso=Entrepiso, CotaMetros=2.80.
        // Tras migrar v3→v4, esos valores deben aparecer en el propio Nivel.
        var path = TempFile();
        File.WriteAllText(path, BuildV3Json(BuildProyecto()));

        var p = ProyectoSerializer.Load(path);

        var nivel = p.Edificios[0].Niveles[0];
        Assert.Equal(SistemaUso.Entrepiso, nivel.Uso);
        Assert.Equal(2.80, nivel.CotaMetros, precision: 2);
        Assert.Equal(2.80, nivel.Cota,       precision: 2);

        // El sistema conserva su copia (obsoleta) para no romper lectores previos.
        Assert.Equal(2.80, p.Sistemas[0].CotaMetros, precision: 2);
    }

    [Fact]
    public void Migracion_v3_a_v4_roundtrip_estable_con_Uso_y_Cota_en_el_Nivel()
    {
        // v3 en disco → Load (migra a v4, escribe Uso/Cota en el Nivel) → Save (v4)
        // → recargar: el Nivel sigue trayendo Uso/Cota directamente (sin re-migrar).
        var v3Path = TempFile();
        File.WriteAllText(v3Path, BuildV3Json(BuildProyecto()));
        var migrado = ProyectoSerializer.Load(v3Path);

        var v4Path = TempFile();
        ProyectoSerializer.Save(migrado, v4Path);

        var root = JsonNode.Parse(File.ReadAllText(v4Path))!.AsObject();
        Assert.Equal(ProyectoSerializer.FormatVersion, (int)root["version"]!);   // formato vigente al re-guardar

        var recargado = ProyectoSerializer.Load(v4Path);
        var nivel = recargado.Edificios[0].Niveles[0];
        Assert.Equal(SistemaUso.Entrepiso, nivel.Uso);
        Assert.Equal(2.80, nivel.CotaMetros, precision: 2);
    }

    // =====================================================================
    // Migración v4 → v5 (UI1.1): posX/posY → coordenadaX/Y + anclada
    // =====================================================================

    /// <summary>
    /// Proyecto con un sistema "S-UI11" (las losas dadas) bajo el primer nivel —
    /// el blanco de los asserts de migración v4→v5.
    /// </summary>
    private static Proyecto BuildProyectoUi11(params Losa[] losas)
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Nombre = "Proyecto UI11";
        p.AsegurarEstructura();
        var s = new Sistema { Nombre = "S-UI11" };
        foreach (var l in losas) s.Losas.Add(l);
        p.Edificios[0].Niveles[0].Sistemas.Add(s);
        return p;
    }

    /// <summary>Todas las losas (como <see cref="JsonObject"/>) de todos los sistemas.</summary>
    private static IEnumerable<JsonObject> LosasJson(JsonObject root)
    {
        foreach (var ed in root["proyecto"]!["edificios"]!.AsArray())
        foreach (var niv in ed!["niveles"]!.AsArray())
        {
            if (niv!["sistemas"] is not JsonArray sistemas) continue;
            foreach (var sis in sistemas)
                if (sis!["losas"] is JsonArray losas)
                    foreach (var l in losas)
                        yield return l!.AsObject();
        }
    }

    /// <summary>El array JSON de losas del sistema "S-UI11".</summary>
    private static JsonArray LosasJsonUi11(JsonObject root)
    {
        foreach (var ed in root["proyecto"]!["edificios"]!.AsArray())
        foreach (var niv in ed!["niveles"]!.AsArray())
            if (niv!["sistemas"] is JsonArray sistemas)
                foreach (var sis in sistemas)
                    if (sis!["nombre"]?.GetValue<string>() == "S-UI11")
                        return sis["losas"]!.AsArray();
        throw new InvalidOperationException("fixture sin sistema S-UI11");
    }

    /// <summary>
    /// Degrada el formato actual a un v4 auténtico: <c>version = 4</c> y sin el
    /// campo <c>anclada</c> (no existía en v4). <paramref name="mutar"/> permite
    /// inyectar <c>posX/posY</c> legados en las losas de "S-UI11".
    /// </summary>
    private static string BuildV4Json(Proyecto p, Action<JsonArray>? mutar = null)
    {
        var root = JsonNode.Parse(ProyectoSerializer.ToJson(p))!.AsObject();
        root["version"] = 4;
        foreach (var l in LosasJson(root)) l.Remove("anclada");
        mutar?.Invoke(LosasJsonUi11(root));
        return root.ToJsonString();
    }

    private static Sistema SistemaUi11(Proyecto p)
    {
        foreach (var s in p.Edificios[0].Niveles[0].Sistemas)
            if (s.Nombre == "S-UI11") return s;
        throw new InvalidOperationException("proyecto cargado sin sistema S-UI11");
    }

    [Fact]
    public void Load_v4_planta_virgen_migra_el_ancla_CAD_a_coordenadas()
    {
        // Proyecto v4 "solo CAD": coordenadas de planta vírgenes (0,0) y una losa
        // anclada en el lienzo CAD legado (posX/posY). El ancla era la única
        // verdad ⇒ pasa a coordenadaX/Y + anclada.
        var path = TempFile();
        File.WriteAllText(path, BuildV4Json(
            BuildProyectoUi11(new Losa { Id = 1, Lx = 5, Ly = 4 },
                              new Losa { Id = 2, Lx = 3, Ly = 3 }),
            losas => { losas[0]!["posX"] = 5.0; losas[0]!["posY"] = 2.0; }));

        var s = SistemaUi11(ProyectoSerializer.Load(path));

        Assert.Equal(5.0, s.Losas[0].CoordenadaX, 9);
        Assert.Equal(2.0, s.Losas[0].CoordenadaY, 9);
        Assert.True(s.Losas[0].Anclada);
        Assert.False(s.Losas[1].Anclada);              // sin ancla y planta virgen ⇒ libre
        Assert.Equal(0.0, s.Losas[1].CoordenadaX, 9);
    }

    [Fact]
    public void Load_v4_planta_arreglada_gana_sobre_el_ancla_CAD_divergente()
    {
        // La planta tiene datos (el usuario arregló losas ahí) y el CAD trae una
        // copia divergente/stale ⇒ política UI1.1: PLANTA GANA y todo queda anclado.
        var path = TempFile();
        File.WriteAllText(path, BuildV4Json(
            BuildProyectoUi11(new Losa { Id = 1, CoordenadaX = 7, CoordenadaY = 1 },
                              new Losa { Id = 2, CoordenadaX = 3, CoordenadaY = 4 }),
            losas => { losas[0]!["posX"] = 5.0; losas[0]!["posY"] = 2.0; }));

        var s = SistemaUi11(ProyectoSerializer.Load(path));

        Assert.Equal(7.0, s.Losas[0].CoordenadaX, 9);
        Assert.Equal(1.0, s.Losas[0].CoordenadaY, 9);
        Assert.True(s.Losas[0].Anclada);
        Assert.Equal(3.0, s.Losas[1].CoordenadaX, 9);
        Assert.Equal(4.0, s.Losas[1].CoordenadaY, 9);
        Assert.True(s.Losas[1].Anclada);
    }

    [Fact]
    public void Load_v4_virgen_sin_anclas_queda_libre()
    {
        var path = TempFile();
        File.WriteAllText(path, BuildV4Json(
            BuildProyectoUi11(new Losa { Id = 1 }, new Losa { Id = 2 })));

        var s = SistemaUi11(ProyectoSerializer.Load(path));

        Assert.All(s.Losas, l => Assert.False(l.Anclada));
        Assert.All(s.Losas, l => Assert.Equal(0.0, l.CoordenadaX, 9));
    }

    [Fact]
    public void Load_v4_migra_aunque_posX_venga_antes_que_coordenadaX()
    {
        // La migración opera sobre el JSON crudo ⇒ debe ser inmune al orden de
        // propiedades (el footgun clásico de los adaptadores por setter).
        var path = TempFile();
        File.WriteAllText(path, BuildV4Json(
            BuildProyectoUi11(new Losa { Id = 1, Lx = 5, Ly = 4 }),
            losas =>
            {
                var original = losas[0]!.AsObject();
                var invertida = new JsonObject { ["posX"] = 9.0, ["posY"] = 3.0 };
                foreach (var kv in System.Linq.Enumerable.ToList(original))
                    if (kv.Key is not ("posX" or "posY"))
                        invertida[kv.Key] = kv.Value?.DeepClone();
                losas[0] = invertida;   // posX/posY ANTES que coordenadaX/Y
            }));

        var s = SistemaUi11(ProyectoSerializer.Load(path));

        Assert.Equal(9.0, s.Losas[0].CoordenadaX, 9);
        Assert.Equal(3.0, s.Losas[0].CoordenadaY, 9);
        Assert.True(s.Losas[0].Anclada);
    }

    [Fact]
    public void Load_v4_y_Save_estampan_v5_sin_posX_posY()
    {
        // El bump de formato es parte del contrato de UI1.1.
        Assert.Equal(5, ProyectoSerializer.FormatVersion);

        var v4Path = TempFile();
        File.WriteAllText(v4Path, BuildV4Json(
            BuildProyectoUi11(new Losa { Id = 1, Lx = 5, Ly = 4 }),
            losas => { losas[0]!["posX"] = 5.0; losas[0]!["posY"] = 2.0; }));
        var migrado = ProyectoSerializer.Load(v4Path);

        var v5Path = TempFile();
        ProyectoSerializer.Save(migrado, v5Path);

        var root = JsonNode.Parse(File.ReadAllText(v5Path))!.AsObject();
        Assert.Equal(ProyectoSerializer.FormatVersion, (int)root["version"]!);
        foreach (var losa in LosasJson(root))
        {
            Assert.False(losa.ContainsKey("posX"), "un v5 no debe re-escribir posX");
            Assert.False(losa.ContainsKey("posY"), "un v5 no debe re-escribir posY");
        }

        var recargado = SistemaUi11(ProyectoSerializer.Load(v5Path));
        Assert.Equal(5.0, recargado.Losas[0].CoordenadaX, 9);
        Assert.True(recargado.Losas[0].Anclada);
    }

    [Fact]
    public void Load_v1_con_ancla_CAD_encadena_hasta_v5()
    {
        // Cadena completa v1 → v2 → (semilla v3) → v4 → v5 sobre un archivo
        // ancestral con posX/posY en el primer sistema plano.
        var root = JsonNode.Parse(BuildV1Json(BuildProyecto()))!.AsObject();
        foreach (var sis in root["proyecto"]!["sistemas"]!.AsArray())
            if (sis!["losas"] is JsonArray losas)
                foreach (var l in losas)
                    l!.AsObject().Remove("anclada");   // un v1 auténtico no la traía
        var primera = root["proyecto"]!["sistemas"]![0]!["losas"]![0]!.AsObject();
        primera["posX"] = 9.0;
        primera["posY"] = 3.0;

        var p = ProyectoSerializer.FromJson(root.ToJsonString());

        var losa = p.Sistemas[0].Losas[0];
        Assert.Equal(9.0, losa.CoordenadaX, 9);
        Assert.Equal(3.0, losa.CoordenadaY, 9);
        Assert.True(losa.Anclada);
    }

    /// <summary>
    /// Construye un proyecto con DOS niveles, cada uno con DOS sistemas, para
    /// verificar que <c>CantidadNiveles</c> cuenta NIVELES (=2), no sistemas (=4).
    /// </summary>
    private static Proyecto BuildProyectoDosNivelesDosSistemas()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Nombre = "Torre 2x2";
        p.AsegurarEstructura();
        var ed = p.Edificios[0];

        var nivel1 = ed.Niveles[0];
        nivel1.Nombre = "Planta Baja";
        nivel1.Sistemas.Add(new Sistema { Nombre = "PB-A" });
        nivel1.Sistemas.Add(new Sistema { Nombre = "PB-B" });

        var nivel2 = new Nivel { Nombre = "Nivel 2", Cota = 3.0 };
        nivel2.Sistemas.Add(new Sistema { Nombre = "N2-A" });
        nivel2.Sistemas.Add(new Sistema { Nombre = "N2-B" });
        ed.Niveles.Add(nivel2);

        return p;
    }

    [Fact]
    public void ReadMetadata_CantidadNiveles_cuenta_niveles_no_sistemas()
    {
        // 2 niveles × 2 sistemas = 4 sistemas, pero CantidadNiveles debe ser 2.
        var path = TempFile();
        ProyectoSerializer.Save(BuildProyectoDosNivelesDosSistemas(), path);

        var meta = ProyectoSerializer.ReadMetadata(path);

        Assert.Equal(2, meta.CantidadNiveles);
    }
}
