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
    public void Exporta_la_georreferencia_cuando_el_proyecto_esta_ubicado()
    {
        // El motor ignora claves desconocidas (contrato.py usa d.get), así que
        // el bloque viaja como metadata para los consumidores geo (fases M/N).
        var geo = new Georreferencia
        {
            Latitud = 18.47, Longitud = -69.94, Elevacion = 25.0, RotacionNorte = 15.0,
        };

        string json = ExportadorModeloMotor.ToJson(
            ExportadorModeloMotor.Exportar(PorticoConZapatas(), geo));

        Assert.Contains("\"georreferencia\"", json);
        Assert.Contains("\"latitud\": 18.47", json);
        Assert.Contains("\"longitud\": -69.94", json);
        Assert.Contains("\"rotacion_norte\": 15", json);
        Assert.Contains("\"epsg\": 4326", json);
    }

    [Fact]
    public void Sin_georreferencia_el_json_no_lleva_la_clave()
    {
        // Retrocompatibilidad: el JSON de un proyecto no ubicado es idéntico
        // al de antes de la K.6.
        string json = ExportadorModeloMotor.ToJson(ExportadorModeloMotor.Exportar(PorticoConZapatas()));
        Assert.DoesNotContain("georreferencia", json);
    }

    [Fact]
    public void Exporta_losas_con_4_esquinas_a_la_cota()
    {
        var nivel = new Nivel { Cota = 3.0 };
        var sis = new Sistema { Fc = 0.210, Fy = 4.200 };
        sis.Losas.Add(new Losa { CoordenadaX = 1, CoordenadaY = 2, Lx = 4, Ly = 5, Espesor = 0.12 });
        nivel.Sistemas.Add(sis);
        // columnas con zapata → el modelo es válido (tiene apoyos) y exportable
        foreach (var (x, y) in new[] { (0.0, 0.0), (4.0, 0.0) })
            nivel.Columnas.Add(new Columna { CoordenadaX = x, CoordenadaY = y, Base = 0.30, Peralte = 0.30, Altura = 3.0, Zapata = new Zapata() });
        var ed = new Edificio();
        ed.Niveles.Add(nivel);

        var m = ExportadorModeloMotor.Exportar(ed);

        Assert.Single(m.Losas);
        var p = m.Losas[0].Puntos;
        Assert.Equal(4, p.Length);
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, p[0]);   // (X, Y, cota)
        Assert.Equal(new[] { 5.0, 2.0, 3.0 }, p[1]);   // (X+Lx, Y, cota)
        Assert.Equal(new[] { 5.0, 7.0, 3.0 }, p[2]);   // (X+Lx, Y+Ly, cota)
        Assert.Equal(new[] { 1.0, 7.0, 3.0 }, p[3]);   // (X, Y+Ly, cota)
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
