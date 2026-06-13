using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Services;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de <see cref="EncuadrePlanta"/> — la matemática pura del encuadre de
/// Planta 2D (UI1.7): extents del contenido y transform de ajuste (fit).
/// </summary>
public class EncuadrePlantaTests
{
    // ---- Helpers de construcción ----

    private static Nivel NivelVacio() => new();

    private static Nivel NivelConLosa(double x, double y, double lx, double ly)
    {
        var nivel = new Nivel();
        var sistema = new Sistema();
        sistema.Losas.Add(new Losa { CoordenadaX = x, CoordenadaY = y, Lx = lx, Ly = ly });
        nivel.Sistemas.Add(sistema);
        return nivel;
    }

    // ---- CalcularExtents: estructura ----

    [Fact]
    public void Extents_null_sin_contenido()
    {
        Assert.Null(EncuadrePlanta.CalcularExtents(null, null, null, incluirUnderlays: true));
        Assert.Null(EncuadrePlanta.CalcularExtents(NivelVacio(), null, null, incluirUnderlays: true));
    }

    [Fact]
    public void Extents_de_una_losa()
    {
        var r = EncuadrePlanta.CalcularExtents(NivelConLosa(2, 3, 4, 5), null, null, false);

        Assert.Equal(new RectM(2, 3, 4, 5), r);
    }

    [Fact]
    public void Extents_de_columnas_usa_sus_centros()
    {
        var nivel = new Nivel();
        nivel.Columnas.Add(new Columna { CoordenadaX = 1, CoordenadaY = 2 });
        nivel.Columnas.Add(new Columna { CoordenadaX = 9, CoordenadaY = 7 });

        var r = EncuadrePlanta.CalcularExtents(nivel, null, null, false);

        Assert.Equal(new RectM(1, 2, 8, 5), r);
    }

    [Fact]
    public void Extents_de_viga_cubre_origen_y_extremo()
    {
        var nivel = new Nivel();
        // Viga con un tramo de longitud 5 (default de TramoViga), ángulo 0
        // ⇒ ExtremoX = OrigenX + 5·cos(0) = 1+5 = 6, ExtremoY = 2.
        var viga = new Viga { OrigenX = 1, OrigenY = 2, AnguloGrados = 0 };
        viga.Tramos.Add(new TramoViga { Longitud = 5 });
        nivel.Vigas.Add(viga);

        var r = EncuadrePlanta.CalcularExtents(nivel, null, null, false);

        Assert.Equal(new RectM(1, 2, 5, 0), r);
    }

    [Fact]
    public void Extents_de_muros_usa_sus_dos_extremos()
    {
        var nivel = new Nivel();
        var sistema = new Sistema();
        sistema.Muros.Add(new Muro { PuntoInicio = new PuntoCad(0, 1), PuntoFin = new PuntoCad(6, 4) });
        nivel.Sistemas.Add(sistema);

        var r = EncuadrePlanta.CalcularExtents(nivel, null, null, false);

        Assert.Equal(new RectM(0, 1, 6, 3), r);
    }

    [Fact]
    public void Extents_une_estructura_de_varios_sistemas()
    {
        var nivel = NivelConLosa(0, 0, 2, 2);
        var otro = new Sistema();
        otro.Losas.Add(new Losa { CoordenadaX = 10, CoordenadaY = 10, Lx = 2, Ly = 2 });
        nivel.Sistemas.Add(otro);

        var r = EncuadrePlanta.CalcularExtents(nivel, null, null, false);

        Assert.Equal(new RectM(0, 0, 12, 12), r);
    }

    // ---- CalcularExtents: underlays ----

    [Fact]
    public void Extents_con_underlays_incluye_el_pdf()
    {
        var pdf = new PdfReferencia { Ancho = 0.841, Alto = 0.594, Escala = 100, OffsetX = 5, OffsetY = 10 };

        var r = EncuadrePlanta.CalcularExtents(null, null, pdf, incluirUnderlays: true);

        Assert.NotNull(r);
        Assert.Equal(5.0,  r!.Value.X,     precision: 9);
        Assert.Equal(10.0, r!.Value.Y,     precision: 9);
        Assert.Equal(84.1, r!.Value.Ancho, precision: 9);
        Assert.Equal(59.4, r!.Value.Alto,  precision: 9);
    }

    [Fact]
    public void Extents_con_underlays_incluye_el_dxf_con_flip_y()
    {
        // x ∈ OffsetX + [MinX, MaxX]·Escala; y ∈ [OffsetY, OffsetY + Alto·Escala] (flip-Y).
        var plano = new PlanoReferencia
        {
            MinX = 2, MinY = 1, MaxX = 12, MaxY = 6,
            Escala = 2, OffsetX = 10, OffsetY = 100,
        };

        var r = EncuadrePlanta.CalcularExtents(null, plano, null, incluirUnderlays: true);

        Assert.Equal(new RectM(14, 100, 20, 10), r);
    }

    [Fact]
    public void Extents_sin_underlays_ignora_plano_y_pdf()
    {
        var plano = new PlanoReferencia { MinX = 0, MinY = 0, MaxX = 50, MaxY = 50 };
        var pdf = new PdfReferencia { Ancho = 1, Alto = 1 };

        Assert.Null(EncuadrePlanta.CalcularExtents(null, plano, pdf, incluirUnderlays: false));

        var r = EncuadrePlanta.CalcularExtents(NivelConLosa(0, 0, 4, 4), plano, pdf, false);
        Assert.Equal(new RectM(0, 0, 4, 4), r);
    }

    [Fact]
    public void Extents_ignora_underlays_degenerados()
    {
        // Plano sin extensión (Min == Max) y PDF sin dimensiones no aportan nada.
        var plano = new PlanoReferencia { MinX = 0, MinY = 0, MaxX = 0, MaxY = 0 };
        var pdf = new PdfReferencia { Ancho = 0, Alto = 0 };

        Assert.Null(EncuadrePlanta.CalcularExtents(null, plano, pdf, incluirUnderlays: true));
    }

    // ---- CalcularEncuadre (fit) ----

    [Fact]
    public void Encuadre_escala_por_el_eje_limitante_con_margen()
    {
        // 1200×800, rect 20×10, margen 5% c/lado ⇒ min(1200·0.9/20, 800·0.9/10) = 54.
        var (escala, tx, ty) = EncuadrePlanta.CalcularEncuadre(new RectM(0, 0, 20, 10), 1200, 800);

        Assert.Equal(54.0, escala, precision: 9);
        Assert.Equal(600 - 10 * 54.0, tx, precision: 9);   // centra cx = 10
        Assert.Equal(400 - 5 * 54.0, ty, precision: 9);    // centra cy = 5
    }

    [Fact]
    public void Encuadre_clampa_la_escala_a_200()
    {
        var (escala, tx, ty) = EncuadrePlanta.CalcularEncuadre(new RectM(0, 0, 1, 1), 1200, 800);

        Assert.Equal(EncuadrePlanta.EscalaMax, escala);
        Assert.Equal(600 - 0.5 * 200.0, tx, precision: 9);
        Assert.Equal(400 - 0.5 * 200.0, ty, precision: 9);
    }

    [Fact]
    public void Encuadre_degenerado_total_usa_escala_default_centrada()
    {
        // Una sola columna: rect 0×0 en (5, 7) ⇒ escala 40 y el punto al centro.
        var (escala, tx, ty) = EncuadrePlanta.CalcularEncuadre(new RectM(5, 7, 0, 0), 1200, 800);

        Assert.Equal(EncuadrePlanta.EscalaFallback, escala);
        Assert.Equal(600 - 5 * 40.0, tx, precision: 9);
        Assert.Equal(400 - 7 * 40.0, ty, precision: 9);
    }

    [Fact]
    public void Encuadre_con_un_solo_eje_degenerado_manda_el_otro()
    {
        // Fila horizontal de columnas: alto 0 ⇒ manda el ancho (1200·0.9/10 = 108).
        var (escala, _, ty) = EncuadrePlanta.CalcularEncuadre(new RectM(0, 0, 10, 0), 1200, 800);

        Assert.Equal(108.0, escala, precision: 9);
        Assert.Equal(400.0, ty, precision: 9);   // cy = 0 ⇒ ty = altoPx/2
    }
}
