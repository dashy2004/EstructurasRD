using System;
using System.IO;
using System.Linq;
using LosasPlus.Cargas;
using LosasPlus.Models;
using LosasPlus.Persistence;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Fase B (B4): carga COMPLETA del proyecto y undo TOTAL.
///
/// <para>
/// Antes de B4:
/// </para>
/// <list type="bullet">
///   <item>Abrir un <c>.lpx.json</c> copiaba sólo <c>Edificios</c> + metadata
///   básica (Nombre/Autor/...) al proyecto vivo, descartando en silencio
///   <see cref="Proyecto.Cargas"/>, <see cref="Proyecto.Combinaciones"/> y la
///   metadata MemoriaPlus (CODIA, suelo, materiales, normas) — aunque el
///   serializador SÍ las persiste. Resultado: las cargas/combos desaparecían
///   al reabrir.</item>
///   <item><see cref="MainViewModel"/>.RestoreSnapshot (undo) restauraba
///   Edificios + Combinaciones + Vigas, pero NO <see cref="Proyecto.Cargas"/>
///   ni los placeholders MemoriaPlus — el undo era parcial.</item>
/// </list>
///
/// Estos tests fallan antes del fix de B4.
/// </summary>
public class CargaCompletaUndoTotalTests
{
    private static string TempLpx() => Path.Combine(
        Path.GetTempPath(), $"losasplus_b4_{Guid.NewGuid():N}.lpx.json");

    // =====================================================================
    // (a) Round-trip: guardar → abrir preserva cargas/combos/metadata.
    // =====================================================================

    [Fact]
    public void AbrirLpx_preserva_cargas_combinaciones_y_metadata_memoriaPlus()
    {
        // 1) Construir un proyecto con cargas + combinaciones + metadata
        //    distintivas y guardarlo a disco con el serializador nativo.
        var origen = new MainViewModel();
        var p = origen.Proyecto;

        // Cargas: semilla por defecto + un peso propio extra + carga viva editada.
        p.Cargas = CargasGlobales.SemillaPorDefecto();
        p.Cargas.PesosPropiosEntrepiso.Items.Add(
            new PesoPropio { Nombre = "MarcaB4", Espesor = 0.123, Densidad = 2.0 });
        p.Cargas.CargasVivas.Entrepiso = 0.357;
        int pesosEsperados = p.Cargas.PesosPropiosEntrepiso.Items.Count;
        int filasEsperadas = p.Cargas.CargaMuertaPorEspesor.Filas.Count;

        // Combinaciones: semilla ASCE 7-05 (4 casos + 8 combinaciones).
        p.Combinaciones = CombinacionesProyecto.SemillaPorDefecto(NormaCombinaciones.Asce7_05);
        int casosEsperados  = p.Combinaciones.Casos.Count;
        int combosEsperados = p.Combinaciones.Combinaciones.Count;
        Assert.True(casosEsperados > 0);
        Assert.True(combosEsperados > 0);

        // Metadata MemoriaPlus distintiva.
        p.Codia = "COD-B4-2026";
        p.EsfuerzoAdmisible = 1.85;
        p.FcKgCm2 = 350;
        p.Normas.Add("R-001-B4");

        var ruta = TempLpx();
        p.Archivo = ruta;
        ProyectoSerializer.Save(p, ruta);

        try
        {
            // 2) Abrir en una VM limpia por el camino vivo de la app.
            var vm = new MainViewModel();
            vm.AbrirProyectoLpxPorPath(ruta);
            var cargado = vm.Proyecto;

            // Cargas sobreviven al round-trip.
            Assert.Equal(filasEsperadas, cargado.Cargas.CargaMuertaPorEspesor.Filas.Count);
            Assert.Equal(pesosEsperados, cargado.Cargas.PesosPropiosEntrepiso.Items.Count);
            Assert.Contains(cargado.Cargas.PesosPropiosEntrepiso.Items, i => i.Nombre == "MarcaB4");
            Assert.Equal(0.357, cargado.Cargas.CargasVivas.Entrepiso, precision: 3);

            // Combinaciones sobreviven.
            Assert.Equal(casosEsperados,  cargado.Combinaciones.Casos.Count);
            Assert.Equal(combosEsperados, cargado.Combinaciones.Combinaciones.Count);
            Assert.Equal(NormaCombinaciones.Asce7_05, cargado.Combinaciones.Norma);

            // Metadata MemoriaPlus sobrevive.
            Assert.Equal("COD-B4-2026", cargado.Codia);
            Assert.Equal(1.85, cargado.EsfuerzoAdmisible, precision: 2);
            Assert.Equal(350, cargado.FcKgCm2, precision: 0);
            Assert.Contains("R-001-B4", cargado.Normas);
        }
        finally
        {
            if (File.Exists(ruta)) File.Delete(ruta);
        }
    }

    // =====================================================================
    // (b) Undo total: una mutación a Cargas se revierte por completo.
    // =====================================================================

    [Fact]
    public void Undo_restaura_cargas_completas()
    {
        var vm = new MainViewModel();
        var p = vm.Proyecto;

        // Estado base de cargas con un marcador conocido.
        p.Cargas = CargasGlobales.SemillaPorDefecto();
        int pesosBase = p.Cargas.PesosPropiosEntrepiso.Items.Count;
        double vivaBase = p.Cargas.CargasVivas.Entrepiso;

        // Snapshot ANTES de mutar (chokepoint de undo), luego mutar las cargas.
        vm.PushUndoSnapshot();
        p.Cargas.PesosPropiosEntrepiso.Items.Add(
            new PesoPropio { Nombre = "CargaTransitoria", Espesor = 0.05, Densidad = 2.4 });
        p.Cargas.CargasVivas.Entrepiso = 0.999;

        Assert.Equal(pesosBase + 1, vm.Proyecto.Cargas.PesosPropiosEntrepiso.Items.Count);
        Assert.Equal(0.999, vm.Proyecto.Cargas.CargasVivas.Entrepiso, precision: 3);

        // Undo debe restaurar el estado COMPLETO de las cargas.
        Assert.True(vm.PuedeUndo);
        vm.UndoCommand!.Execute(null);

        Assert.Equal(pesosBase, vm.Proyecto.Cargas.PesosPropiosEntrepiso.Items.Count);
        Assert.DoesNotContain(vm.Proyecto.Cargas.PesosPropiosEntrepiso.Items,
            i => i.Nombre == "CargaTransitoria");
        Assert.Equal(vivaBase, vm.Proyecto.Cargas.CargasVivas.Entrepiso, precision: 3);
    }

    /// <summary>
    /// El undo de una mutación de cargas no debe perder los placeholders de
    /// metadata MemoriaPlus presentes en el snapshot (CODIA, suelo, normas).
    /// </summary>
    [Fact]
    public void Undo_restaura_metadata_memoriaPlus()
    {
        var vm = new MainViewModel();
        var p = vm.Proyecto;

        p.Codia = "COD-UNDO";
        p.EsfuerzoAdmisible = 2.10;
        p.Normas.Clear();
        p.Normas.Add("R-001-UNDO");

        // Snapshot, luego ensuciar la metadata.
        vm.PushUndoSnapshot();
        p.Codia = "BASURA";
        p.EsfuerzoAdmisible = 99.9;
        p.Normas.Add("NORMA-BASURA");

        vm.UndoCommand!.Execute(null);

        Assert.Equal("COD-UNDO", vm.Proyecto.Codia);
        Assert.Equal(2.10, vm.Proyecto.EsfuerzoAdmisible, precision: 2);
        Assert.Contains("R-001-UNDO", vm.Proyecto.Normas);
        Assert.DoesNotContain("NORMA-BASURA", vm.Proyecto.Normas);
    }
}
