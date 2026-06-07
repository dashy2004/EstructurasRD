using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LosasPlus.IA;

/// <summary>
/// Configuración de la IA local (Qwen vía Ollama). Se carga de
/// <c>qwen.config.json</c>. La bandera <see cref="PermitirModificarCodigo"/> es
/// siempre <c>false</c>: la IA es un asistente de extracción de geometría, no un
/// editor de código.
/// </summary>
public sealed record QwenConfig(
    string Endpoint = "http://127.0.0.1:11434",
    string Modelo = "qwen2.5vl:7b",
    bool SoloLectura = true,
    bool PermitirModificarCodigo = false,
    double Temperatura = 0.1,
    int TimeoutSegundos = 120);

/// <summary>Columna propuesta por la IA (datos de geometría, en metros).</summary>
public sealed record ColumnaPropuesta(double XMetros, double YMetros, double BaseM, double PeralteM, string? Nombre = null);

/// <summary>Losa propuesta por la IA (esquina + dimensiones, en metros).</summary>
public sealed record LosaPropuesta(double XMetros, double YMetros, double LxM, double LyM, int Tipo = 10);

/// <summary>Viga propuesta por la IA (segmento (x1,y1)-(x2,y2), en metros).</summary>
public sealed record VigaPropuesta(double X1Metros, double Y1Metros, double X2Metros, double Y2Metros, string? Nombre = null);

/// <summary>Eje/rejilla propuesto por la IA (etiqueta + recta, en metros).</summary>
public sealed record EjePropuesto(string Etiqueta, double X1, double Y1, double X2, double Y2);

/// <summary>
/// Propuesta de elementos estructurales extraída de un documento. Son
/// <b>datos de dominio</b> que el ingeniero revisa y aplica manualmente — nunca
/// código.
/// </summary>
public sealed record PropuestaElementos(
    IReadOnlyList<LosaPropuesta> Losas,
    IReadOnlyList<VigaPropuesta> Vigas,
    IReadOnlyList<ColumnaPropuesta> Columnas,
    IReadOnlyList<EjePropuesto> Ejes,
    string? Advertencias = null);

/// <summary>
/// Analizador estructural por IA local (<b>solo lectura</b>). Lee un archivo de
/// entrada (PDF/DXF/imagen) y devuelve una <see cref="PropuestaElementos"/>.
///
/// <para><b>GUARDRAIL.</b> Esta interfaz NO expone ninguna operación que
/// modifique el código fuente ni el proyecto: solo <i>devuelve</i> una propuesta
/// de datos. La implementación debe respetar <see cref="QwenConfig.SoloLectura"/>
/// y <see cref="QwenConfig.PermitirModificarCodigo"/> = <c>false</c>, y limitarse
/// a leer el archivo de entrada (sin escribir en disco fuera de un caché temporal
/// propio). El motor de cálculo (Pieper-Martens) y las reglas estructurales NO
/// los decide la IA.</para>
/// </summary>
public interface IAnalizadorEstructuralIA
{
    /// <summary>Analiza un PDF/DXF/imagen y propone elementos. No modifica nada del proyecto.</summary>
    Task<PropuestaElementos> AnalizarAsync(string archivoPath, CancellationToken ct = default);
}
