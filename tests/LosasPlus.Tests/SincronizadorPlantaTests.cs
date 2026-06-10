using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del <see cref="SincronizadorPlanta"/>: hornear el layout de
/// <see cref="LayoutSolver"/> en <see cref="Losa.CoordenadaX"/>/<see cref="Losa.CoordenadaY"/>
/// para que el 2D y el 3D dejen de apilar las losas en el origen.
/// </summary>
public class SincronizadorPlantaTests
{
    private static Sistema DosLosasAdyacentesEnX()
    {
        var s = new Sistema();
        s.Losas.Add(new Losa { Id = 1, Lx = 4.0, Ly = 4.0 });   // CoordenadaX/Y = 0 (sin posicionar)
        s.Losas.Add(new Losa { Id = 2, Lx = 4.0, Ly = 4.0 });
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2 });        // losa 2 a la derecha de la 1
        return s;
    }

    [Fact]
    public void RequiereSincronizacion_true_cuando_todas_en_origen()
    {
        Assert.True(SincronizadorPlanta.RequiereSincronizacion(DosLosasAdyacentesEnX()));
    }

    [Fact]
    public void Sincronizar_separa_las_losas_y_no_se_apilan()
    {
        var s = DosLosasAdyacentesEnX();
        bool cambio = SincronizadorPlanta.Sincronizar(s);

        Assert.True(cambio);
        var l1 = s.Losas[0];
        var l2 = s.Losas[1];
        // La losa 2 queda a la derecha de la 1 (a Lx de distancia) → no se solapan.
        Assert.Equal(0.0, l1.CoordenadaX, 6);
        Assert.Equal(4.0, l2.CoordenadaX, 6);
        Assert.NotEqual(
            (l1.CoordenadaX, l1.CoordenadaY),
            (l2.CoordenadaX, l2.CoordenadaY));
    }

    [Fact]
    public void Sincronizar_es_idempotente_y_no_pisa_lo_ya_posicionado()
    {
        var s = DosLosasAdyacentesEnX();
        SincronizadorPlanta.Sincronizar(s);
        // Tras posicionar, ya no requiere sincronización ni cambia.
        Assert.False(SincronizadorPlanta.RequiereSincronizacion(s));
        Assert.False(SincronizadorPlanta.Sincronizar(s));
    }

    [Fact]
    public void Sincronizar_respeta_ancladas_y_coloca_las_libres_relativo_al_ancla()
    {
        // UI1.1: una losa colocada explícitamente (drag/import) queda ANCLADA; la
        // libre apilada en el origen sí se coloca — relativa al ancla (BFS híbrido).
        var s = DosLosasAdyacentesEnX();
        s.Losas[0].CoordenadaX = 12.5;
        s.Losas[0].Anclada = true;

        Assert.True(SincronizadorPlanta.Sincronizar(s));

        Assert.Equal(12.5, s.Losas[0].CoordenadaX, 6);   // el ancla no se toca
        Assert.Equal(16.5, s.Losas[1].CoordenadaX, 6);   // libre → a la derecha del ancla
        Assert.True(s.Losas[1].Anclada);                  // lo horneado queda anclado
    }

    [Fact]
    public void RequiereSincronizacion_ignora_las_ancladas_en_el_origen()
    {
        // Dos losas ancladas deliberadamente en (0,0): el usuario las puso ahí.
        // La guarda nueva mira losas LIBRES en el origen, no coordenadas en cero.
        var s = DosLosasAdyacentesEnX();
        s.Losas[0].Anclada = true;
        s.Losas[1].Anclada = true;

        Assert.False(SincronizadorPlanta.RequiereSincronizacion(s));
    }

    [Fact]
    public void RequiereSincronizacion_detecta_losa_libre_en_origen_entre_ancladas()
    {
        // Import DXF escribe coordenadas reales (ancladas); una losa creada en el
        // Editor-grid queda libre en (0,0). Con la guarda vieja ("todas en 0,0")
        // jamás se colocaría — quedaría apilada en el origen para siempre.
        var s = DosLosasAdyacentesEnX();
        s.Losas[0].CoordenadaX = 5.0;
        s.Losas[0].CoordenadaY = 2.0;
        s.Losas[0].Anclada = true;

        Assert.True(SincronizadorPlanta.RequiereSincronizacion(s));
    }

    [Fact]
    public void Sincronizar_ancla_lo_que_hornea()
    {
        // Bake-once: tras hornear el layout, TODAS las posiciones quedan explícitas
        // — así un ancla posterior no "teletransporta" a las demás (no hay re-BFS).
        var s = DosLosasAdyacentesEnX();

        Assert.True(SincronizadorPlanta.Sincronizar(s));

        Assert.True(s.Losas[0].Anclada);
        Assert.True(s.Losas[1].Anclada);
    }

    [Fact]
    public void Sincronizar_sin_losas_o_una_sola_no_hace_nada()
    {
        Assert.False(SincronizadorPlanta.Sincronizar(new Sistema()));
        var una = new Sistema();
        una.Losas.Add(new Losa { Id = 1 });
        Assert.False(SincronizadorPlanta.RequiereSincronizacion(una));
    }

    [Fact]
    public void SincronizarColumnas_distribuye_en_grilla_sin_apilar()
    {
        var nivel = new Nivel();
        for (int i = 1; i <= 4; i++) nivel.Columnas.Add(new Columna { Id = i, Nombre = $"C-{i}" });

        bool cambio = SincronizadorPlanta.SincronizarColumnas(nivel);

        Assert.True(cambio);
        // 4 columnas → grilla 2×2, separación 5 m → posiciones distintas.
        var posiciones = nivel.Columnas
            .Select(c => (c.CoordenadaX, c.CoordenadaY))
            .Distinct()
            .Count();
        Assert.Equal(4, posiciones);   // ninguna se apila sobre otra
    }

    [Fact]
    public void SincronizarColumnas_respeta_posiciones_manuales()
    {
        var nivel = new Nivel();
        nivel.Columnas.Add(new Columna { Id = 1, CoordenadaX = 3.0 });
        nivel.Columnas.Add(new Columna { Id = 2 });
        // Hay una posicionada → no se reposiciona automáticamente.
        Assert.False(SincronizadorPlanta.SincronizarColumnas(nivel));
        Assert.Equal(3.0, nivel.Columnas[0].CoordenadaX, 6);
    }

    [Fact]
    public void SincronizarEdificio_posiciona_losas_y_columnas()
    {
        var edificio = new Edificio();
        var nivel = new Nivel();
        nivel.Sistemas.Add(DosLosasAdyacentesEnX());
        nivel.Columnas.Add(new Columna { Id = 1 });
        nivel.Columnas.Add(new Columna { Id = 2 });
        edificio.Niveles.Add(nivel);

        SincronizadorPlanta.SincronizarEdificio(edificio);

        Assert.Equal(4.0, nivel.Sistemas[0].Losas[1].CoordenadaX, 6);   // losa separada
        Assert.NotEqual(
            (nivel.Columnas[0].CoordenadaX, nivel.Columnas[0].CoordenadaY),
            (nivel.Columnas[1].CoordenadaX, nivel.Columnas[1].CoordenadaY));  // columnas separadas
    }
}
