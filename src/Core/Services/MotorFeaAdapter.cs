using System.Text.Json;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>Traduce el JSON de salida de <c>--disenar-losa</c> (una losa) al intermedio
/// <see cref="LosaResult"/> que consume <see cref="SalidaPerdomoAdapter"/>, convirtiendo SI a app.</summary>
public static class MotorFeaAdapter
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static LosaResult Map(string jsonSalida, Losa losa)
    {
        var m = JsonSerializer.Deserialize<ResultadoLosaMotor>(jsonSalida, Opts)
                ?? throw new MotorFeaException($"Salida del motor vacia o invalida para la losa {losa.Id}.");
        return Map(m, losa);
    }

    public static LosaResult Map(ResultadoLosaMotor m, Losa losa)
    {
        double d = losa.Espesor - losa.Rec; // peralte efectivo (m)
        return new LosaResult
        {
            Id = losa.Id,
            Tipo = losa.Tipo,
            Carga = losa.Carga,
            H = losa.Espesor,
            Lx = losa.Lx,
            Ly = losa.Ly,
            Mfx = m.MxMax * MotorFeaConversion.Nm_a_TonfM,
            Mfy = m.MyMax * MotorFeaConversion.Nm_a_TonfM,
            MSx = m.MApoyoMax * MotorFeaConversion.Nm_a_TonfM,
            MSy = m.MApoyoMax * MotorFeaConversion.Nm_a_TonfM,
            Dx = d,
            Mux = m.MuX * MotorFeaConversion.Nmm_a_TonfM,
            AsxReq = m.FranjaX.AsRequerido * MotorFeaConversion.Mm2_a_Cm2,
            AsxProv = m.FranjaX.AsProvista * MotorFeaConversion.Mm2_a_Cm2,
            DisponerX = m.FranjaX.Disponer,
            Dy = d,
            Muy = m.MuY * MotorFeaConversion.Nmm_a_TonfM,
            AsyReq = m.FranjaY.AsRequerido * MotorFeaConversion.Mm2_a_Cm2,
            AsyProv = m.FranjaY.AsProvista * MotorFeaConversion.Mm2_a_Cm2,
            DisponerY = m.FranjaY.Disponer,
        };
    }
}
