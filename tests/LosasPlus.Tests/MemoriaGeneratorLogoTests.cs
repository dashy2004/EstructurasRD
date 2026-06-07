using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LosasPlus.Generation;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Verifica que la Memoria generada lleva el logo de la aplicación en el
/// encabezado, aun cuando la plantilla no trae header (el generador lo crea y lo
/// referencia en el sectPr del cuerpo).
/// </summary>
public class MemoriaGeneratorLogoTests
{
    private const string PlantillaPath = "fixtures/Memoria_Losas_PLANTILLA.docx";

    [Fact]
    public void Memoria_lleva_logo_en_el_encabezado()
    {
        var output = Path.Combine(Path.GetTempPath(), $"memoria_logo_{Guid.NewGuid():N}.docx");
        try
        {
            new MemoriaGenerator().Generar(new Proyecto { Nombre = "Logo test" }, PlantillaPath, output);

            using var doc = WordprocessingDocument.Open(output, isEditable: false);
            var main = doc.MainDocumentPart!;

            // 1. Existe un header part con una imagen embebida.
            var header = main.HeaderParts.FirstOrDefault();
            Assert.NotNull(header);
            Assert.NotEmpty(header!.ImageParts);

            // 2. El encabezado contiene un Drawing (la imagen del logo).
            Assert.NotEmpty(header.Header.Descendants<Drawing>());

            // 3. El header está referenciado en las propiedades de sección del cuerpo.
            var sectPr = main.Document.Body!.Elements<SectionProperties>().LastOrDefault();
            Assert.NotNull(sectPr);
            Assert.NotEmpty(sectPr!.Elements<HeaderReference>());
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
