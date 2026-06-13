using System.Linq;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del servicio puro de geometría de bordes de continuidad (UI1.8).
/// </summary>
public class BordesPlantaServiceTests
{
    private static Losa L(int id, double x, double y, double lx = 4, double ly = 3, int tipo = 0)
        => new Losa { Id = id, CoordenadaX = x, CoordenadaY = y, Lx = lx, Ly = ly, Tipo = tipo };

    [Fact]
    public void EjeInferido_lado_a_lado_horizontal_es_X()
    {
        var a = L(1, 0, 0);
        var b = L(2, 4, 0);                       // a la derecha de a
        Assert.Equal(EjeBorde.X, BordesPlantaService.EjeInferido(a, b));
    }

    [Fact]
    public void EjeInferido_apiladas_vertical_es_Y()
    {
        var a = L(1, 0, 0);
        var b = L(2, 0, 3);                        // encima/debajo de a
        Assert.Equal(EjeBorde.Y, BordesPlantaService.EjeInferido(a, b));
    }

    [Fact]
    public void EjeInferido_empate_resuelve_a_X()
    {
        var a = L(1, 0, 0, lx: 2, ly: 2);
        var b = L(2, 2, 2, lx: 2, ly: 2);         // |Δx| == |Δy|
        Assert.Equal(EjeBorde.X, BordesPlantaService.EjeInferido(a, b));
    }

    [Fact]
    public void SegmentoCompartido_contacto_pleno_horizontal_devuelve_cara_vertical_eje_X()
    {
        var a = L(1, 0, 0, lx: 4, ly: 3);
        var b = L(2, 4, 0, lx: 4, ly: 3);         // b pegada a la derecha de a
        var seg = BordesPlantaService.SegmentoCompartido(a, b);
        Assert.NotNull(seg);
        Assert.Equal(EjeBorde.X, seg!.Value.Eje);
        Assert.Equal(4, seg.Value.X0, 3);          // cara en x = 4
        Assert.Equal(4, seg.Value.X1, 3);
        Assert.Equal(0, seg.Value.Y0, 3);          // solape y = [0,3]
        Assert.Equal(3, seg.Value.Y1, 3);
    }

    [Fact]
    public void SegmentoCompartido_contacto_pleno_vertical_devuelve_cara_horizontal_eje_Y()
    {
        var a = L(1, 0, 0, lx: 4, ly: 3);
        var b = L(2, 0, 3, lx: 4, ly: 3);         // b pegada encima de a
        var seg = BordesPlantaService.SegmentoCompartido(a, b);
        Assert.NotNull(seg);
        Assert.Equal(EjeBorde.Y, seg!.Value.Eje);
        Assert.Equal(3, seg.Value.Y0, 3);          // cara en y = 3
        Assert.Equal(3, seg.Value.Y1, 3);
    }

    [Fact]
    public void SegmentoCompartido_contacto_parcial_recorta_el_solape()
    {
        var a = L(1, 0, 0, lx: 4, ly: 4);
        var b = L(2, 4, 2, lx: 4, ly: 4);         // desfase vertical: solape y = [2,4]
        var seg = BordesPlantaService.SegmentoCompartido(a, b);
        Assert.NotNull(seg);
        Assert.Equal(EjeBorde.X, seg!.Value.Eje);
        Assert.Equal(2, seg.Value.Y0, 3);
        Assert.Equal(4, seg.Value.Y1, 3);
    }

    [Fact]
    public void SegmentoCompartido_con_holgura_mayor_que_tol_devuelve_null()
    {
        var a = L(1, 0, 0, lx: 4, ly: 3);
        var b = L(2, 5, 0, lx: 4, ly: 3);         // 1 m de hueco
        Assert.Null(BordesPlantaService.SegmentoCompartido(a, b));
    }

    [Fact]
    public void SegmentoCompartido_disjuntas_devuelve_null()
    {
        var a = L(1, 0, 0, lx: 4, ly: 3);
        var b = L(2, 20, 20, lx: 4, ly: 3);
        Assert.Null(BordesPlantaService.SegmentoCompartido(a, b));
    }

    [Fact]
    public void HachuraAristas_tipo_del_catalogo_mapea_NESW_y_geometria()
    {
        var kvp = TipoLosa.Catalogo.First();       // un tipo válido cualquiera, sin hardcodear el patrón
        var losa = L(1, 1, 2, lx: 4, ly: 3, tipo: kvp.Key);
        var esperado = kvp.Value.Bordes;           // [N,E,S,W]

        var aristas = BordesPlantaService.HachuraAristas(losa);

        Assert.Equal(4, aristas.Count);
        Assert.Equal(esperado[0], aristas[0].Kind); // N
        Assert.Equal(esperado[1], aristas[1].Kind); // E
        Assert.Equal(esperado[2], aristas[2].Kind); // S
        Assert.Equal(esperado[3], aristas[3].Kind); // W
        // N = arista superior (1,2)-(5,2)
        Assert.Equal(1, aristas[0].X0, 3); Assert.Equal(2, aristas[0].Y0, 3);
        Assert.Equal(5, aristas[0].X1, 3); Assert.Equal(2, aristas[0].Y1, 3);
        // W = arista izquierda (1,2)-(1,5)
        Assert.Equal(1, aristas[3].X0, 3); Assert.Equal(2, aristas[3].Y0, 3);
        Assert.Equal(1, aristas[3].X1, 3); Assert.Equal(5, aristas[3].Y1, 3);
    }

    [Fact]
    public void HachuraAristas_tipo_fuera_del_catalogo_degrada_a_4_apoyado()
    {
        int invalido = Enumerable.Range(1, 100000)
            .First(c => !TipoLosa.Catalogo.ContainsKey(TipoLosa.NormalizarCodigo(c)));
        var losa = L(1, 0, 0, tipo: invalido);

        var aristas = BordesPlantaService.HachuraAristas(losa);

        Assert.All(aristas, ar => Assert.Equal(BorderKind.Apoyado, ar.Kind));
    }

    private static Sistema SistemaDosLosasConBordeX()
    {
        var s = new Sistema();
        s.Losas.Add(L(1, 0, 0, lx: 4, ly: 3));
        s.Losas.Add(L(2, 4, 0, lx: 4, ly: 3));     // pegada a la derecha
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" });
        return s;
    }

    [Fact]
    public void HitTestBorde_click_sobre_la_cara_compartida_devuelve_el_borde()
    {
        var s = SistemaDosLosasConBordeX();
        var hit = BordesPlantaService.HitTestBorde(4.0, 1.5, s, tol: 0.2);  // sobre x=4, dentro del solape
        Assert.NotNull(hit);
        Assert.Equal(EjeBorde.X, hit!.Value.Eje);
        Assert.Same(s.BordesX[0], hit.Value.Borde);
    }

    [Fact]
    public void HitTestBorde_click_lejano_devuelve_null()
    {
        var s = SistemaDosLosasConBordeX();
        Assert.Null(BordesPlantaService.HitTestBorde(1.0, 1.5, s, tol: 0.2));
    }

    [Fact]
    public void HitTestBorde_borde_con_id_inexistente_se_ignora()
    {
        var s = new Sistema();
        s.Losas.Add(L(1, 0, 0));
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 99, Balanceo = "S" });  // 99 no existe
        Assert.Null(BordesPlantaService.HitTestBorde(4.0, 1.5, s, tol: 0.5));
    }

    [Fact]
    public void HitTestBorde_sistema_null_devuelve_null()
    {
        Assert.Null(BordesPlantaService.HitTestBorde(0, 0, null!, tol: 0.2));
    }
}
