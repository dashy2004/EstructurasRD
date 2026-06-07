using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LosasPlus.Models;
using LosasPlus.Services;
using MemoriaPlus.Common;

namespace MemoriaPlus.ViewModels;

/// <summary>
/// Resultado individual de búsqueda — wrapper que une un elemento del modelo
/// con metadatos útiles para la UI (etiqueta de tipo, subtítulo, identificador
/// para navegación).
/// </summary>
public sealed record BusquedaResultado
{
    public required string Tipo { get; init; }      // "Proyecto" | "Nivel" | "Losa"
    public required string Titulo { get; init; }    // Línea principal en la card
    public string Subtitulo { get; init; } = "";    // Línea secundaria (path, sistema, etc.)
    public string? PathProyecto { get; init; }      // Para resultados de proyecto
    public string? NombreSistema { get; init; }     // Para resultados de sistema o losa
    public int? LosaId { get; init; }               // Para resultados de losa
}

/// <summary>
/// ViewModel del modo Búsqueda Global. Filtra simultáneamente sobre:
/// <list type="bullet">
///   <item><b>Proyectos recientes</b> — match en nombre / ingeniero / CODIA.</item>
///   <item><b>Sistemas (niveles)</b> del proyecto activo — match en nombre.</item>
///   <item><b>Losas</b> del proyecto activo — match en Id (como número o string).</item>
/// </list>
///
/// <para>
/// La búsqueda es case-insensitive y se ejecuta sincrónicamente en cada cambio
/// de <see cref="TextoBusqueda"/> — la cantidad de datos en memoria es chica
/// (12 recientes + el proyecto activo) y no justifica un debounce.
/// </para>
///
/// <para>
/// La navegación final se delega al <see cref="MainViewModel"/> via callbacks
/// configurados al crear el VM (no se referencia directamente para no acoplar).
/// </para>
/// </summary>
public sealed class BusquedaViewModel : INotifyPropertyChanged
{
    private readonly Func<IEnumerable<ProyectoResumen>> _getProyectosRecientes;
    private readonly Func<Proyecto?> _getProyectoActivo;
    private readonly Action<string> _abrirProyectoPorPath;
    private readonly Action<string> _irASistema;            // (nombreSistema)
    private readonly Action<string, int> _irALosa;          // (nombreSistema, losaId)

    private string _textoBusqueda = "";

    public BusquedaViewModel(
        Func<IEnumerable<ProyectoResumen>> getProyectosRecientes,
        Func<Proyecto?> getProyectoActivo,
        Action<string> abrirProyectoPorPath,
        Action<string> irASistema,
        Action<string, int> irALosa)
    {
        _getProyectosRecientes = getProyectosRecientes;
        _getProyectoActivo     = getProyectoActivo;
        _abrirProyectoPorPath  = abrirProyectoPorPath;
        _irASistema            = irASistema;
        _irALosa               = irALosa;

        ResultadosProyectos = new ObservableCollection<BusquedaResultado>();
        ResultadosSistemas  = new ObservableCollection<BusquedaResultado>();
        ResultadosLosas     = new ObservableCollection<BusquedaResultado>();

        IrAResultadoCommand = new RelayCommand<BusquedaResultado>(IrAResultado);
        LimpiarCommand      = new RelayCommand(() => TextoBusqueda = "");
    }

    public string TextoBusqueda
    {
        get => _textoBusqueda;
        set
        {
            if (_textoBusqueda == (value ?? "")) return;
            _textoBusqueda = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(BusquedaVacia));
            ActualizarResultados();
        }
    }

    public bool BusquedaVacia => string.IsNullOrWhiteSpace(_textoBusqueda);

    public ObservableCollection<BusquedaResultado> ResultadosProyectos { get; }
    public ObservableCollection<BusquedaResultado> ResultadosSistemas  { get; }
    public ObservableCollection<BusquedaResultado> ResultadosLosas     { get; }

    public int ConteoProyectos => ResultadosProyectos.Count;
    public int ConteoSistemas  => ResultadosSistemas.Count;
    public int ConteoLosas     => ResultadosLosas.Count;
    public int ConteoTotal     => ConteoProyectos + ConteoSistemas + ConteoLosas;
    public bool HayResultados  => ConteoTotal > 0;

    public RelayCommand<BusquedaResultado> IrAResultadoCommand { get; }
    public RelayCommand LimpiarCommand { get; }

    /// <summary>
    /// Re-corre la búsqueda contra los datos actuales. Llamada también cuando
    /// el proyecto activo cambia (desde MainViewModel) para refrescar
    /// resultados de sistemas / losas sin esperar a que el usuario tipee.
    /// </summary>
    public void Refrescar() => ActualizarResultados();

    private void ActualizarResultados()
    {
        ResultadosProyectos.Clear();
        ResultadosSistemas.Clear();
        ResultadosLosas.Clear();

        if (BusquedaVacia)
        {
            NotificarConteos();
            return;
        }

        var q = _textoBusqueda.Trim();

        // 1) Proyectos recientes
        foreach (var p in _getProyectosRecientes())
        {
            if (p.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase)
                || p.Ingeniero.Contains(q, StringComparison.OrdinalIgnoreCase)
                || p.Codia.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                ResultadosProyectos.Add(new BusquedaResultado
                {
                    Tipo = "Proyecto",
                    Titulo = p.Nombre,
                    Subtitulo = $"{p.Ingeniero} · CODIA {p.Codia} · {p.UltimaEdicion}",
                    PathProyecto = string.IsNullOrEmpty(p.Path) ? null : p.Path,
                });
            }
        }

        // 2) Sistemas y losas del proyecto activo — TODOS los niveles (B2b), no
        //    sólo la fachada Niveles[0]. EnumerarSistemas recorre
        //    Edificios → Niveles → Sistemas.
        var proyecto = _getProyectoActivo();
        if (proyecto is not null)
        {
            foreach (var sistema in ProyectoService.EnumerarSistemas(proyecto))
            {
                if (sistema.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    ResultadosSistemas.Add(new BusquedaResultado
                    {
                        Tipo = "Nivel",
                        Titulo = $"Nivel {sistema.Nombre}",
                        Subtitulo = $"{sistema.Losas.Count} losa(s) · cota +{sistema.CotaMetros:0.00} m · uso {sistema.Uso}",
                        NombreSistema = sistema.Nombre,
                    });
                }
                foreach (var losa in sistema.Losas)
                {
                    var losaIdStr = losa.Id.ToString();
                    var losaLabel = $"L{losa.Id}";
                    if (losaIdStr.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || losaLabel.Contains(q, StringComparison.OrdinalIgnoreCase))
                    {
                        ResultadosLosas.Add(new BusquedaResultado
                        {
                            Tipo = "Losa",
                            Titulo = $"Losa {losaLabel} en nivel {sistema.Nombre}",
                            Subtitulo = $"Tipo {losa.Tipo} · Lx={losa.Lx:0.00} m · Ly={losa.Ly:0.00} m · h={losa.Espesor:0.000} m",
                            NombreSistema = sistema.Nombre,
                            LosaId = losa.Id,
                        });
                    }
                }
            }
        }

        NotificarConteos();
    }

    private void NotificarConteos()
    {
        OnPropertyChanged(nameof(ConteoProyectos));
        OnPropertyChanged(nameof(ConteoSistemas));
        OnPropertyChanged(nameof(ConteoLosas));
        OnPropertyChanged(nameof(ConteoTotal));
        OnPropertyChanged(nameof(HayResultados));
    }

    private void IrAResultado(BusquedaResultado? r)
    {
        if (r is null) return;
        switch (r.Tipo)
        {
            case "Proyecto" when r.PathProyecto is not null:
                _abrirProyectoPorPath(r.PathProyecto);
                break;
            case "Nivel" when r.NombreSistema is not null:
                _irASistema(r.NombreSistema);
                break;
            case "Losa" when r.NombreSistema is not null && r.LosaId is int id:
                _irALosa(r.NombreSistema, id);
                break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
