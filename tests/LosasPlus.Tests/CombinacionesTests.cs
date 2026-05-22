using System.Collections.Generic;
using System.Linq;
using LosasPlus.Cargas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del dominio de Casos y Combinaciones de carga (Fase 2 de la suite
/// estructural): la estructura de clases y la semilla por defecto ASCE 7-05.
/// </summary>
public class CombinacionesTests
{
    [Fact]
    public void SemillaPorDefecto_declara_los_cuatro_casos_base()
    {
        var cp = CombinacionesProyecto.SemillaPorDefecto();

        Assert.Equal(4, cp.Casos.Count);
        Assert.Contains(cp.Casos, c => c.Codigo == "D");
        Assert.Contains(cp.Casos, c => c.Codigo == "L");
        Assert.Contains(cp.Casos, c => c.Codigo == "Lr");
        Assert.Contains(cp.Casos, c => c.Codigo == "E");
    }

    [Fact]
    public void SemillaPorDefecto_asigna_el_tipo_correcto_a_cada_caso()
    {
        var cp = CombinacionesProyecto.SemillaPorDefecto();

        Assert.Equal(TipoCasoCarga.Muerta, cp.Casos.Single(c => c.Codigo == "D").Tipo);
        Assert.Equal(TipoCasoCarga.Viva,   cp.Casos.Single(c => c.Codigo == "L").Tipo);
        Assert.Equal(TipoCasoCarga.Techo,  cp.Casos.Single(c => c.Codigo == "Lr").Tipo);
        Assert.Equal(TipoCasoCarga.Sismo,  cp.Casos.Single(c => c.Codigo == "E").Tipo);
    }

    [Fact]
    public void SemillaPorDefecto_registra_ocho_combinaciones()
    {
        var cp = CombinacionesProyecto.SemillaPorDefecto();
        Assert.Equal(8, cp.Combinaciones.Count);
    }

    [Fact]
    public void SemillaPorDefecto_tiene_cuatro_ultimas_y_cuatro_de_servicio()
    {
        var cp = CombinacionesProyecto.SemillaPorDefecto();

        Assert.Equal(4, cp.Combinaciones.Count(c => c.Tipo == TipoCombinacion.Ultima));
        Assert.Equal(4, cp.Combinaciones.Count(c => c.Tipo == TipoCombinacion.Servicio));
    }

    [Fact]
    public void SemillaPorDefecto_combinacion_ultima_principal_tiene_los_factores_correctos()
    {
        var cp = CombinacionesProyecto.SemillaPorDefecto();
        var combo = cp.Combinaciones.Single(c => c.Nombre == "1.2D + 1.6L + 0.5Lr");

        Assert.Equal(TipoCombinacion.Ultima, combo.Tipo);
        Assert.Equal(3, combo.Terminos.Count);
        Assert.Equal(1.2, combo.Terminos.Single(t => t.CodigoCaso == "D").Factor);
        Assert.Equal(1.6, combo.Terminos.Single(t => t.CodigoCaso == "L").Factor);
        Assert.Equal(0.5, combo.Terminos.Single(t => t.CodigoCaso == "Lr").Factor);
    }

    [Fact]
    public void SemillaPorDefecto_almacena_la_norma_recibida()
    {
        var cp = CombinacionesProyecto.SemillaPorDefecto(NormaCombinaciones.Asce7_05);
        Assert.Equal(NormaCombinaciones.Asce7_05, cp.Norma);
    }

    [Fact]
    public void CombinacionesProyecto_nuevo_arranca_vacio()
    {
        var cp = new CombinacionesProyecto();

        Assert.Empty(cp.Casos);
        Assert.Empty(cp.Combinaciones);
    }

    [Fact]
    public void Las_clases_de_dominio_notifican_cambios_de_propiedad()
    {
        var props = new List<string?>();

        var caso = new CasoCarga();
        caso.PropertyChanged += (_, a) => props.Add(a.PropertyName);
        caso.Codigo = "D";

        var combo = new CombinacionCarga();
        combo.PropertyChanged += (_, a) => props.Add(a.PropertyName);
        combo.Nombre = "1.4D";

        var termino = new TerminoCombinacion();
        termino.PropertyChanged += (_, a) => props.Add(a.PropertyName);
        termino.Factor = 1.4;

        Assert.Contains(nameof(CasoCarga.Codigo), props);
        Assert.Contains(nameof(CombinacionCarga.Nombre), props);
        Assert.Contains(nameof(TerminoCombinacion.Factor), props);
    }
}
