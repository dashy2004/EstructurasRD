using LosasPlus.Models;
using LosasPlus.ViewModels;
using Xunit;
using System.Linq;

namespace LosasPlus.Tests.ViewModels;

public class MainViewModelTests
{
    [Fact]
    public void Seleccionar_agregar_nivel_cambia_conjuntos_y_preserva_existentes()
    {
        var vm = new MainViewModel();
        var edificio = vm.Proyecto.Edificios[0];

        // Verifica el estado inicial
        Assert.Single(edificio.Niveles);
        var nivel1 = vm.NivelActivo;
        Assert.NotNull(nivel1);
        Assert.Single(nivel1.Sistemas);

        var sistemaInicial = vm.SistemaActivo;
        Assert.NotNull(sistemaInicial);
        Assert.Equal(nivel1.Sistemas[0], sistemaInicial);

        // Agrega una losa al sistema inicial para identificarlo después
        sistemaInicial.Losas.Add(new Losa { Id = 1, Tipo = 10, Lx = 4, Ly = 5 });

        // Agregar nivel 2
        vm.AgregarNivel("Nivel 2", 3.0);

        Assert.Equal(2, edificio.Niveles.Count);
        var nivel2 = vm.NivelActivo;
        Assert.NotNull(nivel2);
        Assert.NotEqual(nivel1, nivel2);
        Assert.Equal("Nivel 2", nivel2.Nombre);
        Assert.Equal(3.0, nivel2.Cota);

        // El sistema activo debe cambiar al primer sistema del nuevo nivel
        var sistemaNivel2 = vm.SistemaActivo;
        Assert.NotNull(sistemaNivel2);
        Assert.NotEqual(sistemaInicial, sistemaNivel2);
        Assert.Equal(3, sistemaNivel2.Losas.Count); // Es un sistema demo (tiene 3 losas por defecto)

        // Cambiar de vuelta al nivel 1
        vm.SeleccionarNivel(nivel1);
        Assert.Equal(nivel1, vm.NivelActivo);
        Assert.Equal(sistemaInicial, vm.SistemaActivo);

        // Los datos del nivel 1 deben preservarse
        Assert.Equal(4, vm.SistemaActivo.Losas.Count);
        Assert.Equal(1, vm.SistemaActivo.Losas.Last().Id);
    }

    // ---- FIX A5: ColumnasEditor.Recargar() propagates on NivelActivo change ----

    /// <summary>
    /// Reproduce el bug A5: al construir MainViewModel NivelActivo era null, así que
    /// ColumnasEditorViewModel.Recargar() en el ctor devolvía Columnas == null.
    /// El fix: el setter de NivelActivo llama ColumnasEditor?.Recargar() después de
    /// asignar el nivel, de modo que vm.ColumnasEditor.Columnas apunta a la colección
    /// real del nivel activo y no queda null/vacía.
    /// </summary>
    [Fact]
    public void ColumnasEditor_Columnas_refleja_nivel_activo_al_cambiar_NivelActivo()
    {
        var vm = new MainViewModel();
        var edificio = vm.Proyecto.Edificios[0];

        // Preparar nivel 1 con dos columnas explícitas.
        var nivel1 = vm.NivelActivo!;
        nivel1.Columnas.Add(new Columna { Id = 1, Nombre = "C-1" });
        nivel1.Columnas.Add(new Columna { Id = 2, Nombre = "C-2" });

        // Agregar un segundo nivel con una columna distinta.
        vm.AgregarNivel("Nivel 2", 3.0);
        var nivel2 = vm.NivelActivo!;
        nivel2.Columnas.Add(new Columna { Id = 10, Nombre = "C-10" });

        // El editor de columnas debe mostrar las del nivel 2 (activo).
        Assert.NotNull(vm.ColumnasEditor.Columnas);
        Assert.Single(vm.ColumnasEditor.Columnas);
        Assert.Equal("C-10", vm.ColumnasEditor.Columnas![0].Nombre);

        // Al volver al nivel 1, el editor debe reflejar sus 2 columnas.
        vm.SeleccionarNivel(nivel1);
        Assert.NotNull(vm.ColumnasEditor.Columnas);
        Assert.Equal(2, vm.ColumnasEditor.Columnas!.Count);
        Assert.Equal("C-1", vm.ColumnasEditor.Columnas[0].Nombre);
    }

    // ---- FIX A3: seleccionar un reciente no debe abrir el proyecto ----

    /// <summary>
    /// Reproduce el bug A3: el setter de ProyectoRecienteSeleccionado llamaba
    /// AbrirEnEditorCommand.Execute(null), lo que abría el proyecto de inmediato
    /// con un solo clic. El fix: el setter solo asigna la selección; el usuario
    /// debe hacer clic en el botón «Abrir» (o doble clic) explícitamente.
    /// </summary>
    [Fact]
    public void ProyectoRecienteSeleccionado_no_cambia_proyecto_activo_al_solo_seleccionar()
    {
        var vm = new MainViewModel();
        var nombreInicial = vm.Proyecto.Nombre;

        // Crear un ProyectoResumen que apunte a una ruta inexistente (no se cargará).
        var reciente = new MemoriaPlus.ViewModels.ProyectoResumen(
            "Proyecto Reciente",
            "Ing. Test",
            "COD-001",
            1,
            "01/01/25",
            "Guardado",
            "/tmp/no_existe_para_test.lpx.json");

        // Solo asignar la selección — NO debe abrir nada.
        vm.ProyectoRecienteSeleccionado = reciente;

        // El nombre del proyecto activo no debe cambiar (no se abrió nada).
        Assert.Equal(nombreInicial, vm.Proyecto.Nombre);
        // La selección sí debe quedar asignada.
        Assert.Same(reciente, vm.ProyectoRecienteSeleccionado);
    }

    // ---- Fase C: branding centralizado --------------------------------

    [Fact]
    public void Branding_const_producto_es_EstructurasRD()
    {
        Assert.Equal("EstructurasRD", MemoriaPlus.Common.Branding.Producto);
    }

    [Fact]
    public void TituloVentana_usa_la_marca_EstructurasRD()
    {
        var vm = new MainViewModel();
        Assert.StartsWith(MemoriaPlus.Common.Branding.Producto, vm.TituloVentana);
        Assert.StartsWith("EstructurasRD", vm.TituloVentana);
    }

    // ---- Fase C: atajo Ctrl+L (AgregarLosa) cableado a un ICommand -----

    [Fact]
    public void AgregarLosaCommand_existe_y_agrega_una_losa()
    {
        var vm = new MainViewModel();
        var antes = vm.Sistema.Losas.Count;

        Assert.NotNull(vm.AgregarLosaCommand);
        Assert.True(vm.AgregarLosaCommand!.CanExecute(null));
        vm.AgregarLosaCommand.Execute(null);

        Assert.Equal(antes + 1, vm.Sistema.Losas.Count);
    }
}
