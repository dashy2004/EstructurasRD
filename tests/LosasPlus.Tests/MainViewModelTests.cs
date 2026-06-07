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
}
