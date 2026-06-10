using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using LosasPlus.Models;
using LosasPlus.Transmision;
using LosasPlus.ViewModels;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del ViewModel «Bajada de cargas» (Fase J.4): carga las filas del
/// edificio activo, expone la carga en base y recalcula la zapata al cambiar la
/// presión admisible.
/// </summary>
public class BajadaCargasViewModelTests
{
    private static Edificio EdificioDosNiveles()
    {
        var ed = new Edificio();
        foreach (var cota in new[] { 0.0, 3.0 })
        {
            var n = new Nivel { Cota = cota };
            var s = new Sistema();
            s.Losas.Add(new Losa { Lx = 10, Ly = 10, Carga = 1.5 }); // 150 por nivel
            n.Sistemas.Add(s);
            ed.Niveles.Add(n);
        }
        return ed;
    }

    [Fact]
    public void Carga_filas_y_carga_en_base_del_edificio()
    {
        var vm = new BajadaCargasViewModel(() => EdificioDosNiveles());
        Assert.Equal(2, vm.Filas.Count);
        Assert.Equal(300, vm.CargaEnBase, 6);
    }

    [Fact]
    public void Presion_admisible_recalcula_la_zapata()
    {
        var vm = new BajadaCargasViewModel(() => EdificioDosNiveles())
        {
            PresionAdmisible = 15 // 300 / 15 = 20 m²
        };
        Assert.Equal(20, vm.AreaZapata, 6);

        vm.PresionAdmisible = 30; // 300 / 30 = 10 m²
        Assert.Equal(10, vm.AreaZapata, 6);
    }

    [Fact]
    public void Recalcular_refleja_cambios_del_edificio()
    {
        var ed = EdificioDosNiveles();
        var vm = new BajadaCargasViewModel(() => ed);
        Assert.Equal(2, vm.Filas.Count);

        var n = new Nivel { Cota = 6 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 10, Ly = 10, Carga = 1.5 });
        n.Sistemas.Add(s);
        ed.Niveles.Add(n);

        vm.Recalcular();
        Assert.Equal(3, vm.Filas.Count);
        Assert.Equal(450, vm.CargaEnBase, 6);
    }

    [Fact]
    public void ExportarXlsx_genera_el_libro_de_bajada_de_cargas()
    {
        var vm = new BajadaCargasViewModel(() => EdificioDosNiveles());
        var path = Path.Combine(Path.GetTempPath(), $"bajada_vm_{Guid.NewGuid():N}.xlsx");
        try
        {
            vm.ExportarXlsx(path);
            Assert.True(File.Exists(path));
            using var wb = new XLWorkbook(path);
            Assert.Contains(wb.Worksheets, w => w.Name == BajadaCargasExporter.NombreHoja);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void PredimensionarZapatas_dimensiona_las_zapatas_de_las_columnas()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 10, Ly = 10, Carga = 1.5 }); // base 150
        nivel.Sistemas.Add(s);
        nivel.Columnas.Add(new Columna { Nombre = "C-1" });
        nivel.Columnas.Add(new Columna { Nombre = "C-2" });
        nivel.Columnas.Add(new Columna { Nombre = "C-3" });
        ed.Niveles.Add(nivel);

        var vm = new BajadaCargasViewModel(() => ed) { PresionAdmisible = 15 };
        Assert.Equal(150, vm.CargaEnBase, 6);

        vm.PredimensionarZapatas();

        double lado = Math.Sqrt(50.0 / 15.0); // 150/3 = 50 t/columna → área 50/15
        Assert.All(nivel.Columnas, c =>
        {
            Assert.NotNull(c.Zapata);
            Assert.Equal(lado, c.Zapata!.Ancho, 4);
        });
        Assert.Contains("3 columna", vm.ResumenZapatas);
    }

    [Fact]
    public void PredimensionarZapatas_sin_columnas_informa()
    {
        var vm = new BajadaCargasViewModel(() => EdificioDosNiveles());
        vm.PredimensionarZapatas();
        Assert.Contains("No hay columnas", vm.ResumenZapatas);
    }

    [Fact]
    public void PredimensionarZapatas_usa_descenso_geometrico_cuando_hay_vigas()
    {
        // Fixture geométrico de PredimensionarGeometricoTests: 2 losas (Carga 10,
        // 4×4), 3 vigas, 4 columnas; C04 recibe Wu = 60 t por área tributaria.
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 0 });
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 4 });
        nivel.Sistemas.Add(s);
        Viga V(double ox, double oy, double len, double ang)
        {
            var v = new Viga { OrigenX = ox, OrigenY = oy, AnguloGrados = ang };
            v.Tramos.Add(new TramoViga { Longitud = len });
            return v;
        }
        nivel.Vigas.Add(V(0, 4, 4, 0));
        nivel.Vigas.Add(V(0, 0, 4, 0));
        nivel.Vigas.Add(V(0, 0, 4, 90));
        var c04 = new Columna { Nombre = "C04", CoordenadaX = 0, CoordenadaY = 4 };
        nivel.Columnas.Add(new Columna { Nombre = "C00", CoordenadaX = 0, CoordenadaY = 0 });
        nivel.Columnas.Add(new Columna { Nombre = "C40", CoordenadaX = 4, CoordenadaY = 0 });
        nivel.Columnas.Add(c04);
        nivel.Columnas.Add(new Columna { Nombre = "C44", CoordenadaX = 4, CoordenadaY = 4 });
        ed.Niveles.Add(nivel);

        var vm = new BajadaCargasViewModel(() => ed) { PresionAdmisible = 15 };
        vm.PredimensionarZapatas();

        // C04 con su axial tributaria real (60 t), no el equitativo CargaEnBase/4.
        var fila = vm.ZapatasDiseno.Single(z => ReferenceEquals(z.Columna, c04));
        Assert.Equal(60, fila.PuTon, 3);
        Assert.Contains("geométrico", vm.ResumenZapatas);
    }
}
