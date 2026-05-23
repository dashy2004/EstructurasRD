using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using LosasPlus.Cargas;
using LosasPlus.Columnas;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.Vigas;
using LosasPlus.Zapatas;

namespace LosasPlus.Auditoria;

/// <summary>
/// Motor de auditoría cruzada del proyecto — recorre el grafo completo
/// (Vigas, Columnas, Zapatas del primer nivel topológico), valida la
/// consistencia inter-elemento (materiales y continuidad de cargas),
/// recolecta los peores ratios demanda/capacidad de cada módulo analítico
/// y emite un <see cref="InformeAuditoriaGlobal"/> con la lista
/// estructurada de hallazgos y el veredicto agregado de conformidad
/// (Fase 7, Iteración 1 de la suite estructural).
///
/// <para>
/// Clase estática <b>pura</b> — sin estado ni dependencias de UI. Toda la
/// orquestación de motores analíticos (Vigas, Columnas, Zapatas) corre
/// dentro de este método único, que es seguro de invocar desde un hilo
/// secundario.
/// </para>
/// </summary>
public static class ProyectoAuditorEngine
{
    /// <summary>Tolerancia (MPa) para considerar que f'c o fy difieren del valor global.</summary>
    private const double ToleranciaMaterial = 0.1;

    /// <summary>Tolerancia relativa para detectar discrepancias de continuidad de carga columna ↔ zapata.</summary>
    private const double ToleranciaContinuidad = 0.05;   // 5 %

    /// <summary>Ratio por encima del cual una falla se eleva de Advertencia a ErrorFallo.</summary>
    private const double LimiteRatioCritico = 1.5;

    /// <summary>
    /// Audita el <paramref name="proyecto"/> y devuelve el informe consolidado.
    /// </summary>
    /// <param name="proyecto">El proyecto a auditar — no puede ser null.</param>
    /// <param name="fcGlobal">
    /// Resistencia f'c global del proyecto, en MPa. Vigas y columnas se
    /// comparan contra este valor; las zapatas se asumen con f'c = fcGlobal.
    /// </param>
    /// <param name="fyGlobal">
    /// Fluencia fy global del acero, en MPa.
    /// </param>
    public static InformeAuditoriaGlobal AuditarProyecto(
        Proyecto proyecto, double fcGlobal = 28.0, double fyGlobal = 420.0)
    {
        ArgumentNullException.ThrowIfNull(proyecto);
        if (fcGlobal <= 0.0 || fyGlobal <= 0.0)
            return InformeAuditoriaGlobal.Vacio;

        proyecto.AsegurarEstructura();
        var nivel = proyecto.Edificios[0].Niveles[0];
        var combinaciones = proyecto.Combinaciones;
        string nivelNombre = string.IsNullOrEmpty(nivel.Nombre) ? "Nivel 1" : nivel.Nombre;
        var mensajes = new List<MensajeAuditoria>();

        // ----- Sección A: Consistencia de materiales -----
        AuditarMaterialesVigas(nivel.Vigas, fcGlobal, fyGlobal, nivelNombre, mensajes);
        AuditarMaterialesColumnas(nivel.Columnas, fcGlobal, fyGlobal, nivelNombre, mensajes);

        // ----- Sección B: Continuidad de cargas columna ↔ zapata -----
        AuditarContinuidadCargas(nivel.Columnas, nivel.Zapatas, nivelNombre, mensajes);

        // ----- Sección C: Ratios D/C globales por elemento -----
        double ratioVigas    = AuditarRatiosVigas(nivel.Vigas, combinaciones, nivelNombre, mensajes);
        double ratioColumnas = AuditarRatiosColumnas(nivel.Columnas, nivelNombre, mensajes);
        double ratioZapatas  = AuditarRatiosZapatas(
            nivel.Zapatas, combinaciones, fcGlobal, fyGlobal, nivelNombre, mensajes);

        double ratioGlobal = Math.Max(ratioVigas, Math.Max(ratioColumnas, ratioZapatas));
        int advertencias = mensajes.Count(m => m.Tipo == TipoMensajeAuditoria.Advertencia);
        int errores = mensajes.Count(m => m.Tipo == TipoMensajeAuditoria.ErrorFallo);
        bool conforme = ratioGlobal <= 1.0 && errores == 0;

        return new InformeAuditoriaGlobal(
            Mensajes: mensajes,
            CantidadAdvertencias: advertencias,
            CantidadErrores: errores,
            RatioMaximoVigas: ratioVigas,
            RatioMaximoColumnas: ratioColumnas,
            RatioMaximoZapatas: ratioZapatas,
            RatioMaximoGlobal: ratioGlobal,
            ProyectoConforme: conforme);
    }

    // ----- Sección A: Materiales -----

    private static void AuditarMaterialesVigas(
        ObservableCollection<Viga> vigas, double fcGlobal, double fyGlobal,
        string nivelNombre, List<MensajeAuditoria> mensajes)
    {
        foreach (var viga in vigas)
        {
            for (int i = 0; i < viga.Tramos.Count; i++)
            {
                var tramo = viga.Tramos[i];
                string ubic = $"{nivelNombre} > Viga {viga.Nombre} > Tramo {i + 1}";
                if (Math.Abs(tramo.Seccion.Fc - fcGlobal) > ToleranciaMaterial)
                    mensajes.Add(new MensajeAuditoria(
                        TipoMensajeAuditoria.Advertencia,
                        Categoria: "Materiales",
                        Codigo: "MAT-001",
                        Mensaje: FormatoMaterial("f'c", tramo.Seccion.Fc, fcGlobal, "MPa"),
                        Ubicacion: ubic));
                if (Math.Abs(tramo.Seccion.Fy - fyGlobal) > ToleranciaMaterial)
                    mensajes.Add(new MensajeAuditoria(
                        TipoMensajeAuditoria.Advertencia,
                        Categoria: "Materiales",
                        Codigo: "MAT-002",
                        Mensaje: FormatoMaterial("fy", tramo.Seccion.Fy, fyGlobal, "MPa"),
                        Ubicacion: ubic));
            }
        }
    }

    private static void AuditarMaterialesColumnas(
        ObservableCollection<Columna> columnas, double fcGlobal, double fyGlobal,
        string nivelNombre, List<MensajeAuditoria> mensajes)
    {
        foreach (var col in columnas)
        {
            string ubic = $"{nivelNombre} > Columna {col.Nombre}";
            if (Math.Abs(col.Geometria.Fc - fcGlobal) > ToleranciaMaterial)
                mensajes.Add(new MensajeAuditoria(
                    TipoMensajeAuditoria.Advertencia,
                    Categoria: "Materiales",
                    Codigo: "MAT-001",
                    Mensaje: FormatoMaterial("f'c", col.Geometria.Fc, fcGlobal, "MPa"),
                    Ubicacion: ubic));
            if (Math.Abs(col.Acero.Fy - fyGlobal) > ToleranciaMaterial)
                mensajes.Add(new MensajeAuditoria(
                    TipoMensajeAuditoria.Advertencia,
                    Categoria: "Materiales",
                    Codigo: "MAT-002",
                    Mensaje: FormatoMaterial("fy", col.Acero.Fy, fyGlobal, "MPa"),
                    Ubicacion: ubic));
        }
    }

    private static string FormatoMaterial(string nombre, double actual, double esperado, string unidad)
    {
        var cul = CultureInfo.InvariantCulture;
        return $"{nombre} = {actual.ToString("0.#", cul)} {unidad} difiere del valor global del proyecto "
               + $"({esperado.ToString("0.#", cul)} {unidad}).";
    }

    // ----- Sección B: Continuidad de cargas -----

    private static void AuditarContinuidadCargas(
        ObservableCollection<Columna> columnas, ObservableCollection<ZapataAislada> zapatas,
        string nivelNombre, List<MensajeAuditoria> mensajes)
    {
        foreach (var col in columnas)
        {
            if (col.Demandas.Count == 0) continue;

            double sumaPuColumna = col.Demandas.Sum(d => Math.Abs(d.Pu));
            var zapata = zapatas.FirstOrDefault(z => z.Id == col.Id);

            if (zapata is null)
            {
                mensajes.Add(new MensajeAuditoria(
                    TipoMensajeAuditoria.Advertencia,
                    Categoria: "ContinuidadCarga",
                    Codigo: "CARGA-001",
                    Mensaje: $"Columna {col.Nombre} (Id={col.Id}) tiene "
                             + $"{col.Demandas.Count} demanda(s) registrada(s) pero no existe "
                             + $"una zapata con Id={col.Id} en el nivel.",
                    Ubicacion: $"{nivelNombre} > Columna {col.Nombre}"));
                continue;
            }

            double sumaPZapata = zapata.Cargas.Sum(c => Math.Abs(c.P));

            if (sumaPuColumna > 0.0 && sumaPZapata == 0.0)
            {
                mensajes.Add(new MensajeAuditoria(
                    TipoMensajeAuditoria.Advertencia,
                    Categoria: "ContinuidadCarga",
                    Codigo: "CARGA-002",
                    Mensaje: $"Columna {col.Nombre} suma Pu = {sumaPuColumna:0.#} kN, "
                             + $"pero la zapata {zapata.Nombre} no tiene cargas axiales mapeadas.",
                    Ubicacion: $"{nivelNombre} > Columna {col.Nombre} ↔ Zapata {zapata.Nombre}"));
                continue;
            }

            if (sumaPuColumna > 0.0 && sumaPZapata > 0.0)
            {
                double mayor = Math.Max(sumaPuColumna, sumaPZapata);
                double dif = Math.Abs(sumaPuColumna - sumaPZapata) / mayor;
                if (dif > ToleranciaContinuidad)
                {
                    mensajes.Add(new MensajeAuditoria(
                        TipoMensajeAuditoria.Advertencia,
                        Categoria: "ContinuidadCarga",
                        Codigo: "CARGA-003",
                        Mensaje: $"Discrepancia de {dif * 100.0:0.#} % entre la suma de Pu "
                                 + $"en la columna ({sumaPuColumna:0.#} kN) y la suma de P "
                                 + $"en la zapata ({sumaPZapata:0.#} kN).",
                        Ubicacion: $"{nivelNombre} > Columna {col.Nombre} ↔ Zapata {zapata.Nombre}"));
                }
            }
        }
    }

    // ----- Sección C: Ratios D/C -----

    private static double AuditarRatiosVigas(
        ObservableCollection<Viga> vigas, CombinacionesProyecto combinaciones,
        string nivelNombre, List<MensajeAuditoria> mensajes)
    {
        double ratioMax = 0.0;
        foreach (var viga in vigas)
        {
            if (viga.Tramos.Count == 0) continue;
            var resultado = VigaContinuaEngine.Resolver(viga, combinaciones);
            if (resultado.EsInestable || resultado.Envolvente is null) continue;
            var verif = RcDesignEngine.VerificarVigaCompleta(viga, resultado.Envolvente);
            if (verif.RatioMaximo > ratioMax) ratioMax = verif.RatioMaximo;
            EmitirMensajeRatio(verif.RatioMaximo, "Viga", viga.Nombre, "RATIO-VIGA", nivelNombre, mensajes);
        }
        return ratioMax;
    }

    private static double AuditarRatiosColumnas(
        ObservableCollection<Columna> columnas, string nivelNombre, List<MensajeAuditoria> mensajes)
    {
        double ratioMax = 0.0;
        foreach (var col in columnas)
        {
            if (col.Demandas.Count == 0) continue;
            var diagrama = ColumnaDesignEngine.ConstruirDiagramaInteraccion2D(col.Geometria, col.Acero);
            double ratioCol = 0.0;
            foreach (var d in col.Demandas)
            {
                var r = ColumnaDesignEngine.VerificarPuntoDemanda(diagrama, d.Mu, d.Pu);
                if (r.RatioEficiencia > ratioCol) ratioCol = r.RatioEficiencia;
            }
            if (ratioCol > ratioMax) ratioMax = ratioCol;
            EmitirMensajeRatio(ratioCol, "Columna", col.Nombre, "RATIO-COLUMNA", nivelNombre, mensajes);
        }
        return ratioMax;
    }

    private static double AuditarRatiosZapatas(
        ObservableCollection<ZapataAislada> zapatas, CombinacionesProyecto combinaciones,
        double fcGlobal, double fyGlobal, string nivelNombre, List<MensajeAuditoria> mensajes)
    {
        double ratioMax = 0.0;
        foreach (var z in zapatas)
        {
            var r = ZapataDesignEngine.VerificarEstructuraZapata(z, combinaciones, fcGlobal, fyGlobal);
            if (r.RatioEstructuralMaximo > ratioMax) ratioMax = r.RatioEstructuralMaximo;
            EmitirMensajeRatio(r.RatioEstructuralMaximo, "Zapata", z.Nombre, "RATIO-ZAPATA", nivelNombre, mensajes);
        }
        return ratioMax;
    }

    private static void EmitirMensajeRatio(
        double ratio, string tipoElemento, string nombreElemento, string codigoBase,
        string nivelNombre, List<MensajeAuditoria> mensajes)
    {
        if (double.IsInfinity(ratio) || ratio > LimiteRatioCritico)
        {
            mensajes.Add(new MensajeAuditoria(
                TipoMensajeAuditoria.ErrorFallo,
                Categoria: "RatioDC",
                Codigo: codigoBase + "-CRITICO",
                Mensaje: $"{tipoElemento} {nombreElemento} excede gravemente la capacidad de diseño "
                         + $"(D/C = {(double.IsInfinity(ratio) ? "∞" : ratio.ToString("0.##"))}).",
                Ubicacion: $"{nivelNombre} > {tipoElemento} {nombreElemento}"));
        }
        else if (ratio > 1.0)
        {
            mensajes.Add(new MensajeAuditoria(
                TipoMensajeAuditoria.Advertencia,
                Categoria: "RatioDC",
                Codigo: codigoBase,
                Mensaje: $"{tipoElemento} {nombreElemento} excede la capacidad de diseño "
                         + $"(D/C = {ratio:0.##}).",
                Ubicacion: $"{nivelNombre} > {tipoElemento} {nombreElemento}"));
        }
    }
}
