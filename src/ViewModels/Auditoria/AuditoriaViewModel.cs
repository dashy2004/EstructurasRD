using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LosasPlus.Auditoria;
using LosasPlus.Models;

namespace LosasPlus.ViewModels.Auditoria;

/// <summary>
/// ViewModel del Dashboard Ejecutivo de Auditoría Global del proyecto
/// (Fase 7, Iteración 2). Cuelga de <c>MainViewModel</c> y consume el
/// motor estático <see cref="ProyectoAuditorEngine"/> para exponer al
/// shell:
///
/// <list type="bullet">
/// <item>El último <see cref="InformeAuditoriaGlobal"/> generado.</item>
/// <item>KPIs derivados (cantidad de advertencias y errores, ratios
/// D/C máximos por tipo de elemento, ratio global y bandera de
/// conformidad).</item>
/// <item>Una colección filtrable por severidad
/// (<see cref="MensajesFiltrados"/>) para el log maestro de alertas.</item>
/// <item>Una bandera <see cref="EjecutandoAnalisis"/> bound al
/// <c>ProgressBar IsIndeterminate</c> de la toolbar mientras corre el
/// motor en hilo secundario.</item>
/// </list>
///
/// <para>
/// El panel es de sólo lectura — no muta el grafo del proyecto — y por
/// eso el constructor no recibe <c>pushUndoSnapshot</c>. El análisis se
/// dispara desde <c>MainViewModel</c> al entrar al modo
/// <c>ModoSidebar.Auditoria</c> y desde Restauración Undo/Redo si el
/// modo activo coincide.
/// </para>
/// </summary>
public sealed class AuditoriaViewModel : INotifyPropertyChanged
{
    private readonly Proyecto _proyecto;

    private InformeAuditoriaGlobal? _informe;
    private TipoMensajeAuditoria? _filtroGravedad;
    private bool _ejecutandoAnalisis;

    public AuditoriaViewModel(Proyecto proyecto)
    {
        _proyecto = proyecto ?? throw new ArgumentNullException(nameof(proyecto));
    }

    // ---- Informe consolidado ----

    /// <summary>
    /// Último informe producido por el motor. <c>null</c> antes del
    /// primer análisis. El setter notifica todas las KPIs derivadas y la
    /// colección filtrada para que un único <c>OnPropertyChanged</c>
    /// repinte el dashboard completo.
    /// </summary>
    public InformeAuditoriaGlobal? Informe
    {
        get => _informe;
        private set
        {
            _informe = value;
            OnPropertyChanged();
            NotificarKPIs();
        }
    }

    /// <summary><c>true</c> tras la primera ejecución exitosa del análisis.</summary>
    public bool HayInforme => _informe is not null;

    // ---- KPIs derivadas (pass-through con default neutro) ----

    /// <summary>
    /// <c>true</c> si el último informe declara el proyecto conforme.
    /// Default <c>true</c> antes del primer análisis para que el chip
    /// arranque en estado "OK" en lugar de "CRÍTICO".
    /// </summary>
    public bool ProyectoConforme => _informe?.ProyectoConforme ?? true;

    /// <summary>Total de mensajes <see cref="TipoMensajeAuditoria.Advertencia"/> del informe.</summary>
    public int CantidadAdvertencias => _informe?.CantidadAdvertencias ?? 0;

    /// <summary>Total de mensajes <see cref="TipoMensajeAuditoria.ErrorFallo"/> del informe.</summary>
    public int CantidadErrores => _informe?.CantidadErrores ?? 0;

    /// <summary>Peor ratio D/C de flexión sobre todas las vigas del proyecto.</summary>
    public double RatioMaximoVigas => _informe?.RatioMaximoVigas ?? 0.0;

    /// <summary>Peor ratio D/C radial sobre todas las demandas de columnas.</summary>
    public double RatioMaximoColumnas => _informe?.RatioMaximoColumnas ?? 0.0;

    /// <summary>Peor ratio estructural ELU sobre todas las zapatas.</summary>
    public double RatioMaximoZapatas => _informe?.RatioMaximoZapatas ?? 0.0;

    /// <summary>Máximo de los tres ratios anteriores — peor caso global.</summary>
    public double RatioMaximoGlobal => _informe?.RatioMaximoGlobal ?? 0.0;

    /// <summary><c>true</c> si <see cref="RatioMaximoVigas"/> &gt; 1.0 — DataTrigger de la barra cromática.</summary>
    public bool RatioVigasExcede => RatioMaximoVigas > 1.0;

    /// <summary><c>true</c> si <see cref="RatioMaximoColumnas"/> &gt; 1.0.</summary>
    public bool RatioColumnasExcede => RatioMaximoColumnas > 1.0;

    /// <summary><c>true</c> si <see cref="RatioMaximoZapatas"/> &gt; 1.0.</summary>
    public bool RatioZapatasExcede => RatioMaximoZapatas > 1.0;

    // ---- Filtro del log maestro ----

    /// <summary>
    /// Filtro activo de severidad sobre la lista de mensajes. <c>null</c>
    /// significa "sin filtro" (muestra todos). El ComboBox lo bindea a un
    /// ítem nullable; cambiarlo notifica <see cref="MensajesFiltrados"/>
    /// para que el DataGrid se repinte sin volver a invocar el motor.
    /// </summary>
    public TipoMensajeAuditoria? FiltroGravedad
    {
        get => _filtroGravedad;
        set
        {
            if (_filtroGravedad == value) return;
            _filtroGravedad = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MensajesFiltrados));
        }
    }

    /// <summary>
    /// Mensajes del informe filtrados por <see cref="FiltroGravedad"/>.
    /// Computado on the fly — no se cachea — para que el setter del
    /// filtro sólo dispare la notificación. Devuelve <c>Array.Empty</c>
    /// si todavía no hay informe.
    ///
    /// <para>
    /// El tipo es <see cref="IReadOnlyList{T}"/> y no <c>IEnumerable</c>
    /// porque el DataGrid del log y el contador de la toolbar bindean a
    /// <c>MensajesFiltrados.Count</c> (los bindings WPF no resuelven
    /// métodos de extensión LINQ).
    /// </para>
    /// </summary>
    public IReadOnlyList<MensajeAuditoria> MensajesFiltrados
    {
        get
        {
            if (_informe is null) return Array.Empty<MensajeAuditoria>();
            if (_filtroGravedad is null) return _informe.Mensajes;
            return _informe.Mensajes.Where(m => m.Tipo == _filtroGravedad).ToList();
        }
    }

    // ---- Estado del análisis ----

    /// <summary>
    /// <c>true</c> mientras el motor corre en hilo secundario. Bound al
    /// <c>Visibility</c> del <c>ProgressBar IsIndeterminate</c> de la
    /// toolbar para dar feedback visual al usuario.
    /// </summary>
    public bool EjecutandoAnalisis
    {
        get => _ejecutandoAnalisis;
        private set
        {
            if (_ejecutandoAnalisis == value) return;
            _ejecutandoAnalisis = value;
            OnPropertyChanged();
        }
    }

    // ---- Ejecución del análisis ----

    /// <summary>
    /// Dispara una nueva auditoría del proyecto en hilo secundario y
    /// publica el informe resultante. Captura el contexto de
    /// sincronización para que la notificación de propiedades reanude en
    /// el hilo de UI cuando se invoca desde la app real.
    ///
    /// <para>
    /// Sin cancelación cooperativa: el motor es lineal en el número de
    /// elementos del proyecto y completa en milisegundos sobre proyectos
    /// realistas. Si una excepción no anticipada ocurre, el informe se
    /// reemplaza por <see cref="InformeAuditoriaGlobal.Vacio"/> para que
    /// el dashboard quede en un estado neutro consistente.
    /// </para>
    /// </summary>
    public async Task ActualizarAuditoriaAsync()
    {
        EjecutandoAnalisis = true;
        try
        {
            var resultado = await Task.Run(
                () => ProyectoAuditorEngine.AuditarProyecto(_proyecto));
            Informe = resultado;
        }
        catch (Exception)
        {
            Informe = InformeAuditoriaGlobal.Vacio;
        }
        finally
        {
            EjecutandoAnalisis = false;
        }
    }

    // ---- Helpers ----

    /// <summary>
    /// Refresca el bloque de KPIs derivadas, la bandera
    /// <see cref="HayInforme"/> y la colección filtrada. Llamado desde
    /// el setter de <see cref="Informe"/>.
    /// </summary>
    private void NotificarKPIs()
    {
        OnPropertyChanged(nameof(HayInforme));
        OnPropertyChanged(nameof(ProyectoConforme));
        OnPropertyChanged(nameof(CantidadAdvertencias));
        OnPropertyChanged(nameof(CantidadErrores));
        OnPropertyChanged(nameof(RatioMaximoVigas));
        OnPropertyChanged(nameof(RatioMaximoColumnas));
        OnPropertyChanged(nameof(RatioMaximoZapatas));
        OnPropertyChanged(nameof(RatioMaximoGlobal));
        OnPropertyChanged(nameof(RatioVigasExcede));
        OnPropertyChanged(nameof(RatioColumnasExcede));
        OnPropertyChanged(nameof(RatioZapatasExcede));
        OnPropertyChanged(nameof(MensajesFiltrados));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
