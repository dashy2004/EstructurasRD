using LosasPlus.IA;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Parser de la respuesta de la IA (Qwen) → propuesta de losas/vigas. Testea el
/// núcleo puro (sin red): que extraiga la geometría aunque el modelo agregue
/// texto/markdown, y que no falle con basura.
/// </summary>
public class QwenAnalizadorTests
{
    [Fact]
    public void ParsearPropuesta_extrae_losas_y_vigas()
    {
        var json = "{\"losas\":[{\"x\":0,\"y\":0,\"lx\":4,\"ly\":5},{\"x\":4,\"y\":0,\"lx\":3,\"ly\":5}]," +
                   "\"vigas\":[{\"x1\":0,\"y1\":0,\"x2\":7,\"y2\":0}]}";

        var p = QwenAnalizador.ParsearPropuesta(json);

        Assert.Equal(2, p.Losas.Count);
        Assert.Single(p.Vigas);
        Assert.Equal(4.0, p.Losas[0].LxM, 3);
        Assert.Equal(5.0, p.Losas[0].LyM, 3);
        Assert.Equal(7.0, p.Vigas[0].X2Metros, 3);
        Assert.Null(p.Advertencias);
    }

    [Fact]
    public void ParsearPropuesta_tolera_texto_y_markdown_alrededor()
    {
        var contenido = "Acá está el resultado:\n```json\n" +
                        "{\"losas\":[{\"x\":1,\"y\":2,\"lx\":4,\"ly\":4}],\"vigas\":[]}\n```\nGracias.";

        var p = QwenAnalizador.ParsearPropuesta(contenido);

        Assert.Single(p.Losas);
        Assert.Empty(p.Vigas);
        Assert.Equal(1.0, p.Losas[0].XMetros, 3);
        Assert.Equal(2.0, p.Losas[0].YMetros, 3);
    }

    [Fact]
    public void ParsearPropuesta_sin_json_no_crashea()
    {
        var p = QwenAnalizador.ParsearPropuesta("no hay json acá");
        Assert.Empty(p.Losas);
        Assert.Empty(p.Vigas);
    }
}
