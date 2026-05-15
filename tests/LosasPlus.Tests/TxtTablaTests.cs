using System;
using System.IO;
using System.Linq;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del parser tabular del .TXT del motor. Cubre parseo por columnas,
/// detección de modificaciones, render preservando layout y exportación a
/// archivo modificado sin pisar el original.
/// </summary>
public class TxtTablaTests
{
    [Fact]
    public void Parse_vacio_devuelve_tabla_sin_filas()
    {
        var t = TxtTabla.Parse("");
        Assert.Empty(t.Filas);
        Assert.Equal(0, t.CantidadColumnas);
    }

    [Fact]
    public void Parse_separa_por_2_o_mas_espacios()
    {
        var txt = "ID  Lx  Ly  H\n1   4.0  3.0  0.12\n2   5.0  4.0  0.15";
        var t = TxtTabla.Parse(txt);
        Assert.Equal(3, t.Filas.Count);
        Assert.Equal(4, t.CantidadColumnas);
        Assert.Equal("ID", t.Filas[0].Celdas[0].Valor);
        Assert.Equal("Lx", t.Filas[0].Celdas[1].Valor);
        Assert.Equal("1",   t.Filas[1].Celdas[0].Valor);
        Assert.Equal("4.0", t.Filas[1].Celdas[1].Valor);
        Assert.Equal("0.15", t.Filas[2].Celdas[3].Valor);
    }

    [Fact]
    public void Parse_no_separa_por_1_espacio_simple()
    {
        // "Tu casa" debe ser una sola celda (1 espacio), no 2.
        var txt = "Tu casa  Mi casa";
        var t = TxtTabla.Parse(txt);
        Assert.Single(t.Filas);
        Assert.Equal(2, t.Filas[0].Celdas.Count);
        Assert.Equal("Tu casa", t.Filas[0].Celdas[0].Valor);
        Assert.Equal("Mi casa", t.Filas[0].Celdas[1].Valor);
    }

    [Fact]
    public void Celda_Modificada_es_true_solo_si_Valor_cambio()
    {
        var c = new TxtCelda("orig");
        Assert.False(c.Modificada);
        c.Valor = "orig";
        Assert.False(c.Modificada);
        c.Valor = "editado";
        Assert.True(c.Modificada);
    }

    [Fact]
    public void Render_emite_LineaOriginal_si_fila_no_fue_modificada()
    {
        var txt = "alpha  beta  gamma";
        var t = TxtTabla.Parse(txt);
        var rendered = t.Render().TrimEnd('\r', '\n');
        // Sin modificaciones → preserva exactamente el espaciado original
        Assert.Equal("alpha  beta  gamma", rendered);
    }

    [Fact]
    public void Render_emite_celdas_con_tabs_si_fila_fue_modificada()
    {
        var txt = "alpha  beta  gamma";
        var t = TxtTabla.Parse(txt);
        t.Filas[0].Celdas[1].Valor = "BETA";  // modificar
        var rendered = t.Render().TrimEnd('\r', '\n');
        Assert.Equal("alpha\tBETA\tgamma", rendered);
    }

    [Fact]
    public void TieneModificaciones_se_propaga_desde_celdas()
    {
        var t = TxtTabla.Parse("a  b\nc  d");
        Assert.False(t.TieneModificaciones);
        t.Filas[1].Celdas[0].Valor = "C";
        Assert.True(t.TieneModificaciones);
        Assert.True(t.Filas[1].TieneModificaciones);
        Assert.False(t.Filas[0].TieneModificaciones);
    }

    [Fact]
    public void ExportarPreservandoOriginal_no_pisa_el_archivo_fuente()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"txttabla_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "salida.txt");
            File.WriteAllText(src, "alpha  beta\nfoo  bar");

            var t = TxtTabla.Parse(File.ReadAllText(src));
            t.Filas[0].Celdas[0].Valor = "ALPHA";
            var nuevoPath = t.ExportarPreservandoOriginal(src);

            // El original sigue intacto
            Assert.Equal("alpha  beta\nfoo  bar", File.ReadAllText(src));
            // El nuevo path es {stem}.modificado.txt
            Assert.Equal(Path.Combine(dir, "salida.modificado.txt"), nuevoPath);
            Assert.Contains("ALPHA", File.ReadAllText(nuevoPath));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ExportarPreservandoOriginal_agrega_timestamp_si_destino_ya_existe()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"txttabla_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "salida.txt");
            var modificadoYa = Path.Combine(dir, "salida.modificado.txt");
            File.WriteAllText(src, "x  y");
            File.WriteAllText(modificadoYa, "viejo");

            var t = TxtTabla.Parse("x  y");
            t.Filas[0].Celdas[0].Valor = "X";
            var nuevoPath = t.ExportarPreservandoOriginal(src);

            Assert.NotEqual(modificadoYa, nuevoPath);
            Assert.Contains("salida.modificado.", nuevoPath);
            // El anterior modificado.txt no se pisó
            Assert.Equal("viejo", File.ReadAllText(modificadoYa));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ExportarPreservandoOriginal_lanza_si_path_vacio()
    {
        var t = TxtTabla.Parse("a  b");
        Assert.Throws<ArgumentException>(() => t.ExportarPreservandoOriginal(""));
    }
}
