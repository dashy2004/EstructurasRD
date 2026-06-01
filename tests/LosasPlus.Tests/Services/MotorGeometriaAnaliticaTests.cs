using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del <see cref="MotorGeometriaAnalitica"/> — el motor de
/// auto-alineación y auto-conexión de losas por proximidad (Epic v1.3
/// Iteración 3).
/// </summary>
public class MotorGeometriaAnaliticaTests
{
    private static Losa LosaEn(int id, double posX, double posY, double lx = 4.0, double ly = 4.0)
        => new() { Id = id, Lx = lx, Ly = ly, PosX = posX, PosY = posY };

    [Fact]
    public void DosLosas_LadoALado_HolguraDe3cm_SeAlinean()
    {
        var s = new Sistema();
        var a = LosaEn(1, 0.0, 0.0);
        var b = LosaEn(2, 4.03, 0.0);   // 3 cm de holgura a la derecha de A
        s.Losas.Add(a);
        s.Losas.Add(b);

        var r = MotorGeometriaAnalitica.EjecutarAlineacionYConexion(s);

        Assert.Equal(1, r.LosasAlineadasCount);
        Assert.Equal(4.0, b.PosX!.Value, precision: 9);   // B calza exacto con A.derecha
    }

    [Fact]
    public void DosLosas_TrasAlinear_GeneranUnBordeX_AtomicamenteConLaAlineacion()
    {
        var s = new Sistema();
        s.Losas.Add(LosaEn(1, 0.0, 0.0));
        s.Losas.Add(LosaEn(2, 4.03, 0.0));

        var r = MotorGeometriaAnalitica.EjecutarAlineacionYConexion(s);

        Assert.Equal(1, r.BordesCreadosCount);
        Assert.Single(s.BordesX);
        Assert.Empty(s.BordesY);
        // BI = losa izquierda (Id 1), BJ = losa derecha (Id 2).
        Assert.Equal(1, s.BordesX[0].BI);
        Assert.Equal(2, s.BordesX[0].BJ);
    }

    [Fact]
    public void DosLosas_YaConectadas_NoDuplicaElBorde()
    {
        var s = new Sistema();
        s.Losas.Add(LosaEn(1, 0.0, 0.0));
        s.Losas.Add(LosaEn(2, 4.0, 0.0));   // ya en contacto exacto
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" });

        var r = MotorGeometriaAnalitica.EjecutarAlineacionYConexion(s);

        Assert.Equal(0, r.BordesCreadosCount);
        Assert.Single(s.BordesX);   // sigue habiendo exactamente uno
    }

    [Fact]
    public void DosLosas_SeparadasMasQueTolerancia_NoSeTocanNiConectan()
    {
        var s = new Sistema();
        var a = LosaEn(1, 0.0, 0.0);
        var b = LosaEn(2, 4.20, 0.0);   // 20 cm de holgura — fuera de tolerancia
        s.Losas.Add(a);
        s.Losas.Add(b);

        var r = MotorGeometriaAnalitica.EjecutarAlineacionYConexion(s);

        Assert.Equal(0, r.LosasAlineadasCount);
        Assert.Equal(0, r.BordesCreadosCount);
        Assert.Equal(4.20, b.PosX!.Value, precision: 9);   // B no se movió
        Assert.Empty(s.BordesX);
        Assert.Empty(s.BordesY);
    }

    [Fact]
    public void DosLosas_Apiladas_HolguraDe3cm_SeAlineanYGeneranUnBordeY()
    {
        var s = new Sistema();
        var a = LosaEn(1, 0.0, 0.0);
        var b = LosaEn(2, 0.0, 4.03);   // 3 cm de holgura debajo de A (Y descendente)
        s.Losas.Add(a);
        s.Losas.Add(b);

        var r = MotorGeometriaAnalitica.EjecutarAlineacionYConexion(s);

        Assert.Equal(1, r.LosasAlineadasCount);
        Assert.Equal(4.0, b.PosY!.Value, precision: 9);   // B calza exacto con A.abajo
        Assert.Equal(1, r.BordesCreadosCount);
        Assert.Single(s.BordesY);
        Assert.Empty(s.BordesX);
        Assert.Equal(1, s.BordesY[0].BI);   // BI = losa de arriba
        Assert.Equal(2, s.BordesY[0].BJ);   // BJ = losa de abajo
    }
}
