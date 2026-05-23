using System.Collections.Generic;
using LosasPlus.Topologia;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests del <see cref="SeleccionService"/> (Fase 3D-I3 del Plan Maestro
/// de Expansión 3D). Validan las dos defensas críticas del puente de
/// selección 3D ↔ paneles 2D: <b>anti-recursión</b> (flag
/// <c>_actualizando</c>) e <b>idempotencia</b> (no re-publica eventos al
/// recibir el mismo <see cref="DomainKey"/>).
/// </summary>
public class SeleccionServiceTests
{
    // ===================================================================
    // ESTADO INICIAL
    // ===================================================================

    [Fact]
    public void Instanciacion_Limpia_ElementoSeleccionado_Es_Null()
    {
        var svc = new SeleccionService();

        Assert.Null(svc.ElementoSeleccionado);
        Assert.False(svc.EstaActualizando);
    }

    // ===================================================================
    // MUTACIÓN BÁSICA
    // ===================================================================

    [Fact]
    public void FijarSeleccion_Actualiza_ElementoSeleccionado()
    {
        var svc = new SeleccionService();
        var key = new DomainKey(TipoElemento.Columna, 7);

        svc.FijarSeleccion(key, this);

        Assert.Equal(key, svc.ElementoSeleccionado);
    }

    [Fact]
    public void FijarSeleccion_Dispara_Evento_OnSeleccionCambiada()
    {
        var svc = new SeleccionService();
        DomainKey? recibido = null;
        int conteoInvocaciones = 0;
        svc.OnSeleccionCambiada += k =>
        {
            recibido = k;
            conteoInvocaciones++;
        };

        var key = new DomainKey(TipoElemento.Viga, 3);
        svc.FijarSeleccion(key, this);

        Assert.Equal(1, conteoInvocaciones);
        Assert.Equal(key, recibido);
    }

    // ===================================================================
    // IDEMPOTENCIA
    // ===================================================================

    [Fact]
    public void FijarSeleccion_Idempotente_AnteMismoElemento_NoDisparaEventos()
    {
        var svc = new SeleccionService();
        var key = new DomainKey(TipoElemento.Zapata, 5);
        int conteo = 0;
        svc.OnSeleccionCambiada += _ => conteo++;

        svc.FijarSeleccion(key, this);
        svc.FijarSeleccion(key, this);   // mismo elemento
        svc.FijarSeleccion(key, this);   // mismo elemento

        Assert.Equal(1, conteo);
        Assert.Equal(key, svc.ElementoSeleccionado);
    }

    [Fact]
    public void FijarSeleccion_Null_A_Null_Es_Idempotente()
    {
        var svc = new SeleccionService();
        int conteo = 0;
        svc.OnSeleccionCambiada += _ => conteo++;

        svc.FijarSeleccion(null, this);
        svc.FijarSeleccion(null, this);

        Assert.Equal(0, conteo);
        Assert.Null(svc.ElementoSeleccionado);
    }

    [Fact]
    public void FijarSeleccion_Tipos_Distintos_Mismo_Id_Son_Diferentes()
    {
        // Columna(Id=1) y Viga(Id=1) son DomainKeys distintos por el campo Tipo;
        // ambas selecciones deben publicarse.
        var svc = new SeleccionService();
        var keyCol  = new DomainKey(TipoElemento.Columna, 1);
        var keyViga = new DomainKey(TipoElemento.Viga, 1);
        int conteo = 0;
        svc.OnSeleccionCambiada += _ => conteo++;

        svc.FijarSeleccion(keyCol, this);
        svc.FijarSeleccion(keyViga, this);

        Assert.Equal(2, conteo);
        Assert.Equal(keyViga, svc.ElementoSeleccionado);
    }

    // ===================================================================
    // ANTI-RECURSIÓN
    // ===================================================================

    [Fact]
    public void FijarSeleccion_RompeBucles_EvitaRecursionInfinita()
    {
        // Simula dos componentes A↔B que intentan llamarse recursivamente
        // durante la propagación del evento. Sin el flag _actualizando esto
        // generaría una StackOverflowException; con el flag, la re-entrada
        // se ignora silenciosamente.
        var svc = new SeleccionService();
        var keyA = new DomainKey(TipoElemento.Columna, 1);
        var keyB = new DomainKey(TipoElemento.Columna, 2);
        int conteoEventosA = 0, conteoEventosB = 0;

        svc.OnSeleccionCambiada += k =>
        {
            if (k.HasValue && k.Value.Equals(keyA))
            {
                conteoEventosA++;
                // Componente B reacciona al evento de A intentando
                // imponer su propia selección — re-entrada al servicio.
                svc.FijarSeleccion(keyB, "B");
            }
            else if (k.HasValue && k.Value.Equals(keyB))
            {
                conteoEventosB++;
                // Componente A reacciona al evento de B intentando
                // restaurar A — re-entrada al servicio.
                svc.FijarSeleccion(keyA, "A");
            }
        };

        // Disparar el bucle. Sin protección esto desbordaría el stack.
        svc.FijarSeleccion(keyA, "Origen");

        // Sólo un evento debe haberse propagado: el inicial. Los re-
        // entrantes fueron bloqueados por el flag _actualizando.
        Assert.Equal(1, conteoEventosA);
        Assert.Equal(0, conteoEventosB);
        // El estado del servicio queda en el primer valor establecido.
        Assert.Equal(keyA, svc.ElementoSeleccionado);
        // Tras la propagación, el flag debe estar bajado.
        Assert.False(svc.EstaActualizando);
    }

    [Fact]
    public void EstaActualizando_EsTrue_Solo_Dentro_Del_Evento()
    {
        var svc = new SeleccionService();
        bool snapshotFlag = false;
        svc.OnSeleccionCambiada += _ => snapshotFlag = svc.EstaActualizando;

        Assert.False(svc.EstaActualizando);   // antes de la mutación
        svc.FijarSeleccion(new DomainKey(TipoElemento.Muro, 9), this);

        Assert.True(snapshotFlag);            // dentro del callback el flag estaba arriba
        Assert.False(svc.EstaActualizando);   // tras la propagación, abajo
    }

    // ===================================================================
    // LIMPIAR SELECCIÓN
    // ===================================================================

    [Fact]
    public void LimpiarSeleccion_ReseteaEstadoA_Null_Correctamente()
    {
        var svc = new SeleccionService();
        var key = new DomainKey(TipoElemento.Zapata, 4);
        svc.FijarSeleccion(key, this);

        DomainKey? recibido = key;
        svc.OnSeleccionCambiada += k => recibido = k;

        svc.LimpiarSeleccion(this);

        Assert.Null(svc.ElementoSeleccionado);
        Assert.Null(recibido);
    }

    [Fact]
    public void LimpiarSeleccion_Cuando_NadaSeleccionado_Es_NoOp()
    {
        var svc = new SeleccionService();
        int conteo = 0;
        svc.OnSeleccionCambiada += _ => conteo++;

        svc.LimpiarSeleccion(this);

        Assert.Equal(0, conteo);
        Assert.Null(svc.ElementoSeleccionado);
    }

    // ===================================================================
    // SUSCRIPCIONES MÚLTIPLES Y SECUENCIAS
    // ===================================================================

    [Fact]
    public void Multiples_Suscriptores_Reciben_El_Mismo_Evento()
    {
        var svc = new SeleccionService();
        var key = new DomainKey(TipoElemento.Viga, 11);
        var recibidos = new List<DomainKey?>();
        svc.OnSeleccionCambiada += k => recibidos.Add(k);
        svc.OnSeleccionCambiada += k => recibidos.Add(k);
        svc.OnSeleccionCambiada += k => recibidos.Add(k);

        svc.FijarSeleccion(key, this);

        Assert.Equal(3, recibidos.Count);
        Assert.All(recibidos, r => Assert.Equal(key, r));
    }

    [Fact]
    public void Secuencia_De_Selecciones_Publica_Cada_Cambio_Una_Vez()
    {
        var svc = new SeleccionService();
        var publicados = new List<DomainKey?>();
        svc.OnSeleccionCambiada += k => publicados.Add(k);

        svc.FijarSeleccion(new DomainKey(TipoElemento.Columna, 1), this);
        svc.FijarSeleccion(new DomainKey(TipoElemento.Viga,    2), this);
        svc.FijarSeleccion(new DomainKey(TipoElemento.Zapata,  3), this);
        svc.FijarSeleccion(null, this);
        svc.FijarSeleccion(new DomainKey(TipoElemento.Muro,    4), this);

        Assert.Equal(5, publicados.Count);
        Assert.Equal(new DomainKey(TipoElemento.Muro, 4), svc.ElementoSeleccionado);
    }

    // ===================================================================
    // INTEROPERABILIDAD CON EL MAIN VIEW MODEL
    // (validación headless del cableado de la Parte C)
    // ===================================================================

    [Fact]
    public void MainViewModel_Expone_SeleccionService_Inyectado_En_Viewport3D()
    {
        var main = new MainViewModel();

        Assert.NotNull(main.Seleccion);
        Assert.Same(main.Seleccion, main.Viewport3D.SeleccionService);
    }

    [Fact]
    public void SeleccionNull_MantieneModoSidebar_Invariable_En_Shell()
    {
        var main = new MainViewModel();
        main.ModoActivo = ModoSidebar.Vista3D;

        main.Seleccion.FijarSeleccion(null, this);

        // Pasar null no cambia el modo (no-op explícito del handler).
        Assert.Equal(ModoSidebar.Vista3D, main.ModoActivo);
    }
}
