using System.Collections.Generic;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Validation;
using MemoriaPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Fase B (B2b): validación y búsqueda deben cubrir TODOS los niveles del
/// proyecto, no sólo la fachada <c>Proyecto.Sistemas</c> (= <c>Niveles[0]</c>).
///
/// <para>
/// Antes de B2b, las reglas de validación y los sitios de búsqueda iteraban
/// <c>proyecto.Sistemas</c> (la fachada del primer nivel), así que un sistema
/// inválido en el nivel 2 no se reportaba y un sistema del nivel 2 no aparecía
/// en la búsqueda global. El fix enruta ambos a
/// <c>ProyectoService.EnumerarSistemas(proyecto)</c>
/// (= <c>Edificios.SelectMany(Niveles).SelectMany(Sistemas)</c>).
/// </para>
/// </summary>
public class MultinivelValidacionBusquedaTests
{
    /// <summary>
    /// Proyecto de 2 niveles. El nivel 1 trae un sistema válido; el nivel 2 trae
    /// un sistema con una losa de tipo INVÁLIDO (999, fuera del catálogo
    /// Pieper-Martens) — el caso que la validación pre-B2b ignoraba.
    /// </summary>
    private static Proyecto ProyectoDosNivelesConInvalidoEnNivel2()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.AsegurarEstructura();
        var ed = p.Edificios[0];

        // Nivel 1 — sistema válido.
        var nivel1 = ed.Niveles[0];
        nivel1.Nombre = "Planta Baja";
        var sisOk = new Sistema { Nombre = "PB-OK", Uso = SistemaUso.Entrepiso };
        sisOk.Losas.Add(new Losa { Id = 1, Tipo = 10, Lx = 4.0, Ly = 4.0, Espesor = 0.150 });
        nivel1.Sistemas.Add(sisOk);

        // Nivel 2 — sistema con tipo de losa inválido + nombre buscable.
        var nivel2 = new Nivel { Nombre = "Nivel 2", Cota = 3.0 };
        var sisN2 = new Sistema { Nombre = "Sistema-Nivel2-Buscable", Uso = SistemaUso.Techo };
        sisN2.Losas.Add(new Losa { Id = 7, Tipo = 999, Lx = 4.0, Ly = 4.0, Espesor = 0.150 });
        nivel2.Sistemas.Add(sisN2);
        ed.Niveles.Add(nivel2);

        return p;
    }

    // =====================================================================
    // Validación multinivel.
    // =====================================================================

    [Fact]
    public void Validacion_flaggea_sistema_invalido_en_nivel_2()
    {
        var p = ProyectoDosNivelesConInvalidoEnNivel2();

        var reporte = ValidationEngine.Default().Validar(p);

        // Debe existir un error de tipo de losa no permitido apuntando al
        // sistema del NIVEL 2 (antes de B2b no se detectaba).
        Assert.Contains(reporte.Issues, i =>
            i.Codigo == "TIPO-LOSA-NO-PERMITIDO"
            && i.NombreSistema == "Sistema-Nivel2-Buscable");
        Assert.True(reporte.Errores >= 1);
    }

    // =====================================================================
    // Búsqueda multinivel.
    // =====================================================================

    [Fact]
    public void Busqueda_encuentra_sistema_en_nivel_2()
    {
        var p = ProyectoDosNivelesConInvalidoEnNivel2();

        var vm = new BusquedaViewModel(
            getProyectosRecientes: () => System.Array.Empty<ProyectoResumen>(),
            getProyectoActivo:     () => p,
            abrirProyectoPorPath:  _ => { },
            irASistema:            _ => { },
            irALosa:               (_, __) => { });

        vm.TextoBusqueda = "Nivel2-Buscable";

        Assert.Single(vm.ResultadosSistemas);
        Assert.Equal("Sistema-Nivel2-Buscable", vm.ResultadosSistemas[0].NombreSistema);
    }

    [Fact]
    public void Busqueda_encuentra_losa_en_nivel_2()
    {
        var p = ProyectoDosNivelesConInvalidoEnNivel2();

        var vm = new BusquedaViewModel(
            getProyectosRecientes: () => System.Array.Empty<ProyectoResumen>(),
            getProyectoActivo:     () => p,
            abrirProyectoPorPath:  _ => { },
            irASistema:            _ => { },
            irALosa:               (_, __) => { });

        // La losa Id=7 sólo existe en el sistema del nivel 2.
        vm.TextoBusqueda = "7";

        Assert.Contains(vm.ResultadosLosas, r =>
            r.LosaId == 7 && r.NombreSistema == "Sistema-Nivel2-Buscable");
    }
}
