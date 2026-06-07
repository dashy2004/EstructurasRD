using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests.Models;

/// <summary>
/// Tests de <see cref="IModeloObservable"/>: <see cref="Edificio"/> propaga hacia
/// arriba los cambios profundos (losa/columna anidadas) para que la UI 2D/3D
/// pueda refrescar en vivo.
/// </summary>
public class ModeloObservableTests
{
    [Fact]
    public void Edificio_notifica_cuando_cambia_una_losa_anidada()
    {
        var ed = new Edificio();
        var niv = new Nivel();
        var sis = new Sistema();
        var losa = new Losa { Id = 1 };
        sis.Losas.Add(losa);
        niv.Sistemas.Add(sis);
        ed.Niveles.Add(niv);

        int n = 0;
        ed.ModeloCambiado += (_, _) => n++;
        losa.CoordenadaX = 5.0;          // cambio profundo dentro de la jerarquía

        Assert.True(n > 0, "Edificio.ModeloCambiado debió dispararse por el cambio anidado");
    }

    [Fact]
    public void Edificio_notifica_al_agregar_columna_a_un_nivel()
    {
        var ed = new Edificio();
        var niv = new Nivel();
        ed.Niveles.Add(niv);

        int n = 0;
        ed.ModeloCambiado += (_, _) => n++;
        niv.Columnas.Add(new Columna { Id = 1 });

        Assert.True(n > 0);
    }
}
