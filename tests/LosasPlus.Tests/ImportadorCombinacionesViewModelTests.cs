using LosasPlus.Cargas;
using LosasPlus.Models;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="ImportadorCombinacionesViewModel"/> (Fase 2,
/// Iteración 3): las cuatro estrategias de fusión y el snapshot de Undo.
/// </summary>
public class ImportadorCombinacionesViewModelTests
{
    /// <summary>«Archivo» parseado de prueba: 2 casos (D, S) y 2 combinaciones.</summary>
    private static CombinacionesProyecto Archivo()
    {
        var cp = new CombinacionesProyecto { Norma = NormaCombinaciones.R027 };
        cp.Casos.Add(new CasoCarga { Codigo = "D", Nombre = "Muerta archivo", Tipo = TipoCasoCarga.Muerta });
        cp.Casos.Add(new CasoCarga { Codigo = "S", Nombre = "Nieve",          Tipo = TipoCasoCarga.Viva   });
        cp.Combinaciones.Add(new CombinacionCarga { Nombre = "1.4D",  Tipo = TipoCombinacion.Ultima });
        cp.Combinaciones.Add(new CombinacionCarga { Nombre = "D + S", Tipo = TipoCombinacion.Servicio });
        return cp;
    }

    /// <summary>Proyecto con un caso «D» y una combinación «1.4D» preexistentes.</summary>
    private static Proyecto ProyectoConDatos()
    {
        var p = new Proyecto();
        p.Combinaciones.Casos.Add(new CasoCarga { Codigo = "D", Nombre = "Muerta proyecto", Tipo = TipoCasoCarga.Muerta });
        p.Combinaciones.Combinaciones.Add(new CombinacionCarga { Nombre = "1.4D", Tipo = TipoCombinacion.Ultima });
        return p;
    }

    [Fact]
    public void ReemplazarTodo_deja_exactamente_lo_del_archivo()
    {
        var p = ProyectoConDatos();
        var vm = new ImportadorCombinacionesViewModel(p, Archivo(), "x.dzp", () => { })
        {
            Estrategia = EstrategiaImportacion.ReemplazarTodo,
        };

        vm.AplicarCommand.Execute(null);

        Assert.Equal(2, p.Combinaciones.Casos.Count);
        Assert.Equal(2, p.Combinaciones.Combinaciones.Count);
        Assert.Equal("Muerta archivo", p.Combinaciones.Casos[0].Nombre);   // reemplazado
        Assert.True(vm.Aplicado);
    }

    [Fact]
    public void SoloAgregarNuevas_no_toca_lo_existente()
    {
        var p = ProyectoConDatos();
        var vm = new ImportadorCombinacionesViewModel(p, Archivo(), "x.dzp", () => { })
        {
            Estrategia = EstrategiaImportacion.SoloAgregarNuevas,
        };

        vm.AplicarCommand.Execute(null);

        Assert.Equal(2, p.Combinaciones.Casos.Count);                       // D + S nuevo
        Assert.Equal("Muerta proyecto", p.Combinaciones.Casos[0].Nombre);   // D NO se tocó
        Assert.Equal(2, p.Combinaciones.Combinaciones.Count);               // 1.4D + «D + S»
    }

    [Fact]
    public void MergeInteligente_actualiza_casos_y_agrega_combinaciones()
    {
        var p = ProyectoConDatos();
        var vm = new ImportadorCombinacionesViewModel(p, Archivo(), "x.dzp", () => { })
        {
            Estrategia = EstrategiaImportacion.MergeInteligente,
        };

        vm.AplicarCommand.Execute(null);

        Assert.Equal(2, p.Combinaciones.Casos.Count);
        Assert.Equal("Muerta archivo", p.Combinaciones.Casos[0].Nombre);    // D actualizado
        Assert.Equal(2, p.Combinaciones.Combinaciones.Count);               // «D + S» agregada
    }

    [Fact]
    public void SoloImportarCasos_ignora_las_combinaciones()
    {
        var p = ProyectoConDatos();
        var vm = new ImportadorCombinacionesViewModel(p, Archivo(), "x.dzp", () => { })
        {
            Estrategia = EstrategiaImportacion.SoloImportarCasos,
        };

        vm.AplicarCommand.Execute(null);

        Assert.Equal(2, p.Combinaciones.Casos.Count);
        Assert.Single(p.Combinaciones.Combinaciones);   // las combinaciones no se tocan
    }

    [Fact]
    public void Aplicar_toma_un_snapshot_de_undo()
    {
        int snapshots = 0;
        var p = ProyectoConDatos();
        var vm = new ImportadorCombinacionesViewModel(p, Archivo(), "x.dzp", () => snapshots++);

        vm.AplicarCommand.Execute(null);

        Assert.Equal(1, snapshots);
    }
}
