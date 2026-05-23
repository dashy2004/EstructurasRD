using System.Linq;
using LosasPlus.Auditoria;
using LosasPlus.Cargas;
using LosasPlus.Columnas;
using LosasPlus.Models;
using LosasPlus.Persistence;
using LosasPlus.Vigas;
using LosasPlus.Zapatas;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del <see cref="ProyectoAuditorEngine"/> (Fase 7, Iteración 1): la
/// auditoría cruzada del grafo del proyecto valida la consistencia de
/// materiales entre vigas y columnas, la continuidad de cargas entre la
/// columna y su zapata basal (correlación por Id), y la recolección de los
/// peores ratios D/C de cada módulo. También verifica el roundtrip JSON
/// integral del grafo completo (Sistemas, Vigas, Columnas, Zapatas).
/// </summary>
public class ProyectoAuditorEngineTests
{
    /// <summary>
    /// Proyecto base con combinaciones seedeadas + 1 viga, 1 columna y 1
    /// zapata correlativa (mismo Id=1), todos con materiales por defecto
    /// (28/420 MPa) y cargas balanceadas.
    /// </summary>
    private static Proyecto ProyectoCompleto()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.AsegurarEstructura();
        var n = p.Edificios[0].Niveles[0];

        // Viga simplemente apoyada de un tramo con acero suficiente.
        var viga = new Viga { Id = 1, Nombre = "V-1" };
        var tramo = new TramoViga { Longitud = 6.0, ModuloElasticidad = 2.5e7, Inercia = 0.002 };
        tramo.Cargas.Add(new CargaElemento(TipoCargaElemento.Distribuida, 20.0, "D"));
        tramo.Refuerzo.AreaAceroBot = 0.0020;
        tramo.Refuerzo.AreaAceroTop = 0.0010;
        viga.Tramos.Add(tramo);
        viga.Apoyos.Add(new ApoyoViga(0.0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(6.0, TipoApoyo.Fijo));
        n.Vigas.Add(viga);

        // Columna 0.30×0.30 con capas perimetrales y una demanda modesta.
        var col = new Columna { Id = 1, Nombre = "C-1" };
        col.Geometria.Base = 0.30;
        col.Geometria.Peralte = 0.30;
        col.Geometria.Fc = 28.0;
        col.Acero.Fy = 420.0;
        col.Acero.Capas.Add(new CapaAcero { Profundidad = 0.05, Area = 0.0004 });
        col.Acero.Capas.Add(new CapaAcero { Profundidad = 0.25, Area = 0.0004 });
        col.Demandas.Add(new DemandaColumna { NombreCombo = "1.4D", Pu = 100.0, Mu = 10.0 });
        n.Columnas.Add(col);

        // Zapata con el mismo Id=1 — vinculada a la columna por correlación.
        var zap = new ZapataAislada { Id = 1, Nombre = "Z-1" };
        zap.Dimensiones.LargoB = 1.50;
        zap.Dimensiones.AnchoL = 1.50;
        zap.Dimensiones.EspesorH = 0.40;
        zap.Dimensiones.ProfundidadDesplante = 1.50;
        zap.Dimensiones.PeralteColumna = 0.30;
        zap.Dimensiones.AnchoColumna = 0.30;
        zap.Suelo.PresionAdmisible = 200.0;
        zap.Suelo.PesoEspecificoSuelo = 18.0;
        zap.Cargas.Add(new CargaZapata { CodigoCaso = "D", P = 100.0 });   // misma magnitud que Pu de la columna
        n.Zapatas.Add(zap);

        return p;
    }

    [Fact]
    public void ProyectoIdealEquilibrado_GeneraReporteConforme()
    {
        var p = ProyectoCompleto();

        var informe = ProyectoAuditorEngine.AuditarProyecto(p);

        Assert.True(informe.ProyectoConforme,
            $"Mensajes inesperados: {string.Join(" | ", informe.Mensajes.Select(m => $"{m.Codigo} {m.Mensaje}"))}");
        Assert.Empty(informe.Mensajes);
        Assert.True(informe.RatioMaximoGlobal < 1.0,
            $"RatioMaximoGlobal = {informe.RatioMaximoGlobal:0.###}");
    }

    [Fact]
    public void DesfaseDe_Fc_GatillaAdvertenciaDeInconsistenciaDeMateriales()
    {
        var p = ProyectoCompleto();
        // Columna con f'c = 21 MPa contra global 28 MPa.
        p.Edificios[0].Niveles[0].Columnas[0].Geometria.Fc = 21.0;

        var informe = ProyectoAuditorEngine.AuditarProyecto(p);

        Assert.Contains(informe.Mensajes, m =>
            m.Tipo == TipoMensajeAuditoria.Advertencia
            && m.Categoria == "Materiales"
            && m.Codigo == "MAT-001"
            && m.Ubicacion.Contains("C-1"));
    }

    [Fact]
    public void ColumnaHuerfanaSinZapata_DetonaAlerta()
    {
        var p = ProyectoCompleto();
        // Columna con Id=5, distinta de la zapata existente (Id=1).
        var huerfana = new Columna { Id = 5, Nombre = "C-5" };
        huerfana.Acero.Capas.Add(new CapaAcero { Profundidad = 0.05, Area = 0.0004 });
        huerfana.Acero.Capas.Add(new CapaAcero { Profundidad = 0.25, Area = 0.0004 });
        huerfana.Demandas.Add(new DemandaColumna { NombreCombo = "1.4D", Pu = 200.0, Mu = 5.0 });
        p.Edificios[0].Niveles[0].Columnas.Add(huerfana);

        var informe = ProyectoAuditorEngine.AuditarProyecto(p);

        Assert.Contains(informe.Mensajes, m =>
            m.Tipo == TipoMensajeAuditoria.Advertencia
            && m.Categoria == "ContinuidadCarga"
            && m.Codigo == "CARGA-001"
            && m.Ubicacion.Contains("C-5"));
    }

    [Fact]
    public void DiscrepanciaDeMagnitudes_EntreColumnaYZapata_DetonaAdvertencia()
    {
        var p = ProyectoCompleto();
        // Mantener la columna C-1 con Pu=100; modificar la zapata Z-1 para
        // que la suma de P sea sustancialmente menor (60 → 40 % de diferencia).
        var zap = p.Edificios[0].Niveles[0].Zapatas[0];
        zap.Cargas.Clear();
        zap.Cargas.Add(new CargaZapata { CodigoCaso = "D", P = 60.0 });

        var informe = ProyectoAuditorEngine.AuditarProyecto(p);

        Assert.Contains(informe.Mensajes, m =>
            m.Tipo == TipoMensajeAuditoria.Advertencia
            && m.Categoria == "ContinuidadCarga"
            && m.Codigo == "CARGA-003");
    }

    [Fact]
    public void PersistenciaIntegral_GrafoCompletoSobreviveElRoundtripJson()
    {
        // Proyecto con Sistema + Viga + Columna + Zapata simultáneamente.
        var p = new Proyecto();
        p.AsegurarEstructura();
        var n = p.Edificios[0].Niveles[0];

        n.Sistemas.Add(new Sistema { Nombre = "S-1", Uso = SistemaUso.Entrepiso });

        var viga = new Viga { Id = 11, Nombre = "V-Roundtrip" };
        viga.Tramos.Add(new TramoViga { Longitud = 4.0 });
        viga.Tramos.Add(new TramoViga { Longitud = 5.0 });
        viga.Tramos[0].Cargas.Add(new CargaElemento(TipoCargaElemento.Distribuida, 12.0, "D"));
        viga.Apoyos.Add(new ApoyoViga(0.0, TipoApoyo.Empotrado));
        viga.Apoyos.Add(new ApoyoViga(4.0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(9.0, TipoApoyo.Fijo));
        n.Vigas.Add(viga);

        var col = new Columna { Id = 22, Nombre = "C-Roundtrip", Altura = 3.2 };
        col.Geometria.Base = 0.35;
        col.Geometria.Peralte = 0.45;
        col.Acero.Capas.Add(new CapaAcero { Profundidad = 0.05, Area = 0.0006 });
        col.Acero.Capas.Add(new CapaAcero { Profundidad = 0.40, Area = 0.0006 });
        col.Demandas.Add(new DemandaColumna { NombreCombo = "1.2D+1.6L", Pu = 850.0, Mu = 75.0 });
        n.Columnas.Add(col);

        var zap = new ZapataAislada { Id = 33, Nombre = "Z-Roundtrip" };
        zap.Dimensiones.LargoB = 1.80;
        zap.Dimensiones.AnchoL = 2.20;
        zap.Dimensiones.EspesorH = 0.45;
        zap.Suelo.PresionAdmisible = 180.0;
        zap.Cargas.Add(new CargaZapata { CodigoCaso = "D", P = 900.0, Mx = 50.0 });
        n.Zapatas.Add(zap);

        var restaurado = ProyectoSerializer.FromJson(ProyectoSerializer.ToJson(p));
        var nr = restaurado.Edificios[0].Niveles[0];

        // Sistema
        Assert.Single(nr.Sistemas);
        Assert.Equal("S-1", nr.Sistemas[0].Nombre);
        Assert.Equal(SistemaUso.Entrepiso, nr.Sistemas[0].Uso);
        // Viga
        Assert.Single(nr.Vigas);
        Assert.Equal("V-Roundtrip", nr.Vigas[0].Nombre);
        Assert.Equal(2, nr.Vigas[0].Tramos.Count);
        Assert.Equal(3, nr.Vigas[0].Apoyos.Count);
        Assert.Equal(12.0, nr.Vigas[0].Tramos[0].Cargas[0].Magnitud, precision: 6);
        // Columna
        Assert.Single(nr.Columnas);
        Assert.Equal("C-Roundtrip", nr.Columnas[0].Nombre);
        Assert.Equal(0.35, nr.Columnas[0].Geometria.Base, precision: 6);
        Assert.Equal(2, nr.Columnas[0].Acero.Capas.Count);
        Assert.Single(nr.Columnas[0].Demandas);
        Assert.Equal(850.0, nr.Columnas[0].Demandas[0].Pu, precision: 6);
        // Zapata
        Assert.Single(nr.Zapatas);
        Assert.Equal("Z-Roundtrip", nr.Zapatas[0].Nombre);
        Assert.Equal(1.80, nr.Zapatas[0].Dimensiones.LargoB, precision: 6);
        Assert.Equal(180.0, nr.Zapatas[0].Suelo.PresionAdmisible, precision: 6);
        Assert.Single(nr.Zapatas[0].Cargas);
        Assert.Equal(50.0, nr.Zapatas[0].Cargas[0].Mx, precision: 6);
    }
}
