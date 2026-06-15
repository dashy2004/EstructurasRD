namespace LosasPlus.Services;

/// <summary>Constantes de conversión entre las unidades de la app (Perdomo) y las del motor (SI).</summary>
public static class MotorFeaConversion
{
    /// <summary>ton/m² → N/m². 1 tonf = 1000 kgf, 1 kgf = 9.80665 N.</summary>
    public const double TonfM2_a_Nm2 = 9806.65;

    /// <summary>N·m/m → ton·m/m.</summary>
    public const double Nm_a_TonfM = 1.0 / 9806.65;

    /// <summary>N·mm/m → ton·m/m.</summary>
    public const double Nmm_a_TonfM = 1.0 / 9.80665e6;

    /// <summary>mm²/m → cm²/m.</summary>
    public const double Mm2_a_Cm2 = 1.0 / 100.0;

    /// <summary>m → mm.</summary>
    public const double M_a_Mm = 1000.0;

    /// <summary>ton/cm² → MPa. 1 tonf/cm² = 1000 kgf/cm² = 98.0665 MPa.</summary>
    public const double TonfCm2_a_MPa = 98.0665;

    /// <summary>Módulo elástico del hormigón por ACI 318: E[MPa] = 4700·√(fc[MPa]).</summary>
    public static double ModuloElasticoPa(double fcMPa) => 4700.0 * System.Math.Sqrt(fcMPa) * 1e6;
}
