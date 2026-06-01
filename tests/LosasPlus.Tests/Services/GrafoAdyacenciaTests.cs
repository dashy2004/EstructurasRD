using System.Linq;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del BFS de <see cref="GrafoAdyacencia"/> — la recolección de losas
/// conectadas para la herramienta «Mover Conectadas» (Iteración 3, Epic v1.2).
/// </summary>
public class GrafoAdyacenciaTests
{
    private static Sistema SistemaCon(params int[] ids)
    {
        var s = new Sistema();
        foreach (var id in ids) s.Losas.Add(new Losa { Id = id });
        return s;
    }

    [Fact]
    public void Sistema_null_devuelve_set_vacio()
    {
        var c = GrafoAdyacencia.ComponenteConexa(null, 1);
        Assert.Empty(c);
    }

    [Fact]
    public void Raiz_inexistente_devuelve_set_vacio()
    {
        var s = SistemaCon(1, 2);
        var c = GrafoAdyacencia.ComponenteConexa(s, 99);
        Assert.Empty(c);
    }

    [Fact]
    public void Losa_sola_sin_adyacencias_devuelve_solo_la_raiz()
    {
        var s = SistemaCon(1, 2, 3);
        var c = GrafoAdyacencia.ComponenteConexa(s, 2);
        Assert.Single(c);
        Assert.Contains(2, c);
    }

    [Fact]
    public void Pareja_conectada_por_BordeX_devuelve_ambas()
    {
        var s = SistemaCon(1, 2);
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2 });
        var c = GrafoAdyacencia.ComponenteConexa(s, 1);
        Assert.Equal(2, c.Count);
        Assert.Contains(1, c);
        Assert.Contains(2, c);
    }

    [Fact]
    public void Cadena_A_B_C_devuelve_los_tres_desde_cualquier_extremo()
    {
        var s = SistemaCon(1, 2, 3);
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2 });
        s.BordesY.Add(new BordeAdic { BI = 2, BJ = 3 });

        var desde1 = GrafoAdyacencia.ComponenteConexa(s, 1);
        var desde3 = GrafoAdyacencia.ComponenteConexa(s, 3);

        Assert.Equal(3, desde1.Count);
        Assert.Equal(3, desde3.Count);
        Assert.True(new[] { 1, 2, 3 }.All(desde1.Contains));
        Assert.True(new[] { 1, 2, 3 }.All(desde3.Contains));
    }

    [Fact]
    public void Ciclo_no_se_cuelga_y_devuelve_la_componente()
    {
        var s = SistemaCon(1, 2, 3);
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2 });
        s.BordesX.Add(new BordeAdic { BI = 2, BJ = 3 });
        s.BordesY.Add(new BordeAdic { BI = 3, BJ = 1 });   // cierra el ciclo

        var c = GrafoAdyacencia.ComponenteConexa(s, 1);

        Assert.Equal(3, c.Count);
    }

    [Fact]
    public void Componentes_desconectados_no_se_mezclan()
    {
        var s = SistemaCon(1, 2, 3, 4);
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2 });
        s.BordesY.Add(new BordeAdic { BI = 3, BJ = 4 });

        var desde1 = GrafoAdyacencia.ComponenteConexa(s, 1);

        Assert.Equal(2, desde1.Count);
        Assert.DoesNotContain(3, desde1);
        Assert.DoesNotContain(4, desde1);
    }

    [Fact]
    public void Bordes_con_ids_fuera_del_sistema_se_ignoran()
    {
        var s = SistemaCon(1, 2);
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 99 });   // 99 no existe

        var c = GrafoAdyacencia.ComponenteConexa(s, 1);

        Assert.Single(c);
        Assert.Contains(1, c);
    }
}
