using System.Linq;
using System.Reflection;
using LosasPlus.Columnas;
using LosasPlus.Vigas;
using LosasPlus.ViewModels.Mensajeria;
using LosasPlus.Zapatas;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests del adaptador read-only <see cref="ElementoActivoViewModel"/>
/// (Liga B paso B2). Cobertura:
/// <list type="bullet">
///   <item>Null Object — singleton compartido + placeholder semántica.</item>
///   <item>Factories de proyección — Columna/Viga/Zapata generan DTOs con
///   tipo + Id + nombre + dimensiones formateadas correctos.</item>
///   <item>Inmutabilidad estructural — todas las propiedades son <c>get</c>-only
///   (sin setters públicos) verificado vía reflection.</item>
/// </list>
/// </summary>
public class ElementoActivoViewModelTests
{
    [Fact]
    public void Vacio_TieneEsVacioTrueYNombrePlaceholder()
    {
        var v = ElementoActivoViewModel.Vacio;

        Assert.True(v.EsVacio);
        Assert.Equal("Sin selección", v.Nombre);
        Assert.Equal("—", v.Tipo);
        Assert.Equal(0, v.Id);
        Assert.Equal(string.Empty, v.Dimensiones);
        Assert.Equal(0.0, v.RatioDC);
    }

    [Fact]
    public void Adaptar_DesdeColumna_PuebleTipoIdNombreDimensiones()
    {
        var col = new Columna
        {
            Id     = 7,
            Nombre = "C-7",
            Altura = 3.0,
        };
        // Geometria default: Base=0.40, Peralte=0.40 → "40×40 cm".

        var dto = ElementoActivoViewModel.Adaptar(col);

        Assert.False(dto.EsVacio);
        Assert.Equal("Columna", dto.Tipo);
        Assert.Equal(7, dto.Id);
        Assert.Equal("C-7", dto.Nombre);
        Assert.Contains("40×40 cm", dto.Dimensiones);
        Assert.Contains("H=3.00 m", dto.Dimensiones);
    }

    [Fact]
    public void Adaptar_DesdeViga_PuebleTipoIdNombreDimensiones()
    {
        var v = new Viga
        {
            Id     = 3,
            Nombre = "V-3",
        };
        v.Tramos.Add(new TramoViga { Longitud = 2.5, Base = 0.25, Peralte = 0.40 });
        v.Tramos.Add(new TramoViga { Longitud = 2.5, Base = 0.25, Peralte = 0.40 });

        var dto = ElementoActivoViewModel.Adaptar(v);

        Assert.False(dto.EsVacio);
        Assert.Equal("Viga", dto.Tipo);
        Assert.Equal(3, dto.Id);
        Assert.Equal("V-3", dto.Nombre);
        // Dimensiones del primer tramo + longitud total acumulada (5.00 m).
        Assert.Contains("250×400 mm", dto.Dimensiones);
        Assert.Contains("L=5.00 m", dto.Dimensiones);
    }

    [Fact]
    public void Adaptar_DesdeZapata_PuebleTipoIdNombreDimensiones()
    {
        var z = new ZapataAislada
        {
            Id     = 12,
            Nombre = "Z-12",
        };
        z.Dimensiones.LargoB   = 1.50;
        z.Dimensiones.AnchoL   = 1.50;
        z.Dimensiones.EspesorH = 0.40;

        var dto = ElementoActivoViewModel.Adaptar(z);

        Assert.False(dto.EsVacio);
        Assert.Equal("Zapata", dto.Tipo);
        Assert.Equal(12, dto.Id);
        Assert.Equal("Z-12", dto.Nombre);
        Assert.Contains("150×150 cm", dto.Dimensiones);
        Assert.Contains("h=40 cm", dto.Dimensiones);
    }

    [Fact]
    public void Inmutabilidad_PropiedadesSinSetterPublico()
    {
        // Garantía estructural del DTO: ninguna propiedad pública tiene
        // setter accesible — la UI no puede mutar el adapter ni, por
        // tanto, alterar el dominio subyacente.
        var props = typeof(ElementoActivoViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var p in props)
        {
            var setter = p.SetMethod;
            Assert.True(
                setter is null || !setter.IsPublic,
                $"La propiedad '{p.Name}' debe ser get-only (sin setter público) — viola el contrato read-only del adapter.");
        }

        // Sanity: la cantidad de propiedades públicas observables coincide
        // con las 6 declaradas (Tipo, Id, Nombre, Dimensiones, RatioDC, EsVacio).
        Assert.Equal(6, props.Length);
    }

    [Fact]
    public void Vacio_ReferenciaCompartida_SingletonReal()
    {
        // El Null Object es una instancia compartida — dos accesos a
        // .Vacio retornan exactamente la misma referencia.
        var a = ElementoActivoViewModel.Vacio;
        var b = ElementoActivoViewModel.Vacio;
        Assert.Same(a, b);

        // Adaptar(null) cuando la entidad es null degrada al singleton.
        Columna? colNull = null;
        var dto = ElementoActivoViewModel.Adaptar(colNull!);
        Assert.Same(ElementoActivoViewModel.Vacio, dto);
    }
}
