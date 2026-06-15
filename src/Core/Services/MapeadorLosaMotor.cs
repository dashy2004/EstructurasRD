using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>Traduce una <see cref="Losa"/> de la app a los parámetros SI que espera el motor.</summary>
public static class MapeadorLosaMotor
{
    public const int MallaPorDefecto = 8;
    public const double NuHormigon = 0.2;

    public static ParamsLosaMotor Map(Losa losa, Sistema sistema, string borde)
    {
        double fcMPa = sistema.Fc * MotorFeaConversion.TonfCm2_a_MPa;
        double fyMPa = sistema.Fy * MotorFeaConversion.TonfCm2_a_MPa;
        return new ParamsLosaMotor
        {
            A = losa.Lx,
            B = losa.Ly,
            Nx = MallaPorDefecto,
            Ny = MallaPorDefecto,
            E = MotorFeaConversion.ModuloElasticoPa(fcMPa),
            Nu = NuHormigon,
            T = losa.Espesor,
            Q = losa.Carga * MotorFeaConversion.TonfM2_a_Nm2,
            Fc = fcMPa,
            Fy = fyMPa,
            Recubrimiento = losa.Rec * MotorFeaConversion.M_a_Mm,
            Borde = borde,
        };
    }
}
