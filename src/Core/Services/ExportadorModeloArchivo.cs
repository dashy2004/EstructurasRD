using System.IO;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>Resumen de una exportación a archivo.</summary>
public readonly record struct ResumenExportacion(int Nodos, int Barras, string Ruta);

/// <summary>Valida y escribe el modelo del motor a un archivo JSON (única pieza con I/O).</summary>
public static class ExportadorModeloArchivo
{
    public static ResumenExportacion Exportar(
        Edificio edificio, string ruta, Georreferencia? georreferencia = null)
    {
        var modelo = ExportadorModeloMotor.Exportar(edificio, georreferencia);
        var errores = ExportadorModeloMotor.ValidarIntegridad(modelo);
        if (errores.Count > 0)
            throw new ExportadorModeloException("Modelo inválido: " + string.Join("; ", errores));

        File.WriteAllText(ruta, ExportadorModeloMotor.ToJson(modelo));
        return new ResumenExportacion(modelo.Nodos.Count, modelo.Elementos.Count, ruta);
    }
}
