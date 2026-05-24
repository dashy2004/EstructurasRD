using LosasPlus.Cargas;
using LosasPlus.Interop.SAF;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Topologia;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests.Interop;

/// <summary>
/// Tests del método público
/// <see cref="RatioCalculator.CalcularRatioPorElemento"/> introducido
/// en Liga B paso B4. Cobertura: happy path Viga, defensa ante
/// Id huérfano, tipo Muro no soportado, viga sin tramos.
/// </summary>
public class RatioCalculatorPerElementTests
{
    private static Proyecto ConstruirProyectoConViga()
    {
        // Usar el factory canónico: ASCE 7-05 (4 casos CM/CV/SX/SY + 8
        // combinaciones). Garantiza que VigaContinuaEngine encuentre
        // las combinaciones que necesita para construir la envolvente.
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        var edif = new Edificio();
        var nivel = new Nivel { Nombre = "PB", Cota = 0.0 };
        var viga = new Viga { Id = 1, Nombre = "V-1" };
        viga.Tramos.Add(new TramoViga { Longitud = 5.0, Base = 0.30, Peralte = 0.50 });
        viga.Apoyos.Add(new ApoyoViga(0.0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(5.0, TipoApoyo.Fijo));
        // Carga distribuida básica para producir momentos.
        viga.Tramos[0].Cargas.Add(new CargaElemento
        {
            Tipo       = TipoCargaElemento.Distribuida,
            CodigoCaso = "CM",
            Magnitud   = 10.0,   // kN/m
        });
        nivel.Vigas.Add(viga);
        edif.Niveles.Add(nivel);
        proyecto.Edificios.Add(edif);
        return proyecto;
    }

    [Fact]
    public void CalcularRatioPorElemento_VigaConDemandas_RetornaDoubleSinThrow()
    {
        // El método NUNCA debe lanzar. Para una viga con cargas el
        // resultado puede ser un finito > 0 (si el refuerzo default
        // soporta) o NaN (si la sección queda subreforzada y el motor
        // devuelve 0.0 → mapeado a NaN por B4). Ambos son válidos —
        // lo crítico es la NO-PROPAGACIÓN de excepciones.
        var proyecto = ConstruirProyectoConViga();
        var key = new DomainKey(TipoElemento.Viga, 1);

        double ratio = RatioCalculator.CalcularRatioPorElemento(proyecto, key);

        // No Infinity (eso indica división por cero no catchada).
        Assert.False(double.IsInfinity(ratio),
            $"Esperaba un double válido (NaN o finito), no Infinity. Real: {ratio}");
    }

    [Fact]
    public void CalcularRatioPorElemento_IdHuerfano_RetornaNaN()
    {
        var proyecto = new Proyecto();
        proyecto.Edificios.Add(new Edificio());
        // Proyecto sin entidades → cualquier Id es huérfano.

        double ratio = RatioCalculator.CalcularRatioPorElemento(
            proyecto, new DomainKey(TipoElemento.Viga, 999));

        Assert.True(double.IsNaN(ratio),
            $"Id huérfano debe devolver NaN (real: {ratio}).");
    }

    [Fact]
    public void CalcularRatioPorElemento_TipoMuro_RetornaNaN()
    {
        // Tipo Muro no soportado por el calculator → NaN sin throw.
        var proyecto = new Proyecto();
        proyecto.Edificios.Add(new Edificio());

        double ratio = RatioCalculator.CalcularRatioPorElemento(
            proyecto, new DomainKey(TipoElemento.Muro, 1));

        Assert.True(double.IsNaN(ratio),
            $"TipoMuro debe devolver NaN (real: {ratio}).");
    }

    [Fact]
    public void CalcularRatioPorElemento_VigaSinTramos_RetornaNaN()
    {
        // Viga existente pero sin tramos → CalcularRatioVigaSeguro
        // retorna 0.0 (que el método público mapea a NaN — "no
        // calculable" — para que la UI distinga del "calculado y
        // limítrofe").
        var proyecto = new Proyecto();
        var edif = new Edificio();
        var nivel = new Nivel { Nombre = "PB" };
        nivel.Vigas.Add(new Viga { Id = 1, Nombre = "V-vacia" });
        // Sin Tramos.Add(...) → vigas count=0.
        edif.Niveles.Add(nivel);
        proyecto.Edificios.Add(edif);

        double ratio = RatioCalculator.CalcularRatioPorElemento(
            proyecto, new DomainKey(TipoElemento.Viga, 1));

        Assert.True(double.IsNaN(ratio),
            $"Viga sin tramos debe devolver NaN (real: {ratio}).");
    }
}
