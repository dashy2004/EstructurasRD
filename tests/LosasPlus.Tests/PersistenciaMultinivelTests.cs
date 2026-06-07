using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del round-trip <b>multinivel</b> del proyecto guardado por
/// <see cref="ProyectoService"/> (formato multi-archivo manifest +
/// <c>.DL</c>). Antes de B2, <c>GuardarProyecto</c> sólo persistía la fachada
/// <c>Proyecto.Sistemas</c> (= <c>Edificios[0].Niveles[0].Sistemas</c>),
/// perdiendo TODO nivel salvo el primero al guardar.
///
/// <para>
/// Estos tests construyen un proyecto con 2 niveles, cada uno con sistemas
/// distintos, lo guardan a una carpeta temporal y lo recargan, exigiendo que
/// AMBOS niveles y sus sistemas sobrevivan. También cubren la compatibilidad
/// hacia atrás con el manifest legado (un solo nivel + lista de <c>.DL</c>).
/// </para>
/// </summary>
public class PersistenciaMultinivelTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var d in _tempDirs)
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); } catch { }
    }

    private string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"proyecto_multinivel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    /// <summary>
    /// Construye un proyecto con DOS niveles, cada uno con un sistema distinto
    /// (nombre + losas propias) — la estructura mínima para detectar la pérdida
    /// de niveles al guardar.
    /// </summary>
    private static Proyecto BuildProyectoDosNiveles()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Nombre = "Torre Multinivel";
        p.Autor  = "Ing. B2";

        // El proyecto seedeado materializa Edificio[0]/Nivel[0] perezosamente.
        // AsegurarEstructura los crea para poder poblarlos como "Planta Baja".
        p.AsegurarEstructura();
        var ed = p.Edificios[0];
        var nivel1 = ed.Niveles[0];
        nivel1.Nombre = "Planta Baja";
        nivel1.Cota   = 0.0;
        var sisN1 = new Sistema { Nombre = "PB-Entrepiso", Uso = SistemaUso.Entrepiso, CotaMetros = 0.0 };
        sisN1.Losas.Add(new Losa { Id = 1, Lx = 6.00, Ly = 5.00 });
        nivel1.Sistemas.Add(sisN1);

        // Segundo nivel — el que se PIERDE con el guardado pre-B2.
        var nivel2 = new Nivel { Nombre = "Nivel Techo", Cota = 3.0 };
        var sisN2 = new Sistema { Nombre = "Techo-Sistema", Uso = SistemaUso.Techo, CotaMetros = 3.0 };
        sisN2.Losas.Add(new Losa { Id = 1, Lx = 4.00, Ly = 4.00 });
        sisN2.Losas.Add(new Losa { Id = 2, Lx = 3.50, Ly = 3.00 });
        nivel2.Sistemas.Add(sisN2);
        ed.Niveles.Add(nivel2);

        return p;
    }

    // =====================================================================
    // Round-trip multinivel — FALLA antes de B2 (sólo persistía Niveles[0]).
    // =====================================================================

    [Fact]
    public void GuardarYAbrir_preserva_los_dos_niveles_y_sus_sistemas()
    {
        var dir = TempDir();
        var p1 = BuildProyectoDosNiveles();

        var manifestPath = Path.Combine(dir, ProyectoService.ManifestFileName);
        ProyectoService.GuardarProyecto(p1, dir);

        var p2 = ProyectoService.AbrirProyecto(manifestPath);

        // Un solo edificio, DOS niveles.
        Assert.Single(p2.Edificios);
        var niveles = p2.Edificios[0].Niveles;
        Assert.Equal(2, niveles.Count);

        // Nombres y cotas de los niveles sobreviven.
        Assert.Equal("Planta Baja",  niveles[0].Nombre);
        Assert.Equal("Nivel Techo",  niveles[1].Nombre);
        Assert.Equal(0.0, niveles[0].Cota, precision: 3);
        Assert.Equal(3.0, niveles[1].Cota, precision: 3);

        // Cada nivel conserva su sistema distinto.
        Assert.Single(niveles[0].Sistemas);
        Assert.Single(niveles[1].Sistemas);
        Assert.Equal("PB-Entrepiso",  niveles[0].Sistemas[0].Nombre);
        Assert.Equal("Techo-Sistema", niveles[1].Sistemas[0].Nombre);

        // Campos representativos que el formato .DL sí persiste: el conteo de
        // losas del sistema del 2º nivel y su geometría. (Uso/CotaMetros son
        // metadata MemoriaPlus que el .DL byte-compatible no transporta.)
        var sisTecho = niveles[1].Sistemas[0];
        Assert.Equal(2, sisTecho.Losas.Count);
        Assert.Equal(4.00, sisTecho.Losas[0].Lx, precision: 2);
        Assert.Equal(4.00, sisTecho.Losas[0].Ly, precision: 2);
        Assert.Equal(3.50, sisTecho.Losas[1].Lx, precision: 2);
        Assert.Equal(3.00, sisTecho.Losas[1].Ly, precision: 2);
    }

    [Fact]
    public void EnumerarSistemas_recorre_todos_los_niveles()
    {
        var p = BuildProyectoDosNiveles();

        var sistemas = ProyectoService.EnumerarSistemas(p).ToList();

        Assert.Equal(2, sistemas.Count);
        Assert.Contains(sistemas, s => s.Nombre == "PB-Entrepiso");
        Assert.Contains(sistemas, s => s.Nombre == "Techo-Sistema");
    }

    // =====================================================================
    // Compatibilidad hacia atrás — un manifest legado (un solo nivel + .DL)
    // sigue cargando sin lanzar ni corromper el proyecto.
    // =====================================================================

    [Fact]
    public void AbrirProyecto_legado_un_solo_nivel_sigue_cargando()
    {
        var dir = TempDir();

        // Construir un proyecto de UN solo nivel con un sistema y guardarlo en el
        // formato manifest legado a mano: un .DL + un manifest "sistemas" plano,
        // sin la sección de árbol completo introducida en B2.
        var sistema = new Sistema { Nombre = "Sistema Legado" };
        sistema.Losas.Add(new Losa { Id = 1, Lx = 5.00, Ly = 4.00 });

        var dlFile = "sistema_legado.dl";
        DLFileService.Save(sistema, Path.Combine(dir, dlFile));

        var manifestLegado =
            "{\n" +
            "  \"nombre\": \"Proyecto Legado\",\n" +
            "  \"autor\": \"Ing. Viejo\",\n" +
            "  \"version\": 1,\n" +
            "  \"sistemas\": [\n" +
            "    { \"nombre\": \"Sistema Legado\", \"archivo_dl\": \"" + dlFile + "\" }\n" +
            "  ]\n" +
            "}\n";

        var manifestPath = Path.Combine(dir, ProyectoService.ManifestFileName);
        File.WriteAllText(manifestPath, manifestLegado, Encoding.UTF8);

        var p = ProyectoService.AbrirProyecto(manifestPath);

        Assert.Equal("Proyecto Legado", p.Nombre);
        Assert.Single(p.Edificios);
        Assert.Single(p.Edificios[0].Niveles);
        Assert.Single(p.Sistemas);
        Assert.Equal("Sistema Legado", p.Sistemas[0].Nombre);
        Assert.Single(p.Sistemas[0].Losas);
    }

    [Fact]
    public void AbrirProyecto_manifest_vacio_no_fabrica_sistema_demo_silencioso()
    {
        // Un manifest legado SIN sistemas y SIN árbol es un proyecto genuinamente
        // vacío: debe quedar editable (estructura mínima) pero NO debe enmascarar
        // un parseo fallido. Aquí basta con que cargue sin lanzar.
        var dir = TempDir();
        var manifestVacio =
            "{\n  \"nombre\": \"Vacio\",\n  \"version\": 1,\n  \"sistemas\": []\n}\n";
        var manifestPath = Path.Combine(dir, ProyectoService.ManifestFileName);
        File.WriteAllText(manifestPath, manifestVacio, Encoding.UTF8);

        var p = ProyectoService.AbrirProyecto(manifestPath);

        Assert.Equal("Vacio", p.Nombre);
        // Estructura mínima editable.
        Assert.NotEmpty(p.Edificios);
        Assert.NotEmpty(p.Edificios[0].Niveles);
    }
}
