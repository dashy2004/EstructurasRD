using System.Collections.Generic;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de la jerarquía de dominio <see cref="Edificio"/> → <see cref="Nivel"/>
/// introducida en la Fase 1 de la suite estructural, y de la fachada de
/// compatibilidad <see cref="Proyecto.Sistemas"/>.
/// </summary>
public class EdificioNivelTests
{
    [Fact]
    public void Edificio_nuevo_tiene_niveles_vacios_y_nombre_por_defecto()
    {
        var e = new Edificio();

        Assert.NotNull(e.Niveles);
        Assert.Empty(e.Niveles);
        Assert.Equal("Edificio 1", e.Nombre);
    }

    [Fact]
    public void Nivel_nuevo_tiene_sistemas_vacios_y_defaults()
    {
        var n = new Nivel();

        Assert.NotNull(n.Sistemas);
        Assert.Empty(n.Sistemas);
        Assert.Equal("Nivel 1", n.Nombre);
        Assert.Equal(0.0, n.Cota);
    }

    [Fact]
    public void Edificio_y_Nivel_notifican_cambios_de_propiedad()
    {
        var props = new List<string?>();

        var e = new Edificio();
        e.PropertyChanged += (_, a) => props.Add(a.PropertyName);
        e.Nombre = "Torre A";

        var n = new Nivel();
        n.PropertyChanged += (_, a) => props.Add(a.PropertyName);
        n.Nombre = "Planta Baja";
        n.Cota   = 3.0;

        Assert.Contains(nameof(Edificio.Nombre), props);
        Assert.Contains(nameof(Nivel.Nombre), props);
        Assert.Contains(nameof(Nivel.Cota), props);
    }

    [Fact]
    public void Proyecto_nuevo_materializa_un_Edificio_y_un_Nivel_al_usar_Sistemas()
    {
        var p = new Proyecto();

        Assert.NotNull(p.Sistemas);
        Assert.Empty(p.Sistemas);
        Assert.Single(p.Edificios);
        Assert.Single(p.Edificios[0].Niveles);
    }

    [Fact]
    public void AsegurarEstructura_es_idempotente()
    {
        var p = new Proyecto();

        p.AsegurarEstructura();
        p.AsegurarEstructura();
        _ = p.Sistemas;   // tercer disparo, ahora vía la fachada

        Assert.Single(p.Edificios);
        Assert.Single(p.Edificios[0].Niveles);
    }

    [Fact]
    public void Sistemas_es_la_misma_coleccion_del_nivel_por_defecto()
    {
        var p = new Proyecto();
        var s = new Sistema { Nombre = "E1" };

        p.Sistemas.Add(s);

        // La fachada devuelve la colección REAL del primer nivel — no una copia.
        Assert.Same(p.Sistemas, p.Edificios[0].Niveles[0].Sistemas);
        Assert.Single(p.Edificios[0].Niveles[0].Sistemas);
        Assert.Same(s, p.Edificios[0].Niveles[0].Sistemas[0]);
    }

    [Fact]
    public void Edificio_tiene_coleccion_de_ejes_vacia_por_defecto_y_aditiva()
    {
        var ed = new Edificio();
        Assert.Empty(ed.Ejes);

        ed.Ejes.Add(new EjeEstructural { Etiqueta = "A" });
        Assert.Single(ed.Ejes);
        Assert.Equal("A", ed.Ejes[0].Etiqueta);
    }

    // =====================================================================
    // B3 — Uso/Cota viven en Nivel (hogar canónico), no en Sistema.
    // =====================================================================

    [Fact]
    public void Nivel_expone_Uso_y_CotaMetros_con_defaults_y_notifica()
    {
        var n = new Nivel();

        // Defaults aditivos: Entrepiso y cota 0.
        Assert.Equal(SistemaUso.Entrepiso, n.Uso);
        Assert.Equal(0.0, n.CotaMetros, 6);

        var props = new List<string?>();
        n.PropertyChanged += (_, a) => props.Add(a.PropertyName);

        n.Uso        = SistemaUso.Techo;
        n.CotaMetros = 5.60;

        Assert.Equal(SistemaUso.Techo, n.Uso);
        Assert.Equal(5.60, n.CotaMetros, 6);
        Assert.Contains(nameof(Nivel.Uso), props);
        Assert.Contains(nameof(Nivel.CotaMetros), props);
    }

    [Fact]
    public void Nivel_CotaMetros_es_alias_de_Cota_una_sola_cota()
    {
        var n = new Nivel { CotaMetros = 2.80 };
        Assert.Equal(2.80, n.Cota, 6);          // CotaMetros escribe la misma cota

        n.Cota = 3.40;
        Assert.Equal(3.40, n.CotaMetros, 6);    // y Cota la refleja de vuelta
    }
}
