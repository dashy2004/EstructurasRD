using System.Linq;
using LosasPlus.Models;
using MemoriaPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="SelectorTipoLosaViewModel"/>: clasificación por grupo,
/// filtros, búsqueda case-insensitive, selección y commands Confirmar/Cancelar.
/// </summary>
public class SelectorTipoLosaViewModelTests
{
    [Fact]
    public void Construye_con_catalogo_completo_no_vacio()
    {
        var vm = new SelectorTipoLosaViewModel();
        Assert.NotEmpty(vm.Todos);
        Assert.Equal(vm.Todos.Count, vm.TiposFiltrados.Count);
        Assert.Equal(vm.Todos.Count, vm.ConteoTotal);
    }

    [Fact]
    public void Tipo_inicial_arranca_seleccionado_si_existe()
    {
        var vm = new SelectorTipoLosaViewModel(tipoInicial: 33);
        Assert.NotNull(vm.TipoSeleccionado);
        Assert.Equal(33, vm.TipoSeleccionado!.Codigo);
    }

    [Fact]
    public void Tipo_inicial_invalido_deja_sin_seleccion()
    {
        var vm = new SelectorTipoLosaViewModel(tipoInicial: 999);
        Assert.Null(vm.TipoSeleccionado);
        Assert.False(vm.ConfirmarCommand.CanExecute(null));
    }

    [Fact]
    public void Tipo_inicial_null_no_explota()
    {
        var vm = new SelectorTipoLosaViewModel(tipoInicial: null);
        Assert.Null(vm.TipoSeleccionado);
    }

    // -----------------------------------------------------------------
    // Clasificación por grupo
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(10, TipoLosaGrupo.ApoyoSimple)]      // 0 empotrados, 0 vuelos
    [InlineData(21, TipoLosaGrupo.UnBordeContinuo)]  // 1 empotrado
    [InlineData(31, TipoLosaGrupo.DosBordes)]        // 2 empotrados paralelos
    [InlineData(33, TipoLosaGrupo.DosBordes)]        // 2 empotrados adyacentes
    [InlineData(40, TipoLosaGrupo.TresBordes)]
    [InlineData(60, TipoLosaGrupo.CuatroBordes)]     // perimetral
    [InlineData(71, TipoLosaGrupo.Voladizos)]        // 3 vuelos
    [InlineData(72, TipoLosaGrupo.Voladizos)]
    public void Grupo_clasifica_correctamente(int codigo, TipoLosaGrupo esperado)
    {
        Assert.True(TipoLosa.Catalogo.TryGetValue(codigo, out var t));
        Assert.Equal(esperado, SelectorTipoLosaViewModel.Grupo(t!));
    }

    [Fact]
    public void Conteo_por_grupo_suma_total()
    {
        var vm = new SelectorTipoLosaViewModel();
        var suma = vm.ConteoApoyoSimple + vm.ConteoUnBorde + vm.ConteoDosBordes
                 + vm.ConteoTresBordes + vm.ConteoCuatroBordes + vm.ConteoVoladizos;
        Assert.Equal(vm.ConteoTotal, suma);
    }

    // -----------------------------------------------------------------
    // Filtros
    // -----------------------------------------------------------------

    [Fact]
    public void Filtro_por_grupo_solo_muestra_ese_grupo()
    {
        var vm = new SelectorTipoLosaViewModel();
        vm.FiltrarPorGrupoCommand.Execute("UnBordeContinuo");
        Assert.All(vm.TiposFiltrados,
            t => Assert.Equal(TipoLosaGrupo.UnBordeContinuo, SelectorTipoLosaViewModel.Grupo(t)));
        Assert.Equal(vm.ConteoUnBorde, vm.TiposFiltrados.Count);
    }

    [Fact]
    public void Filtro_Todos_restaura_lista_completa()
    {
        var vm = new SelectorTipoLosaViewModel();
        vm.FiltrarPorGrupoCommand.Execute("Voladizos");
        Assert.True(vm.TiposFiltrados.Count < vm.ConteoTotal);
        vm.FiltrarPorGrupoCommand.Execute("Todos");
        Assert.Equal(vm.ConteoTotal, vm.TiposFiltrados.Count);
    }

    [Fact]
    public void Filtro_invalido_cae_en_Todos()
    {
        var vm = new SelectorTipoLosaViewModel();
        vm.FiltrarPorGrupoCommand.Execute("MarteHumano");
        Assert.Equal(TipoLosaGrupo.Todos, vm.GrupoActivo);
    }

    // -----------------------------------------------------------------
    // Búsqueda
    // -----------------------------------------------------------------

    [Fact]
    public void Busqueda_por_codigo_filtra_a_un_resultado()
    {
        var vm = new SelectorTipoLosaViewModel();
        vm.BusquedaTexto = "33";
        Assert.Contains(vm.TiposFiltrados, t => t.Codigo == 33);
        Assert.All(vm.TiposFiltrados, t => Assert.Contains("33", t.Codigo.ToString()));
    }

    [Fact]
    public void Busqueda_por_texto_filtra_por_descripcion()
    {
        var vm = new SelectorTipoLosaViewModel();
        vm.BusquedaTexto = "esquina";
        Assert.NotEmpty(vm.TiposFiltrados);
        Assert.All(vm.TiposFiltrados,
            t => Assert.Contains("esquina", t.Descripcion, System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Busqueda_es_case_insensitive()
    {
        var vm = new SelectorTipoLosaViewModel();
        vm.BusquedaTexto = "PERIMETRAL";
        Assert.NotEmpty(vm.TiposFiltrados);
    }

    [Fact]
    public void Busqueda_vacia_no_filtra()
    {
        var vm = new SelectorTipoLosaViewModel();
        vm.BusquedaTexto = "esquina";
        Assert.True(vm.TiposFiltrados.Count < vm.ConteoTotal);
        vm.BusquedaTexto = "";
        Assert.Equal(vm.ConteoTotal, vm.TiposFiltrados.Count);
    }

    [Fact]
    public void Busqueda_compone_con_filtro_de_grupo()
    {
        var vm = new SelectorTipoLosaViewModel();
        vm.FiltrarPorGrupoCommand.Execute("DosBordes");
        vm.BusquedaTexto = "esquina";
        // Solo deben quedar tipos de 2 bordes Y con "esquina" en descripción.
        Assert.All(vm.TiposFiltrados, t =>
        {
            Assert.Equal(TipoLosaGrupo.DosBordes, SelectorTipoLosaViewModel.Grupo(t));
            Assert.Contains("esquina", t.Descripcion, System.StringComparison.OrdinalIgnoreCase);
        });
    }

    // -----------------------------------------------------------------
    // Confirmar / Cancelar
    // -----------------------------------------------------------------

    [Fact]
    public void Confirmar_sin_seleccion_no_dispara_command()
    {
        var vm = new SelectorTipoLosaViewModel();
        Assert.False(vm.ConfirmarCommand.CanExecute(null));
    }

    [Fact]
    public void Confirmar_setea_TipoConfirmado_y_dispara_Cerrar()
    {
        var vm = new SelectorTipoLosaViewModel(tipoInicial: 33);
        var cerrado = false;
        vm.Cerrar += (_, _) => cerrado = true;
        vm.ConfirmarCommand.Execute(null);
        Assert.Equal(33, vm.TipoConfirmado);
        Assert.True(cerrado);
    }

    [Fact]
    public void Cancelar_deja_TipoConfirmado_null_y_dispara_Cerrar()
    {
        var vm = new SelectorTipoLosaViewModel(tipoInicial: 33);
        var cerrado = false;
        vm.Cerrar += (_, _) => cerrado = true;
        vm.CancelarCommand.Execute(null);
        Assert.Null(vm.TipoConfirmado);
        Assert.True(cerrado);
    }

    [Fact]
    public void Etiqueta_tipo_activo_se_actualiza_con_seleccion()
    {
        var vm = new SelectorTipoLosaViewModel();
        Assert.Contains("ningún", vm.EtiquetaTipoActivo, System.StringComparison.OrdinalIgnoreCase);
        vm.TipoSeleccionado = TipoLosa.Catalogo[33];
        Assert.Contains("33", vm.EtiquetaTipoActivo);
        Assert.Contains("esquina", vm.EtiquetaTipoActivo, System.StringComparison.OrdinalIgnoreCase);
    }
}
