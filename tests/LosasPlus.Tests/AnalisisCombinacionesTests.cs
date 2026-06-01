using LosasPlus.Cargas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del validador normativo <see cref="AnalisisCombinaciones"/> (Fase 2,
/// Iteración 4): detección de una base de cargas incompleta o inconsistente.
/// </summary>
public class AnalisisCombinacionesTests
{
    /// <summary>Construye un caso de carga con el código y tipo indicados.</summary>
    private static CasoCarga Caso(string codigo, TipoCasoCarga tipo)
        => new() { Codigo = codigo, Nombre = codigo, Tipo = tipo };

    /// <summary>Construye una combinación con sus términos vía el indexer.</summary>
    private static CombinacionCarga Combo(string nombre, params (string caso, double factor)[] terminos)
    {
        var c = new CombinacionCarga { Nombre = nombre };
        foreach (var (caso, factor) in terminos)
            c[caso] = factor;
        return c;
    }

    [Fact]
    public void Validar_null_devuelve_una_lista_vacia()
    {
        Assert.Empty(AnalisisCombinaciones.Validar(null));
    }

    [Fact]
    public void Validar_proyecto_vacio_reporta_falta_de_casos_y_combinaciones()
    {
        var alertas = AnalisisCombinaciones.Validar(new CombinacionesProyecto());

        Assert.Contains(alertas, a => a.Contains("casos"));
        Assert.Contains(alertas, a => a.Contains("combinaciones"));
    }

    [Fact]
    public void Validar_detecta_la_falta_de_una_combinacion_de_solo_muerta()
    {
        var cp = new CombinacionesProyecto();
        cp.Casos.Add(Caso("D", TipoCasoCarga.Muerta));
        cp.Casos.Add(Caso("L", TipoCasoCarga.Viva));
        cp.Combinaciones.Add(Combo("D + L", ("D", 1.2), ("L", 1.6)));

        var alertas = AnalisisCombinaciones.Validar(cp);

        Assert.Contains(alertas, a => a.Contains("sólo carga muerta"));
    }

    [Fact]
    public void Validar_no_alerta_si_existe_una_combinacion_de_solo_muerta()
    {
        var cp = new CombinacionesProyecto();
        cp.Casos.Add(Caso("D", TipoCasoCarga.Muerta));
        cp.Combinaciones.Add(Combo("1.4D", ("D", 1.4)));

        var alertas = AnalisisCombinaciones.Validar(cp);

        Assert.DoesNotContain(alertas, a => a.Contains("sólo carga muerta"));
    }

    [Fact]
    public void Validar_detecta_un_caso_que_no_participa_en_ninguna_combinacion()
    {
        var cp = new CombinacionesProyecto();
        cp.Casos.Add(Caso("D", TipoCasoCarga.Muerta));
        cp.Casos.Add(Caso("W", TipoCasoCarga.Viento));
        cp.Combinaciones.Add(Combo("1.4D", ("D", 1.4)));

        var alertas = AnalisisCombinaciones.Validar(cp);

        Assert.Contains(alertas, a => a.Contains("«W»"));
    }

    [Fact]
    public void Validar_semilla_por_defecto_no_genera_alertas()
    {
        var alertas = AnalisisCombinaciones.Validar(CombinacionesProyecto.SemillaPorDefecto());
        Assert.Empty(alertas);
    }
}
