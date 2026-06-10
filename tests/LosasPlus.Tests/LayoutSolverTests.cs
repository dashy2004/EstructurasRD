using System.Linq;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del LayoutSolver: dada una topología I-J, verifica que la BFS produce
/// posiciones consistentes con la convención (J va a la derecha en X, abajo en Y).
/// </summary>
public class LayoutSolverTests
{
    private static Losa L(int id, double lx, double ly) =>
        new Losa { Id = id, Tipo = 10, Carga = 2.0, Espesor = 0.12, Lx = lx, Ly = ly, Rec = 0.02 };

    [Fact]
    public void Sistema_vacio_no_produce_placements()
    {
        var s = new Sistema();
        var r = LayoutSolver.Solve(s);
        Assert.Empty(r.Placements);
    }

    [Fact]
    public void Una_sola_losa_se_coloca_en_origen()
    {
        var s = new Sistema();
        s.Losas.Add(L(1, 4, 3));
        var r = LayoutSolver.Solve(s);
        Assert.Single(r.Placements);
        Assert.Equal(0, r.Placements[0].X);
        Assert.Equal(0, r.Placements[0].Y);
        Assert.Equal(4, r.Width);
        Assert.Equal(3, r.Height);
    }

    [Fact]
    public void Dos_losas_borde_X_se_alinean_horizontalmente()
    {
        // Losa 1 (Lx=4) con Losa 2 a su derecha (Lx=3, Ly=3)
        var s = new Sistema();
        s.Losas.Add(L(1, 4, 3));
        s.Losas.Add(L(2, 3, 3));
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" });

        var r = LayoutSolver.Solve(s);
        var p1 = r.Placements.First(p => p.Id == 1);
        var p2 = r.Placements.First(p => p.Id == 2);

        Assert.Equal(0, p1.X);
        Assert.Equal(0, p1.Y);
        Assert.Equal(4, p2.X);          // p2 a la derecha de p1
        Assert.Equal(0, p2.Y);          // misma fila
        Assert.Equal(7, r.Width);       // 4 + 3
        Assert.Equal(3, r.Height);
    }

    [Fact]
    public void Dos_losas_borde_Y_se_apilan_verticalmente()
    {
        var s = new Sistema();
        s.Losas.Add(L(1, 4, 3));
        s.Losas.Add(L(2, 4, 5));
        s.BordesY.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" });

        var r = LayoutSolver.Solve(s);
        var p1 = r.Placements.First(p => p.Id == 1);
        var p2 = r.Placements.First(p => p.Id == 2);

        Assert.Equal(0, p1.X);
        Assert.Equal(0, p1.Y);
        Assert.Equal(0, p2.X);
        Assert.Equal(3, p2.Y);          // debajo de p1 (Ly=3)
        Assert.Equal(4, r.Width);
        Assert.Equal(8, r.Height);      // 3 + 5
    }

    [Fact]
    public void Grilla_2x2_con_4_bordes_compone_correctamente()
    {
        // 1 - 2
        // |   |
        // 3 - 4
        var s = new Sistema();
        s.Losas.Add(L(1, 4, 3));
        s.Losas.Add(L(2, 5, 3));
        s.Losas.Add(L(3, 4, 4));
        s.Losas.Add(L(4, 5, 4));
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" });
        s.BordesX.Add(new BordeAdic { BI = 3, BJ = 4, Balanceo = "S" });
        s.BordesY.Add(new BordeAdic { BI = 1, BJ = 3, Balanceo = "S" });
        s.BordesY.Add(new BordeAdic { BI = 2, BJ = 4, Balanceo = "S" });

        var r = LayoutSolver.Solve(s);
        var p = r.Placements.ToDictionary(x => x.Id);

        Assert.Equal((0.0, 0.0), (p[1].X, p[1].Y));
        Assert.Equal((4.0, 0.0), (p[2].X, p[2].Y));    // a la derecha de 1
        Assert.Equal((0.0, 3.0), (p[3].X, p[3].Y));    // abajo de 1
        Assert.Equal((4.0, 3.0), (p[4].X, p[4].Y));    // diagonal — primer match en BFS gana
        Assert.Equal(9, r.Width);
        Assert.Equal(7, r.Height);
        Assert.All(r.Placements, x => Assert.False(x.Huerfana));
    }

    [Fact]
    public void Losas_desconectadas_se_marcan_huerfanas()
    {
        // 1 - 2  (cluster) ; 3 (orphan)
        var s = new Sistema();
        s.Losas.Add(L(1, 4, 3));
        s.Losas.Add(L(2, 4, 3));
        s.Losas.Add(L(3, 2, 2));
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" });

        var r = LayoutSolver.Solve(s);
        var p = r.Placements.ToDictionary(x => x.Id);
        Assert.False(p[1].Huerfana);
        Assert.False(p[2].Huerfana);
        Assert.True(p[3].Huerfana);
        // huérfana queda debajo del cluster con al menos 1m de separación
        Assert.True(p[3].Y >= 4.0);  // bbox del cluster es [0,3], + 1m padding = 4
    }

    [Fact]
    public void Losas_inexistentes_referenciadas_en_borde_se_ignoran_sin_excepcion()
    {
        var s = new Sistema();
        s.Losas.Add(L(1, 4, 3));
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 99, Balanceo = "S" });  // 99 no existe

        var r = LayoutSolver.Solve(s);
        Assert.Single(r.Placements);
        Assert.Equal(0, r.Placements[0].X);
    }

    [Fact]
    public void Normalizacion_garantiza_minimo_xy_en_cero()
    {
        // Borde "inverso": losa 2 declara que 1 está a su derecha → 1 debería quedar en 0,0
        // y 2 detrás (-Lx_1). Tras normalización, 2.X debe ser 0 y 1.X positivo.
        var s = new Sistema();
        s.Losas.Add(L(1, 4, 3));
        s.Losas.Add(L(2, 3, 3));
        // Borde con I=2, J=1 → 1 a la derecha de 2
        s.BordesX.Add(new BordeAdic { BI = 2, BJ = 1, Balanceo = "S" });

        var r = LayoutSolver.Solve(s);
        Assert.Equal(0, r.MinX);
        Assert.Equal(0, r.MinY);
        Assert.All(r.Placements, p => Assert.True(p.X >= 0 && p.Y >= 0));
    }

    // =================================================================
    // MODO HÍBRIDO (Fase 2) — losas ancladas (Anclada) vs flotantes
    // =================================================================

    [Fact]
    public void Losa_anclada_conserva_sus_coordenadas_exactas()
    {
        // Una losa anclada NO se recalcula topológicamente: su Placement debe
        // usar sus CoordenadaX/Y tal cual.
        var s = new Sistema();
        var losa = L(1, 4, 3);
        losa.CoordenadaX = 12.5;
        losa.CoordenadaY = 7.25;
        losa.Anclada = true;
        s.Losas.Add(losa);

        var r = LayoutSolver.Solve(s);
        var p = r.Placements.Single();
        Assert.Equal(12.5, p.X, precision: 6);
        Assert.Equal(7.25, p.Y, precision: 6);
    }

    [Fact]
    public void Con_losa_anclada_no_se_normaliza_a_cero()
    {
        // El comportamiento legacy normaliza minX/minY a 0; en modo anclado
        // se preservan las coordenadas absolutas del lienzo CAD.
        var s = new Sistema();
        var losa = L(1, 5, 5);
        losa.CoordenadaX = 30.0;
        losa.CoordenadaY = 20.0;
        losa.Anclada = true;
        s.Losas.Add(losa);

        var r = LayoutSolver.Solve(s);
        Assert.Equal(30.0, r.MinX, precision: 6);
        Assert.Equal(20.0, r.MinY, precision: 6);
    }

    [Fact]
    public void Losa_flotante_se_sigue_infiriendo_topologicamente()
    {
        // Sin losas ancladas el comportamiento legacy se mantiene intacto.
        var s = new Sistema();
        s.Losas.Add(L(1, 4, 3));
        s.Losas.Add(L(2, 3, 3));
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" });

        var r = LayoutSolver.Solve(s);
        var p1 = r.Placements.First(p => p.Id == 1);
        var p2 = r.Placements.First(p => p.Id == 2);
        Assert.Equal(0, p1.X);
        Assert.Equal(4, p2.X);   // inferida a la derecha de p1
        Assert.Equal(0, r.MinX); // sin ancladas → se normaliza
    }

    [Fact]
    public void Mezcla_anclada_y_flotante_vecina_se_infiere_relativo_al_ancla()
    {
        // Losa 1 anclada en (10, 10). Losa 2 flotante, vecina X de la 1.
        // La 2 debe quedar a la derecha de la 1 — relativa al ancla, sin normalizar.
        var s = new Sistema();
        var l1 = L(1, 4, 3);
        l1.CoordenadaX = 10.0;
        l1.CoordenadaY = 10.0;
        l1.Anclada = true;
        var l2 = L(2, 3, 3);   // flotante (libre, sin anclar)
        s.Losas.Add(l1);
        s.Losas.Add(l2);
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" });

        var r = LayoutSolver.Solve(s);
        var p1 = r.Placements.First(p => p.Id == 1);
        var p2 = r.Placements.First(p => p.Id == 2);
        Assert.Equal(10.0, p1.X, precision: 6);   // ancla intacta
        Assert.Equal(10.0, p1.Y, precision: 6);
        Assert.Equal(14.0, p2.X, precision: 6);   // 10 + Lx(4) a la derecha
        Assert.Equal(10.0, p2.Y, precision: 6);
    }
}
