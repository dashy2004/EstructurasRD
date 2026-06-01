using System.Collections.Generic;
using System.Linq;
using LosasPlus.Models;
using MemoriaPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="BusquedaViewModel"/>: matching case-insensitive en las
/// 3 dimensiones (proyectos / sistemas / losas), refresco automático ante
/// cambios del proyecto activo, comandos de navegación, casos edge.
/// </summary>
public class BusquedaViewModelTests
{
    private static (BusquedaViewModel vm, List<string> navProyectos, List<string> navSistemas, List<(string s, int id)> navLosas)
        BuildVm(Proyecto? proyecto, params ProyectoResumen[] recientes)
    {
        var navProyectos = new List<string>();
        var navSistemas = new List<string>();
        var navLosas = new List<(string, int)>();
        var vm = new BusquedaViewModel(
            getProyectosRecientes: () => recientes,
            getProyectoActivo:     () => proyecto,
            abrirProyectoPorPath:  p => navProyectos.Add(p),
            irASistema:            s => navSistemas.Add(s),
            irALosa:               (s, id) => navLosas.Add((s, id)));
        return (vm, navProyectos, navSistemas, navLosas);
    }

    private static ProyectoResumen Reciente(string nombre, string ingeniero, string codia, string path = "")
        => new(nombre, ingeniero, codia, 1, "01/01/26", "Guardado", path);

    private static Proyecto ProyectoConSistemas(params (string nombre, int[] losaIds)[] sistemas)
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        foreach (var (nombre, losaIds) in sistemas)
        {
            var s = new Sistema { Nombre = nombre, Uso = SistemaUso.Entrepiso };
            foreach (var id in losaIds)
                s.Losas.Add(new Losa { Id = id, Tipo = 10, Lx = 4, Ly = 4 });
            p.Sistemas.Add(s);
        }
        return p;
    }

    [Fact]
    public void Busqueda_vacia_no_genera_resultados()
    {
        var (vm, _, _, _) = BuildVm(ProyectoConSistemas(("E1", new[] { 1 })),
            Reciente("Torre A", "Pérez", "12345"));
        Assert.True(vm.BusquedaVacia);
        Assert.Equal(0, vm.ConteoTotal);
        Assert.False(vm.HayResultados);
    }

    [Fact]
    public void Busqueda_proyectos_por_nombre()
    {
        var (vm, _, _, _) = BuildVm(null,
            Reciente("Torre Sol", "Pérez", "12345"),
            Reciente("Edificio Luna", "Gómez", "67890"));
        vm.TextoBusqueda = "sol";
        Assert.Single(vm.ResultadosProyectos);
        Assert.Equal("Torre Sol", vm.ResultadosProyectos[0].Titulo);
    }

    [Fact]
    public void Busqueda_proyectos_por_ingeniero()
    {
        var (vm, _, _, _) = BuildVm(null,
            Reciente("Torre A", "Rafael Gómez", "12345"),
            Reciente("Torre B", "Luis Pérez", "67890"));
        vm.TextoBusqueda = "rafael";
        Assert.Single(vm.ResultadosProyectos);
    }

    [Fact]
    public void Busqueda_proyectos_por_codia()
    {
        var (vm, _, _, _) = BuildVm(null,
            Reciente("A", "X", "11111"),
            Reciente("B", "Y", "99999"));
        vm.TextoBusqueda = "99999";
        Assert.Single(vm.ResultadosProyectos);
        Assert.Equal("B", vm.ResultadosProyectos[0].Titulo);
    }

    [Fact]
    public void Busqueda_es_case_insensitive()
    {
        var (vm, _, _, _) = BuildVm(null, Reciente("Torre Sol", "X", "1"));
        vm.TextoBusqueda = "TORRE";
        Assert.Single(vm.ResultadosProyectos);
    }

    [Fact]
    public void Busqueda_sistemas_por_nombre()
    {
        var (vm, _, _, _) = BuildVm(ProyectoConSistemas(
            ("E1", new[] { 1, 2 }),
            ("Techo", new[] { 1 })));
        vm.TextoBusqueda = "tech";
        Assert.Single(vm.ResultadosSistemas);
        Assert.Contains("Techo", vm.ResultadosSistemas[0].Titulo);
        Assert.Equal("Techo", vm.ResultadosSistemas[0].NombreSistema);
    }

    [Fact]
    public void Busqueda_losas_por_id_numerico()
    {
        var (vm, _, _, _) = BuildVm(ProyectoConSistemas(("E1", new[] { 1, 2, 12, 23 })));
        vm.TextoBusqueda = "12";
        // Match: losa 12 (literal "12") y losa 23 NO (no contiene "12").
        // Pero sí matches "2" si el usuario busca dígitos solos: pruebo "12" para evitarlo.
        Assert.Contains(vm.ResultadosLosas, r => r.LosaId == 12);
        Assert.DoesNotContain(vm.ResultadosLosas, r => r.LosaId == 1);  // "1" no es "12"
    }

    [Fact]
    public void Busqueda_losas_por_label_L_prefix()
    {
        var (vm, _, _, _) = BuildVm(ProyectoConSistemas(("E1", new[] { 5 })));
        vm.TextoBusqueda = "L5";
        Assert.Single(vm.ResultadosLosas);
        Assert.Equal(5, vm.ResultadosLosas[0].LosaId);
    }

    [Fact]
    public void Busqueda_compone_multiples_dimensiones()
    {
        var (vm, _, _, _) = BuildVm(ProyectoConSistemas(("E1", new[] { 1 })),
            Reciente("Torre E1", "X", "1"));
        vm.TextoBusqueda = "E1";
        // Match: proyecto con E1 en nombre, sistema E1, no losas (id=1 no contiene "E1").
        Assert.NotEmpty(vm.ResultadosProyectos);
        Assert.NotEmpty(vm.ResultadosSistemas);
    }

    [Fact]
    public void Limpiar_resetea_texto_y_resultados()
    {
        var (vm, _, _, _) = BuildVm(null, Reciente("Torre", "X", "1"));
        vm.TextoBusqueda = "torre";
        Assert.True(vm.HayResultados);
        vm.LimpiarCommand.Execute(null);
        Assert.Empty(vm.TextoBusqueda);
        Assert.False(vm.HayResultados);
        Assert.True(vm.BusquedaVacia);
    }

    [Fact]
    public void Refrescar_recorre_los_datos_actuales()
    {
        var proyecto = ProyectoConSistemas(("E1", new[] { 1 }));
        var (vm, _, _, _) = BuildVm(proyecto);
        vm.TextoBusqueda = "E1";
        Assert.Single(vm.ResultadosSistemas);
        // Modificar el proyecto y refrescar — debe encontrar el nuevo sistema.
        proyecto.Sistemas.Add(new Sistema { Nombre = "E2", Uso = SistemaUso.Entrepiso });
        // Sin Refrescar, los resultados todavía reflejan el estado antes del cambio
        // (la búsqueda solo recorre Sistemas en el momento de la llamada).
        vm.Refrescar();
        Assert.Single(vm.ResultadosSistemas);  // "E1" solo (E2 no matchea "E1")
        vm.TextoBusqueda = "E";
        Assert.Equal(2, vm.ResultadosSistemas.Count);
    }

    // -----------------------------------------------------------------
    // Navegación
    // -----------------------------------------------------------------

    [Fact]
    public void IrAResultado_proyecto_invoca_callback_con_path()
    {
        var (vm, navProyectos, _, _) = BuildVm(null,
            Reciente("Torre", "X", "1", path: @"C:\proyectos\torre.lpx.json"));
        vm.TextoBusqueda = "torre";
        vm.IrAResultadoCommand.Execute(vm.ResultadosProyectos[0]);
        Assert.Single(navProyectos);
        Assert.Equal(@"C:\proyectos\torre.lpx.json", navProyectos[0]);
    }

    [Fact]
    public void IrAResultado_proyecto_sin_path_es_noop()
    {
        var (vm, navProyectos, _, _) = BuildVm(null, Reciente("Torre", "X", "1", path: ""));
        vm.TextoBusqueda = "torre";
        vm.IrAResultadoCommand.Execute(vm.ResultadosProyectos[0]);
        Assert.Empty(navProyectos);
    }

    [Fact]
    public void IrAResultado_sistema_invoca_callback_con_nombre()
    {
        var (vm, _, navSistemas, _) = BuildVm(ProyectoConSistemas(("E1", new[] { 1 })));
        vm.TextoBusqueda = "E1";
        vm.IrAResultadoCommand.Execute(vm.ResultadosSistemas[0]);
        Assert.Single(navSistemas);
        Assert.Equal("E1", navSistemas[0]);
    }

    [Fact]
    public void IrAResultado_losa_invoca_callback_con_sistema_y_id()
    {
        var (vm, _, _, navLosas) = BuildVm(ProyectoConSistemas(("E1", new[] { 7 })));
        vm.TextoBusqueda = "L7";
        vm.IrAResultadoCommand.Execute(vm.ResultadosLosas[0]);
        Assert.Single(navLosas);
        Assert.Equal(("E1", 7), navLosas[0]);
    }

    [Fact]
    public void IrAResultado_null_es_noop()
    {
        var (vm, navP, navS, navL) = BuildVm(ProyectoConSistemas(("E1", new[] { 1 })));
        vm.IrAResultadoCommand.Execute(null);
        Assert.Empty(navP);
        Assert.Empty(navS);
        Assert.Empty(navL);
    }

    [Fact]
    public void Conteos_corresponden_a_colecciones()
    {
        var (vm, _, _, _) = BuildVm(ProyectoConSistemas(("E1", new[] { 1 })),
            Reciente("Torre E1", "X", "1"));
        vm.TextoBusqueda = "1";
        Assert.Equal(vm.ResultadosProyectos.Count, vm.ConteoProyectos);
        Assert.Equal(vm.ResultadosSistemas.Count, vm.ConteoSistemas);
        Assert.Equal(vm.ResultadosLosas.Count, vm.ConteoLosas);
        Assert.Equal(vm.ConteoProyectos + vm.ConteoSistemas + vm.ConteoLosas, vm.ConteoTotal);
    }

    [Fact]
    public void Sin_proyecto_activo_solo_busca_en_recientes()
    {
        var (vm, _, _, _) = BuildVm(null,
            Reciente("Torre Sol", "X", "1"));
        vm.TextoBusqueda = "sol";
        Assert.Single(vm.ResultadosProyectos);
        Assert.Empty(vm.ResultadosSistemas);
        Assert.Empty(vm.ResultadosLosas);
    }
}
