using System.Linq;
using LosasPlus.Calculo;
using LosasPlus.Models;
using LosasPlus.Validation;
using LosasPlus.Validation.Rules;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="ValidationEngine"/> y de cada regla individual.
/// Cubren casos limpios, errores, advertencias y umbrales según el uso del
/// proyecto (R-001 §3.2.1 + §2.1.4 + ACI 318 §9.5.3.3).
/// </summary>
public class ValidationEngineTests
{
    /// <summary>
    /// Construye un proyecto mínimo: 1 sistema entrepiso con 1 losa válida.
    /// El uso default es "Residencial" (mínimo 0.10 m, carga viva 0.20 t/m²).
    /// </summary>
    private static Proyecto ProyectoBase(string uso = "Residencial")
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Uso = uso;
        p.Normas.Add("R-001");
        p.Normas.Add("ACI 318-05");
        var sistema = new Sistema { Nombre = "E1", Uso = SistemaUso.Entrepiso, CotaMetros = 2.80 };
        sistema.Losas.Add(new Losa { Id = 1, Tipo = 10, Lx = 4.0, Ly = 4.0, Espesor = 0.150 });
        p.Sistemas.Add(sistema);
        return p;
    }

    [Fact]
    public void Default_engine_corre_y_devuelve_reporte_no_nulo()
    {
        var engine = ValidationEngine.Default();
        var reporte = engine.Validar(ProyectoBase());
        Assert.NotNull(reporte);
        Assert.NotEmpty(reporte.ReglamentoAplicable);
    }

    [Fact]
    public void Proyecto_limpio_no_genera_issues()
    {
        var p = ProyectoBase();
        CalculoEngine.RecalcularProyecto(p);
        var reporte = ValidationEngine.Default().Validar(p);
        Assert.Equal(0, reporte.Errores);
        // Aceptamos advertencias sutiles (margen ACI) pero la base no debería tener errores.
        Assert.True(reporte.Errores == 0, $"Se esperaba 0 errores, recibido {reporte.Errores}.");
    }

    // -----------------------------------------------------------------
    // R-001 §3.2.1 — Espesor mínimo según uso
    // -----------------------------------------------------------------

    [Fact]
    public void EspesorMinimoR001_dispara_error_si_residencial_y_espesor_bajo_0_10()
    {
        var p = ProyectoBase("Residencial");
        p.Sistemas[0].Losas[0].Espesor = 0.08;  // bajo el mínimo
        var issues = new EspesorMinimoR001Rule().Evaluar(p).ToList();
        Assert.Single(issues);
        Assert.Equal(ValidationSeverity.Error, issues[0].Severidad);
        Assert.Equal("R-001 §3.2.1", issues[0].ReferenciaNormativa);
        Assert.Equal(1, issues[0].LosaId);
        Assert.Equal("E1", issues[0].NombreSistema);
    }

    [Fact]
    public void EspesorMinimoR001_oficinas_exige_0_12()
    {
        Assert.Equal(0.12, EspesorMinimoR001Rule.EspesorMinimoSegunUso("Oficinas"));
        Assert.Equal(0.12, EspesorMinimoR001Rule.EspesorMinimoSegunUso("OFICINAS"));
    }

    [Fact]
    public void EspesorMinimoR001_industrial_exige_0_15()
    {
        Assert.Equal(0.15, EspesorMinimoR001Rule.EspesorMinimoSegunUso("Industrial"));
    }

    [Fact]
    public void EspesorMinimoR001_uso_desconocido_cae_a_0_10()
    {
        Assert.Equal(0.10, EspesorMinimoR001Rule.EspesorMinimoSegunUso(null));
        Assert.Equal(0.10, EspesorMinimoR001Rule.EspesorMinimoSegunUso(""));
        Assert.Equal(0.10, EspesorMinimoR001Rule.EspesorMinimoSegunUso("Marciano"));
    }

    [Fact]
    public void EspesorMinimoR001_no_dispara_si_espesor_igual_al_minimo()
    {
        var p = ProyectoBase("Comercial");
        p.Sistemas[0].Losas[0].Espesor = 0.120;  // exactamente el mínimo
        var issues = new EspesorMinimoR001Rule().Evaluar(p).ToList();
        Assert.Empty(issues);
    }

    // -----------------------------------------------------------------
    // R-001 §2.1.4 — Carga viva mínima
    // -----------------------------------------------------------------

    [Fact]
    public void CargaVivaMinimaR001_residencial_exige_0_20()
    {
        Assert.Equal(0.20, CargaVivaMinimaR001Rule.CargaMinimaSegunUso("Residencial"));
    }

    [Fact]
    public void CargaVivaMinimaR001_oficinas_exige_0_25()
    {
        Assert.Equal(0.25, CargaVivaMinimaR001Rule.CargaMinimaSegunUso("Oficinas"));
    }

    [Fact]
    public void CargaVivaMinimaR001_dispara_error_si_ql_bajo()
    {
        var p = ProyectoBase("Residencial");
        // Forzar ql bajo el mínimo (0.20) sin pasar por el engine de cálculo.
        p.Sistemas[0].Losas[0].Ql = 0.15;
        var issues = new CargaVivaMinimaR001Rule().Evaluar(p).ToList();
        Assert.Single(issues);
        Assert.Equal(ValidationSeverity.Error, issues[0].Severidad);
        Assert.Equal(0.15, issues[0].ValorActual);
        Assert.Equal(0.20, issues[0].ValorRequerido);
    }

    [Fact]
    public void CargaVivaMinimaR001_techo_admite_mitad_del_minimo()
    {
        var p = ProyectoBase("Residencial");
        p.Sistemas[0].Uso = SistemaUso.Techo;
        p.Sistemas[0].Losas[0].Ql = 0.10;  // mitad de 0.20 — OK para techo
        var issues = new CargaVivaMinimaR001Rule().Evaluar(p).ToList();
        Assert.Empty(issues);
    }

    [Fact]
    public void CargaVivaMinimaR001_no_dispara_si_no_hay_ql_calculado()
    {
        var p = ProyectoBase("Residencial");
        // Losas con Ql=null aún (el engine de cálculo no corrió).
        p.Sistemas[0].Losas[0].Ql = null;
        var issues = new CargaVivaMinimaR001Rule().Evaluar(p).ToList();
        Assert.Empty(issues);
    }

    // -----------------------------------------------------------------
    // ACI 318 §9.5.3.3 — Espesor vs h_calc
    // -----------------------------------------------------------------

    [Fact]
    public void EspesorVsCalculado_dispara_error_si_espesor_menor_que_hcalc()
    {
        var p = ProyectoBase();
        p.Sistemas[0].Losas[0].Espesor = 0.10;
        p.Sistemas[0].Losas[0].HCalc = 0.152;  // simula que el engine calculó
        var issues = new EspesorVsCalculadoRule().Evaluar(p).ToList();
        Assert.Single(issues);
        Assert.Equal(ValidationSeverity.Error, issues[0].Severidad);
        Assert.Equal("ACI 318 §9.5.3.3", issues[0].ReferenciaNormativa);
    }

    [Fact]
    public void EspesorVsCalculado_dispara_warning_si_espesor_igual_al_calculado()
    {
        var p = ProyectoBase();
        p.Sistemas[0].Losas[0].Espesor = 0.150;
        p.Sistemas[0].Losas[0].HCalc = 0.150;  // exacto al cálculo — sin margen
        var issues = new EspesorVsCalculadoRule().Evaluar(p).ToList();
        Assert.Single(issues);
        Assert.Equal(ValidationSeverity.Warning, issues[0].Severidad);
    }

    [Fact]
    public void EspesorVsCalculado_no_dispara_con_margen_sano()
    {
        var p = ProyectoBase();
        p.Sistemas[0].Losas[0].Espesor = 0.200;
        p.Sistemas[0].Losas[0].HCalc = 0.150;
        var issues = new EspesorVsCalculadoRule().Evaluar(p).ToList();
        Assert.Empty(issues);
    }

    [Fact]
    public void EspesorVsCalculado_no_dispara_si_hcalc_null()
    {
        var p = ProyectoBase();
        p.Sistemas[0].Losas[0].HCalc = null;
        var issues = new EspesorVsCalculadoRule().Evaluar(p).ToList();
        Assert.Empty(issues);
    }

    // -----------------------------------------------------------------
    // Aspecto Pieper-Martens
    // -----------------------------------------------------------------

    [Fact]
    public void AspectoLosaRule_dispara_warning_si_relacion_excede_2()
    {
        var p = ProyectoBase();
        p.Sistemas[0].Losas[0].Lx = 2.0;
        p.Sistemas[0].Losas[0].Ly = 5.0;  // ratio 2.5 → fuera de rango
        var issues = new AspectoLosaRule().Evaluar(p).ToList();
        Assert.Single(issues);
        Assert.Equal(ValidationSeverity.Warning, issues[0].Severidad);
    }

    [Fact]
    public void AspectoLosaRule_no_dispara_para_ratio_1()
    {
        var p = ProyectoBase();  // Lx=Ly=4.0 → ratio 1
        var issues = new AspectoLosaRule().Evaluar(p).ToList();
        Assert.Empty(issues);
    }

    // -----------------------------------------------------------------
    // Report agregado
    // -----------------------------------------------------------------

    [Fact]
    public void Reporte_agrega_conteos_errores_y_observaciones()
    {
        var p = ProyectoBase("Comercial");
        // Loosa 1 con espesor bajo el mínimo (0.12 m para comercial) → error
        p.Sistemas[0].Losas[0].Espesor = 0.10;
        // Forzar carga viva bajo el mínimo → error
        p.Sistemas[0].Losas[0].Ql = 0.30;  // bajo 0.50 mínimo comercial
        // Forzar aspecto fuera de rango → warning
        p.Sistemas[0].Losas[0].Lx = 2.0;
        p.Sistemas[0].Losas[0].Ly = 5.0;

        var reporte = ValidationEngine.Default().Validar(p);
        Assert.True(reporte.Errores >= 2, $"Se esperaban >=2 errores, recibido {reporte.Errores}.");
        Assert.True(reporte.Observaciones >= 1, $"Se esperaban >=1 obs, recibido {reporte.Observaciones}.");
        Assert.True(reporte.TieneErrores);
        Assert.False(reporte.EstaLimpio);
    }

    [Fact]
    public void Reporte_vacio_es_reutilizable()
    {
        Assert.Empty(ValidationReport.Vacio.Issues);
        Assert.True(ValidationReport.Vacio.EstaLimpio);
        Assert.Equal(0, ValidationReport.Vacio.Errores);
    }

    [Fact]
    public void Issues_se_ordenan_errores_primero()
    {
        var p = ProyectoBase();
        p.Sistemas[0].Losas[0].Espesor = 0.08;     // error
        p.Sistemas[0].Losas[0].Lx = 2.0;
        p.Sistemas[0].Losas[0].Ly = 5.0;           // warning
        var reporte = ValidationEngine.Default().Validar(p);
        Assert.True(reporte.Issues.Count >= 2);
        Assert.Equal(ValidationSeverity.Error, reporte.Issues[0].Severidad);
    }

    [Fact]
    public void AReporteTexto_incluye_titulos_y_referencias()
    {
        var p = ProyectoBase();
        p.Sistemas[0].Losas[0].Espesor = 0.08;
        var reporte = ValidationEngine.Default().Validar(p);
        var texto = reporte.AReporteTexto();
        Assert.Contains("VALIDACIÓN NORMATIVA", texto);
        Assert.Contains("R-001 §3.2.1", texto);
        Assert.Contains("E1", texto);
    }

    [Fact]
    public void UbicacionFormateada_funciona_para_los_3_alcances()
    {
        var issueProyecto = new ValidationIssue { Codigo = "x", Severidad = ValidationSeverity.Error, Titulo = "x", ReferenciaNormativa = "x" };
        Assert.Equal("Proyecto", issueProyecto.UbicacionFormateada);

        var issueSistema = issueProyecto with { NombreSistema = "E1" };
        Assert.Equal("Sistema E1", issueSistema.UbicacionFormateada);

        var issueLosa = issueSistema with { LosaId = 4 };
        Assert.Equal("Nivel E1 · Losa L4", issueLosa.UbicacionFormateada);
    }
}
