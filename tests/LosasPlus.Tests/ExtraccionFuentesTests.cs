using System.Text.Json.Nodes;
using LosasPlus.Generation;
using Xunit;

namespace LosasPlus.Tests;

public sealed class ExtraccionFuentesTests : IDisposable
{
    private readonly DirectoryInfo _base = Directory.CreateTempSubdirectory("fuentes-prueba-");
    private string F(string nombre, string texto = "")
    {
        string ruta = Path.Combine(_base.FullName, nombre);
        File.WriteAllText(ruta, texto);
        return ruta;
    }
    public void Dispose() => _base.Delete(true);

    [Fact]
    public void Relaciona_modelos_y_niveles_por_tipo_y_ruta()
    {
        var seleccion = new SeleccionFuentesMemoria();
        string dl = F("Nivel 1.DL"), txt = F("Nivel 1.TXT"), modelo = F("A.EDB");
        foreach (string ruta in new[] { dl, txt, modelo, F("base.FDB"), F("cargas.xlsx") }) seleccion.Agregar(ruta);
        var man = seleccion.Preparar(F("salida.json"));
        Assert.Equal(dl, (string?)man["niveles"]![0]!["dl"]);
        Assert.Equal(txt, (string?)man["niveles"]![0]!["txt"]);
        Assert.Equal(modelo, (string?)man["bloques"]![0]!["etabs"]);
        Assert.NotNull(man["cargas"]!["libro"]);
        Assert.NotNull(man["fundaciones"]!["safe"]);
    }

    [Fact]
    public void Normaliza_relativos_sin_modificar_el_manifiesto_original()
    {
        string original = """{"niveles":[{"nombre":"Techo","txt":"n.TXT","sin_hojas":true}],"bloques_json":["resultado.json"]}""";
        string manifiesto = F("fuentes.json", original), txt = F("n.TXT"), bloque = F("resultado.json", "{}");
        var seleccion = new SeleccionFuentesMemoria();
        seleccion.Agregar(manifiesto);
        var man = seleccion.Preparar(F("salida.json"));
        Assert.Equal(txt, (string?)man["niveles"]![0]!["txt"]);
        Assert.True((bool?)man["niveles"]![0]!["sin_hojas"]);
        Assert.Equal(original, File.ReadAllText(manifiesto));
        Assert.Throws<InvalidDataException>(() => seleccion.Preparar(bloque));
        Assert.Throws<InvalidDataException>(() => seleccion.Preparar(manifiesto));
        Assert.Throws<InvalidDataException>(() => seleccion.Agregar(txt));
    }

    [Fact]
    public void Rechaza_dl_suelto_dos_fundaciones_y_configuracion_como_destino()
    {
        var seleccion = new SeleccionFuentesMemoria();
        seleccion.Agregar(F("losa.DL"));
        Assert.Throws<InvalidDataException>(() => seleccion.Preparar(F("datos.json")));
        seleccion.Agregar(F("losa.TXT"));
        seleccion.Agregar(F("A.FDB"));
        Assert.Throws<InvalidDataException>(() => seleccion.Agregar(F("B.FDB")));
        Assert.Throws<InvalidDataException>(() => seleccion.Preparar(F("memoria.json")));
    }

    [Fact]
    public void Rechaza_configuracion_sin_fuentes_y_url_en_manifiesto()
    {
        var seleccion = new SeleccionFuentesMemoria();
        seleccion.Agregar(F("fuentes.json","""{"portada":{}}"""));
        Assert.Throws<InvalidDataException>(() => seleccion.Preparar(F("data.json")));
        F("fuentes.json","""{"niveles":[{"txt":"https://example.com/a.txt"}]}""");
        Assert.Throws<InvalidDataException>(() => seleccion.Preparar(F("data.json")));
    }
}
