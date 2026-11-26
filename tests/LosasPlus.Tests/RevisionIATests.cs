using LosasPlus.Generation;
using Xunit;

namespace LosasPlus.Tests;

public class RevisionIATests : IDisposable
{
    private readonly DirectoryInfo _prueba = Directory.CreateTempSubdirectory("cdcrd-prueba-");
    public void Dispose() => _prueba.Delete(recursive: true);

    [Fact]
    public void Localiza_el_generador_del_checkout_y_prioriza_el_del_paquete()
    {
        string motor = _prueba.CreateSubdirectory("memoria-diseno").FullName;
        string herramientas = _prueba.CreateSubdirectory("herramientas").FullName;
        string repositorio = Path.Combine(herramientas, "revision_ia.py");
        string paquete = Path.Combine(motor, "revision_ia.py");
        Assert.Null(MotorMemoriaDiseno.ResolverRevisionIA(motor));
        File.WriteAllText(repositorio, "");
        Assert.Equal(repositorio, MotorMemoriaDiseno.ResolverRevisionIA(motor));
        File.WriteAllText(paquete, "");
        Assert.Equal(paquete, MotorMemoriaDiseno.ResolverRevisionIA(motor));
    }

    [Fact]
    public async Task La_ausencia_del_generador_conserva_la_revision_anterior()
    {
        string motor = _prueba.CreateSubdirectory("memoria-diseno").FullName;
        string salida = Path.Combine(_prueba.FullName, "revisión.md");
        File.WriteAllText(salida, "Revisión anterior");
        var resultado = await MotorMemoriaDiseno.GenerarRevisionIAAsync(motor, "data.json", salida, "Proyecto");
        Assert.False(resultado.Ok);
        Assert.Contains("revision_ia.py", resultado.Mensaje);
        Assert.Equal("Revisión anterior", File.ReadAllText(salida));
    }

    [Fact]
    public async Task El_cliente_rechaza_destinos_que_no_sean_markdown()
    {
        string motor = _prueba.CreateSubdirectory("memoria-diseno").FullName;
        File.WriteAllText(Path.Combine(motor, "revision_ia.py"), "");
        string entrada = Path.Combine(_prueba.FullName, "data.json");
        File.WriteAllText(entrada, "{}");
        var resultado = await MotorMemoriaDiseno.GenerarRevisionIAAsync(motor, entrada, entrada, "Proyecto");
        Assert.False(resultado.Ok);
        Assert.Equal("{}", File.ReadAllText(entrada));
    }
}
