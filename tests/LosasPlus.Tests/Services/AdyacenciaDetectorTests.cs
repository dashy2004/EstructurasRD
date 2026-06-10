using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de <see cref="AdyacenciaDetector"/> — la detección geométrica de la
/// Fase 4 del plan CAD: pares de losas que se tocan en un borde y aún no están
/// conectadas por un <see cref="BordeAdic"/>.
/// </summary>
public class AdyacenciaDetectorTests
{
    /// <summary>Losa anclada (Anclada=true) — la que el detector sí considera.</summary>
    private static Losa Anclada(int id, double x, double y, double lx, double ly) =>
        new() { Id = id, Tipo = 10, Carga = 2.0, Espesor = 0.12, Rec = 0.02,
                Lx = lx, Ly = ly, CoordenadaX = x, CoordenadaY = y, Anclada = true };

    /// <summary>Losa flotante (libre, sin anclar) — la que el detector ignora.</summary>
    private static Losa Flotante(int id, double lx, double ly) =>
        new() { Id = id, Tipo = 10, Carga = 2.0, Espesor = 0.12, Rec = 0.02, Lx = lx, Ly = ly };

    [Fact]
    public void Dos_losas_adyacentes_en_X_producen_un_candidato_BordeX()
    {
        var s = new Sistema();
        s.Losas.Add(Anclada(1, 0, 0, 4, 3));
        s.Losas.Add(Anclada(2, 4, 0, 3, 3));

        var c = Assert.Single(AdyacenciaDetector.Detectar(s));
        Assert.True(c.EsBordeX);
        Assert.Equal(1, c.BI);            // izquierda
        Assert.Equal(2, c.BJ);            // derecha
        Assert.Equal(4.0, c.MidX, precision: 6);
        Assert.Equal(1.5, c.MidY, precision: 6);
    }

    [Fact]
    public void Dos_losas_adyacentes_en_Y_producen_un_candidato_BordeY()
    {
        var s = new Sistema();
        s.Losas.Add(Anclada(1, 0, 0, 4, 3));
        s.Losas.Add(Anclada(2, 0, 3, 4, 5));

        var c = Assert.Single(AdyacenciaDetector.Detectar(s));
        Assert.False(c.EsBordeX);
        Assert.Equal(1, c.BI);            // arriba
        Assert.Equal(2, c.BJ);            // abajo
        Assert.Equal(2.0, c.MidX, precision: 6);
        Assert.Equal(3.0, c.MidY, precision: 6);
    }

    [Fact]
    public void Una_separacion_menor_que_la_tolerancia_se_detecta()
    {
        // Hueco de 3 cm entre las losas (< tolerancia 5 cm).
        var s = new Sistema();
        s.Losas.Add(Anclada(1, 0, 0, 4, 3));
        s.Losas.Add(Anclada(2, 4.03, 0, 3, 3));
        Assert.Single(AdyacenciaDetector.Detectar(s));
    }

    [Fact]
    public void Un_solape_pequeno_en_el_borde_se_detecta()
    {
        // Las losas se montan 3 cm en el borde (< tolerancia 5 cm).
        var s = new Sistema();
        s.Losas.Add(Anclada(1, 0, 0, 4, 3));
        s.Losas.Add(Anclada(2, 3.97, 0, 3, 3));
        Assert.Single(AdyacenciaDetector.Detectar(s));
    }

    [Fact]
    public void Losas_separadas_no_producen_candidato()
    {
        var s = new Sistema();
        s.Losas.Add(Anclada(1, 0, 0, 4, 3));
        s.Losas.Add(Anclada(2, 10, 0, 3, 3));
        Assert.Empty(AdyacenciaDetector.Detectar(s));
    }

    [Fact]
    public void Losas_que_solo_se_tocan_en_una_esquina_no_producen_candidato()
    {
        // Comparten exactamente un punto — no un borde con longitud.
        var s = new Sistema();
        s.Losas.Add(Anclada(1, 0, 0, 4, 3));
        s.Losas.Add(Anclada(2, 4, 3, 3, 3));
        Assert.Empty(AdyacenciaDetector.Detectar(s));
    }

    [Fact]
    public void Un_par_ya_declarado_en_BordesX_no_se_vuelve_a_sugerir()
    {
        var s = new Sistema();
        s.Losas.Add(Anclada(1, 0, 0, 4, 3));
        s.Losas.Add(Anclada(2, 4, 0, 3, 3));
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" });
        Assert.Empty(AdyacenciaDetector.Detectar(s));
    }

    [Fact]
    public void Un_par_ya_declarado_con_BI_BJ_invertidos_tambien_se_filtra()
    {
        var s = new Sistema();
        s.Losas.Add(Anclada(1, 0, 0, 4, 3));
        s.Losas.Add(Anclada(2, 4, 0, 3, 3));
        s.BordesX.Add(new BordeAdic { BI = 2, BJ = 1, Balanceo = "S" });   // sentido inverso
        Assert.Empty(AdyacenciaDetector.Detectar(s));
    }

    [Fact]
    public void Un_par_ya_declarado_en_BordesY_no_se_vuelve_a_sugerir()
    {
        var s = new Sistema();
        s.Losas.Add(Anclada(1, 0, 0, 4, 3));
        s.Losas.Add(Anclada(2, 0, 3, 4, 5));
        s.BordesY.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" });
        Assert.Empty(AdyacenciaDetector.Detectar(s));
    }

    [Fact]
    public void Una_losa_sin_posicion_explicita_se_ignora()
    {
        var s = new Sistema();
        s.Losas.Add(Anclada(1, 0, 0, 4, 3));
        s.Losas.Add(Flotante(2, 3, 3));   // libre (sin anclar) → no se considera
        Assert.Empty(AdyacenciaDetector.Detectar(s));
    }

    [Fact]
    public void Tres_losas_en_fila_producen_dos_candidatos()
    {
        var s = new Sistema();
        s.Losas.Add(Anclada(1, 0, 0, 4, 3));
        s.Losas.Add(Anclada(2, 4, 0, 3, 3));
        s.Losas.Add(Anclada(3, 7, 0, 2, 3));
        Assert.Equal(2, AdyacenciaDetector.Detectar(s).Count);
    }

    [Fact]
    public void El_punto_medio_es_el_del_segmento_solapado_no_el_del_lado()
    {
        // Losa 1 cubre Y ∈ [0, 3]; losa 2 cubre Y ∈ [1, 6]. El borde compartido
        // es el solape Y ∈ [1, 3] → su punto medio es 2, no 1.5 ni 3.5.
        var s = new Sistema();
        s.Losas.Add(Anclada(1, 0, 0, 4, 3));
        s.Losas.Add(Anclada(2, 4, 1, 3, 5));

        var c = Assert.Single(AdyacenciaDetector.Detectar(s));
        Assert.True(c.EsBordeX);
        Assert.Equal(4.0, c.MidX, precision: 6);
        Assert.Equal(2.0, c.MidY, precision: 6);
    }

    [Fact]
    public void Un_sistema_vacio_o_con_una_sola_losa_no_produce_candidatos()
    {
        Assert.Empty(AdyacenciaDetector.Detectar(new Sistema()));

        var s = new Sistema();
        s.Losas.Add(Anclada(1, 0, 0, 4, 3));
        Assert.Empty(AdyacenciaDetector.Detectar(s));
    }
}
