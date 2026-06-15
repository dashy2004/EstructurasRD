using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>Orquesta el diseno de todas las losas de un <see cref="Sistema"/> via el motor:
/// mapeo -> cliente -> adaptador -> <see cref="ParsedOutput"/> -> <see cref="SalidaPerdomoAdapter"/>.</summary>
public sealed class MotorFeaLosasService
{
    /// <summary>Marcador de origen en <see cref="SalidaPerdomo.ArchivoTxt"/> cuando la fuente es el motor.</summary>
    public const string FuenteMotor = "motor-fea (FEA)";

    private readonly MotorFeaClient _client;

    public MotorFeaLosasService(MotorFeaClient client) => _client = client;

    /// <summary>Calcula todas las losas y devuelve la <see cref="SalidaPerdomo"/> poblada
    /// y la lista de ids de losas que fallaron (omitidas, no cortan la corrida).</summary>
    public async Task<(SalidaPerdomo salida, List<int> fallidas)> CalcularAsync(
        Sistema sistema, string borde, CancellationToken ct)
    {
        var parsed = new ParsedOutput { Sistema = sistema.Nombre };
        var fallidas = new List<int>();

        foreach (var losa in sistema.Losas)
        {
            try
            {
                var prm = MapeadorLosaMotor.Map(losa, sistema, borde);
                string json = System.Text.Json.JsonSerializer.Serialize(prm);
                string salidaJson = await _client.DisenarLosaAsync(json, ct);
                parsed.PorLosa.Add(MotorFeaAdapter.Map(salidaJson, losa));
            }
            catch (MotorFeaException)
            {
                fallidas.Add(losa.Id);
            }
        }

        var ids = sistema.Losas.Select(l => l.Id);
        var salida = SalidaPerdomoAdapter.From(parsed, FuenteMotor, ids);
        return (salida, fallidas);
    }
}
