using System.Linq;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

public class ExportadorModeloMotorTests
{
    private static Edificio PorticoConZapatas()
    {
        var nivel = new Nivel { Cota = 0.0 };
        nivel.Sistemas.Add(new Sistema { Fc = 0.210, Fy = 4.200 });
        (double x, double y)[] esq = { (0, 0), (5, 0), (5, 5), (0, 5) };
        foreach (var (x, y) in esq)
            nivel.Columnas.Add(new Columna
            {
                CoordenadaX = x, CoordenadaY = y, Base = 0.30, Peralte = 0.30, Altura = 3.0,
                Zapata = new Zapata(),
            });
        (double ox, double oy, double ang)[] vigas = { (0, 0, 0), (5, 0, 90), (5, 5, 180), (0, 5, 270) };
        foreach (var (ox, oy, ang) in vigas)
        {
            var v = new Viga { OrigenX = ox, OrigenY = oy, AnguloGrados = ang };
            v.Tramos.Add(new TramoViga { Longitud = 5, Base = 0.30, Peralte = 0.50 });
            nivel.Vigas.Add(v);
        }
        var ed = new Edificio();
        ed.Niveles.Add(nivel);
        return ed;
    }

    [Fact]
    public void Exporta_un_modelo_valido_con_apoyos_y_sin_cargas()
    {
        var m = ExportadorModeloMotor.Exportar(PorticoConZapatas());

        Assert.Equal(8, m.Nodos.Count);
        Assert.Equal(8, m.Elementos.Count);
        Assert.Single(m.Materiales);
        Assert.Equal(2, m.Secciones.Count);
        Assert.Equal(4, m.Apoyos.Count);
        Assert.All(m.Apoyos, a => Assert.True(a.Ux && a.Uy && a.Uz && a.Rx && a.Ry && a.Rz));
        Assert.Empty(m.Cargas);
        Assert.InRange(m.Materiales[0].E, 2.0e10, 2.3e10);
        Assert.Empty(ExportadorModeloMotor.ValidarIntegridad(m));
        Assert.Equal(4, m.Elementos.Count(e => e.VectorReferencia[0] == 1.0));
    }

    [Fact]
    public void Sin_portico_lanza_excepcion()
    {
        var vacio = new Edificio();
        vacio.Niveles.Add(new Nivel { Cota = 0.0 });
        Assert.Throws<ExportadorModeloException>(() => ExportadorModeloMotor.Exportar(vacio));
    }

    [Fact]
    public void ToJson_usa_las_claves_del_contrato()
    {
        string json = ExportadorModeloMotor.ToJson(ExportadorModeloMotor.Exportar(PorticoConZapatas()));
        Assert.Contains("\"nodo_i\"", json);
        Assert.Contains("\"constante_torsion\"", json);
        Assert.Contains("\"vector_referencia\"", json);
    }

    [Fact]
    public void Modelo_sin_apoyos_lanza_excepcion()
    {
        var nivel = new Nivel { Cota = 0.0 };
        nivel.Sistemas.Add(new Sistema { Fc = 0.210, Fy = 4.200 });
        var v = new Viga { OrigenX = 0, OrigenY = 0, AnguloGrados = 0 };
        v.Tramos.Add(new TramoViga { Longitud = 5, Base = 0.30, Peralte = 0.50 });
        nivel.Vigas.Add(v); // viga pero ninguna columna → sin apoyos
        var ed = new Edificio();
        ed.Niveles.Add(nivel);

        Assert.Throws<ExportadorModeloException>(() => ExportadorModeloMotor.Exportar(ed));
    }
}
