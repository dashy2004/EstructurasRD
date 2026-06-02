using System;
using Xunit;
using LosasPlus.Models;
using LosasPlus.Vigas;
using LosasPlus.Views;

namespace LosasPlus.Tests;

public class PlantaSnapEngineTests
{
    private Nivel CreateTestLevel()
    {
        var nivel = new Nivel { Cota = 3.0 };
        
        // Add a column at (2.0, 3.0)
        nivel.Columnas.Add(new Columna
        {
            Id = 1,
            Nombre = "C-1",
            CoordenadaX = 2.0,
            CoordenadaY = 3.0,
            Base = 0.3,
            Peralte = 0.3
        });

        // Add a beam from (0.0, 0.0) to (5.0, 0.0) (horizontal along Y=0)
        var viga = new Viga
        {
            Id = 1,
            Nombre = "V-1",
            OrigenX = 0.0,
            OrigenY = 0.0,
            AnguloGrados = 0
        };
        viga.Tramos.Add(new TramoViga { Longitud = 5.0 });
        viga.Apoyos.Add(new ApoyoViga { CoordenadaX = 0.0 });
        viga.Apoyos.Add(new ApoyoViga { CoordenadaX = 5.0 });
        nivel.Vigas.Add(viga);

        // Add a slab from (0.0, 0.0) with Lx=4.0, Ly=4.0
        var sys = new Sistema { Nombre = "Sys-1" };
        sys.Losas.Add(new Losa
        {
            Id = 1,
            CoordenadaX = 0.0,
            CoordenadaY = 0.0,
            Lx = 4.0,
            Ly = 4.0
        });
        nivel.Sistemas.Add(sys);

        return nivel;
    }

    [Fact]
    public void Snap_disabled_returns_raw_coordinates()
    {
        var nivel = CreateTestLevel();
        var result = PlantaSnapEngine.CalculateSnap(2.13, 2.87, nivel, isSnappingEnabled: false, gridStep: 0.5, searchRadiusMeters: 0.3);
        
        Assert.Equal(2.13, result.X);
        Assert.Equal(2.87, result.Y);
        Assert.False(result.IsSnapped);
        Assert.Equal("Libre", result.Description);
    }

    [Fact]
    public void Snap_to_grid_behaves_correctly()
    {
        var result1 = PlantaSnapEngine.CalculateSnap(2.13, 2.87, null, isSnappingEnabled: true, gridStep: 0.5, searchRadiusMeters: 0.3);
        Assert.Equal(2.0, result1.X);
        Assert.Equal(3.0, result1.Y);
        Assert.True(result1.IsSnapped);

        var result2 = PlantaSnapEngine.CalculateSnap(0.89, 0.12, null, isSnappingEnabled: true, gridStep: 1.0, searchRadiusMeters: 0.3);
        Assert.Equal(1.0, result2.X);
        Assert.Equal(0.0, result2.Y);
    }

    [Fact]
    public void Snap_to_column_center_takes_priority()
    {
        var nivel = CreateTestLevel();
        // Closest to (2.0, 3.0)
        var result = PlantaSnapEngine.CalculateSnap(2.12, 2.91, nivel, isSnappingEnabled: true, gridStep: 0.5, searchRadiusMeters: 0.3);
        
        Assert.Equal(2.0, result.X);
        Assert.Equal(3.0, result.Y);
        Assert.True(result.IsSnapped);
        Assert.Contains("Centro de C-1", result.Description);
    }

    [Fact]
    public void Snap_to_beam_endpoints_and_axis_behaves_correctly()
    {
        var nivel = CreateTestLevel();
        
        // Closest to beam start endpoint (0.0, 0.0)
        var resultEnd = PlantaSnapEngine.CalculateSnap(0.11, 0.08, nivel, isSnappingEnabled: true, gridStep: 0.5, searchRadiusMeters: 0.3);
        Assert.Equal(0.0, resultEnd.X);
        Assert.Equal(0.0, resultEnd.Y);
        Assert.Contains("Origen de V-1", resultEnd.Description);

        // Closest to beam axis (e.g. y=0) at x=2.3
        var resultAxis = PlantaSnapEngine.CalculateSnap(2.32, 0.09, nivel, isSnappingEnabled: true, gridStep: 0.5, searchRadiusMeters: 0.3);
        Assert.Equal(2.32, resultAxis.X, 2);
        Assert.Equal(0.0, resultAxis.Y, 2);
        Assert.Contains("Eje de V-1", resultAxis.Description);
    }

    [Fact]
    public void Snap_to_slab_vertices_behaves_correctly()
    {
        var nivel = CreateTestLevel();
        
        // Slab top-right vertex is at (4.0, 4.0)
        var result = PlantaSnapEngine.CalculateSnap(3.91, 4.12, nivel, isSnappingEnabled: true, gridStep: 0.5, searchRadiusMeters: 0.3);
        Assert.Equal(4.0, result.X);
        Assert.Equal(4.0, result.Y);
        Assert.Contains("Vértice Losa 1", result.Description);
    }
}
