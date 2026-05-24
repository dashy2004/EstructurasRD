// =====================================================================
// SafExporter — Punto de entrada público para exportar un Proyecto v3
// a un archivo SAF 2.2.0 .xlsx, enriquecido con la hoja custom
// `EstructurasRD_Design` que preserva el refuerzo y los ratios D/C que
// el esquema SAF nativo no admite.
//
// Fase INTEROP-I1 (Parte B) del Plan Maestro de Expansión 3D —
// EstructurasRD.
//
// Pipeline:
//   1. SafMapper.MapearProyecto → ExcelModel inmutable (Parte A).
//   2. RatioCalculator.CalcularRatiosPorElemento → diccionario D/C.
//   3. SimpleInjectorBootstrapper → DI container del SDK SAF.
//   4. IExcelExportService.Export → escribe ~30 hojas estándar SAF.
//   5. SafCustomSheet.Inyectar → agrega la hoja `EstructurasRD_Design`.
//   6. ExcelPackage.Save → sella el .xlsx y libera handles de Windows.
//
// Decisiones de diseño:
//   1. ENTRY POINT: `Exportar(Proyecto, string)` — método estático
//      público. Sin async (el SDK SAF es síncrono).
//
//   2. DI BENDECIDO POR SCIA: `SimpleInjectorBootstrapper` arrastra
//      todas las dependencias internas (IExcelDocumentWriter,
//      IEventService, IExcelValidator, etc.) y entrega un IServiceProvider
//      del cual resolvemos `IExcelExportService`.
//
//   3. TRY/CATCH ROBUSTO: las dos operaciones de I/O (escritura SAF
//      baseline + inyección hoja custom) van envueltas en sus propios
//      try/catch que re-lanzan InvalidOperationException con mensaje
//      legible. Esto permite al ViewModel de la UI mostrarle al usuario
//      un toast amigable en lugar de un stack trace de EPPlus.
//
//   4. VERSIÓN SAF 2.2.0: pasamos `new Version(2, 2, 0)` al exporter
//      para fijar la versión de spec destino — última versión estable.
//
//   5. AISLAMIENTO: este servicio vive 100% en `src.Interop.SAF`.
//      Sin referencias a WPF, HelixToolkit u OxyPlot.
// =====================================================================
using System;
using System.IO;
using LosasPlus.Models;
using OfficeOpenXml;
using SAF.Bootstrappers.SimpleInjector5;
using SAF.DataAccess.Contracts;
using SAF.Infrastructure.Bootstrapping;
// SAF.Infrastructure.Extensions provee la extensión genérica
// IServiceProvider.GetService<T>() — System.IServiceProvider sólo expone
// la no-genérica GetService(Type), inutilizable con type-args.
using SAF.Infrastructure.Extensions;

// Alias global para evitar la colisión con `LosasPlus.Interop.SAF` —
// idéntico al usado en SafMapper (Parte A).
using SafModels = global::SAF.DataAccess.Models;

namespace LosasPlus.Interop.SAF;

/// <summary>
/// Servicio público de exportación de un <see cref="Proyecto"/> a SAF
/// 2.2.0 .xlsx. Orquesta el SDK oficial + la inyección de la hoja
/// custom de EstructurasRD.
/// </summary>
public static class SafExporter
{
    /// <summary>Versión del estándar SAF a la que se exporta (última estable, 2022-11-28).</summary>
    public static readonly Version VersionSafObjetivo = new Version(2, 2, 0);

    /// <summary>
    /// Exporta el proyecto a un archivo .xlsx en la ruta indicada.
    /// Sobrescribe el archivo si ya existe.
    ///
    /// <para>
    /// El archivo resultante contiene las ~30 hojas estándar de SAF
    /// 2.2.0 + una hoja extra `EstructurasRD_Design` con los datos
    /// lossy (refuerzo + ratios D/C). SCIA Engineer / RFEM ignoran la
    /// hoja extra; EstructurasRD la consume en el roundtrip.
    /// </para>
    /// </summary>
    /// <param name="proyecto">Proyecto v3 a exportar.</param>
    /// <param name="rutaArchivoXlsx">Ruta absoluta del .xlsx destino.</param>
    /// <exception cref="ArgumentNullException">Si proyecto es null.</exception>
    /// <exception cref="ArgumentException">Si la ruta es null o vacía.</exception>
    /// <exception cref="InvalidOperationException">
    /// Si falla la escritura del archivo SAF baseline (permisos, archivo
    /// bloqueado, parent dir inexistente, etc.) o la inyección de la
    /// hoja custom. El mensaje original del error queda en
    /// <c>InnerException</c>.
    /// </exception>
    public static void Exportar(Proyecto proyecto, string rutaArchivoXlsx)
    {
        ArgumentNullException.ThrowIfNull(proyecto);
        if (string.IsNullOrWhiteSpace(rutaArchivoXlsx))
            throw new ArgumentException(
                "La ruta del archivo SAF no puede ser nula o vacía.",
                nameof(rutaArchivoXlsx));

        ValidarDirectorioDestino(rutaArchivoXlsx);

        // -----------------------------------------------------------------
        // 1) Mapear el proyecto al modelo SAF inmutable (Parte A).
        // -----------------------------------------------------------------
        SafModels.ExcelModel modelo = SafMapper.MapearProyecto(proyecto);

        // -----------------------------------------------------------------
        // 2) Calcular ratios D/C por elemento (motores estáticos de src.Core).
        // -----------------------------------------------------------------
        var ratios = RatioCalculator.CalcularRatiosPorElemento(proyecto);

        // -----------------------------------------------------------------
        // 3) Bootstrap del SDK SAF — DI container SimpleInjector 5.
        // -----------------------------------------------------------------
        IBootstrapper bootstrapper = new SimpleInjectorBootstrapper();
        IServiceProvider scope = bootstrapper.CreateScope();
        var exportService = scope.GetService<IExcelExportService>();
        if (exportService is null)
            throw new InvalidOperationException(
                "El bootstrapper SAF no pudo resolver IExcelExportService. "
                + "Verifique que StructuralAnalysisFormat.Bootstrappers.SimpleInjector5 "
                + "esté instalado correctamente.");

        // -----------------------------------------------------------------
        // 4) Escribir las ~30 hojas estándar SAF al .xlsx destino.
        // -----------------------------------------------------------------
        try
        {
            using var fileStream = File.Create(rutaArchivoXlsx);
            exportService.Export(fileStream, modelo, VersionSafObjetivo);
            // El SDK NO cierra el stream por nosotros; el `using` lo libera.
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"No se pudo escribir el archivo SAF en '{rutaArchivoXlsx}'. "
                + "Verifique que el archivo no esté abierto en Excel y que tenga "
                + "permisos de escritura en el directorio.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"Acceso denegado al escribir '{rutaArchivoXlsx}'. "
                + "Permisos insuficientes en el directorio destino.", ex);
        }

        // -----------------------------------------------------------------
        // 5) Reabrir el .xlsx con EPPlus e inyectar la hoja custom.
        // -----------------------------------------------------------------
        try
        {
            using var package = new ExcelPackage(new FileInfo(rutaArchivoXlsx));
            SafCustomSheet.Inyectar(package, proyecto, ratios);
            package.Save();   // sella el .xlsx y libera handles de Windows
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Archivo SAF baseline escrito correctamente, pero falló la inyección "
                + $"de la hoja `EstructurasRD_Design` en '{rutaArchivoXlsx}'. "
                + "El archivo sigue siendo válido como SAF puro — sólo se perdieron "
                + "los datos lossy de refuerzo y D/C.", ex);
        }
    }

    /// <summary>
    /// Verifica que el directorio padre del archivo destino exista.
    /// Si no, lanza una excepción amigable antes de invocar al SDK
    /// (que daría un error más críptico).
    /// </summary>
    private static void ValidarDirectorioDestino(string rutaArchivoXlsx)
    {
        string? directorio = Path.GetDirectoryName(Path.GetFullPath(rutaArchivoXlsx));
        if (!string.IsNullOrEmpty(directorio) && !Directory.Exists(directorio))
        {
            throw new InvalidOperationException(
                $"El directorio destino '{directorio}' no existe. "
                + "Créelo manualmente antes de exportar el archivo SAF.");
        }
    }
}
