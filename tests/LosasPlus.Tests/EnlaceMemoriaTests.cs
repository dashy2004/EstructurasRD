using Xunit;
using MemoriaPlus.ViewModels;

namespace LosasPlus.Tests;

public class EnlaceMemoriaTests
{
    [Theory]
    [InlineData("https://github.com/equipo/modelos/blob/ramas/ejemplo/caso%201/data.json?raw=1")]
    [InlineData("https://raw.githubusercontent.com/equipo/modelos/ramas/ejemplo/caso%201/data.json")]
    public void Convierte_el_enlace_del_archivo_conservando_rama_y_ruta(string url)
    {
        Assert.Equal("https://raw.githubusercontent.com/equipo/modelos/ramas/ejemplo/caso%201/data.json",
            MainViewModel.ArchivoPublicoGitHub(url).AbsoluteUri);
    }

    [Theory]
    [InlineData("http://github.com/a/b/blob/main/datos.json")]
    [InlineData("https://raw.githubusercontent.com.ejemplo.org/a/b/main/data.json")]
    [InlineData("https://github.com/a/b/tree/main/datos.json")]
    [InlineData("https://github.com/a/b/blob/main/datos.py")]
    [InlineData("https://cuenta@github.com/a/b/blob/main/datos.json")]
    [InlineData("https://raw.githubusercontent.com/a/b/main/data.json?token=demo")]
    public void Descarga_solo_un_archivo_json_publico(string valor)
        => Assert.Throws<InvalidDataException>(() => MainViewModel.ArchivoPublicoGitHub(valor));
    [Theory]
    [InlineData("Prueba.LPX.JSON")]
    [InlineData("memoria.json")]
    [InlineData("fuentes.json")]
    public void Una_descarga_no_sobrescribe_proyectos_ni_configuraciones(string nombre)
        => Assert.Throws<InvalidDataException>(() => LosasPlus.Generation.SeleccionFuentesMemoria.ValidarDestinoDatos(nombre));
}
