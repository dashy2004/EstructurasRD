using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LosasPlus.Models;
using LosasPlus.Validation;
using MemoriaPlus.Common;

namespace MemoriaPlus.ViewModels;

/// <summary>
/// Filtro activo del panel de validación normativa. Mapea a los pills del
/// header del panel ("Todas", "Errores", "Observaciones").
/// </summary>
public enum ValidacionFiltro
{
    Todas,
    Errores,
    Observaciones,
}

/// <summary>
/// ViewModel del panel de validación normativa. Mantiene el reporte actual,
/// los conteos para el indicador del top bar, el filtro activo, y los
/// commands de re-validar / copiar reporte / abrir-cerrar panel.
///
/// <para>
/// Se crea un único instancia desde <see cref="MainViewModel"/> y se mantiene
/// vivo durante toda la sesión. Cuando cambia <c>ProyectoActivo</c>, el VM
/// se re-valida vía <see cref="RevalidarPara"/>.
/// </para>
/// </summary>
public sealed class ValidacionViewModel : INotifyPropertyChanged
{
    private readonly ValidationEngine _engine;
    private Proyecto? _proyecto;
    private ValidationReport _reporte;
    private ValidacionFiltro _filtroActivo = ValidacionFiltro.Todas;
    private bool _panelAbierto;

    public ValidacionViewModel() : this(ValidationEngine.Default()) { }

    public ValidacionViewModel(ValidationEngine engine)
    {
        _engine = engine;
        _reporte = ValidationReport.Vacio;
        Issues = new ObservableCollection<ValidationIssue>();
        IssuesFiltrados = new ObservableCollection<ValidationIssue>();

        RevalidarCommand    = new RelayCommand(Revalidar,    () => _proyecto != null);
        CopiarReporteCommand = new RelayCommand(CopiarReporte, () => Issues.Count > 0);
        AbrirPanelCommand   = new RelayCommand(() => PanelAbierto = true);
        CerrarPanelCommand  = new RelayCommand(() => PanelAbierto = false);
        FiltrarCommand      = new RelayCommand<string>(SetFiltroDesdeString);
    }

    /// <summary>Re-apunta el VM al proyecto dado y dispara una validación.</summary>
    public void RevalidarPara(Proyecto proyecto)
    {
        _proyecto = proyecto;
        Revalidar();
        RevalidarCommand.RaiseCanExecuteChanged();
    }

    public ValidationReport Reporte
    {
        get => _reporte;
        private set
        {
            _reporte = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalErrores));
            OnPropertyChanged(nameof(TotalObservaciones));
            OnPropertyChanged(nameof(TotalConformes));
            OnPropertyChanged(nameof(ResumenIndicador));
            OnPropertyChanged(nameof(TieneIssues));
            OnPropertyChanged(nameof(ColorIndicador));
            OnPropertyChanged(nameof(FechaGeneradoTexto));
            CopiarReporteCommand.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<ValidationIssue> Issues { get; }
    public ObservableCollection<ValidationIssue> IssuesFiltrados { get; }

    public int TotalErrores       => _reporte.Errores;
    public int TotalObservaciones => _reporte.Observaciones;
    public int TotalConformes     => _reporte.Conformes;
    public bool TieneIssues       => _reporte.Issues.Count > 0;

    /// <summary>
    /// Texto del chip indicador en el top bar. Reglas:
    ///   X errores + Y obs → "Validación R-001 · X errores · Y obs"
    ///   solo errores      → "Validación R-001 · X errores"
    ///   solo obs          → "Validación R-001 · Y observaciones"
    ///   ningún issue      → "Validación R-001 · OK"
    /// </summary>
    public string ResumenIndicador
    {
        get
        {
            if (!TieneIssues) return "Validación R-001 · OK";
            var partes = new List<string>();
            if (TotalErrores > 0)       partes.Add($"{TotalErrores} {(TotalErrores == 1 ? "error" : "errores")}");
            if (TotalObservaciones > 0) partes.Add($"{TotalObservaciones} obs");
            return "Validación R-001 · " + string.Join(" · ", partes);
        }
    }

    /// <summary>
    /// Color semántico del chip indicador: Success (verde) si limpio, Error
    /// (rojo) si hay >=1 error, Warning (naranja) si solo hay observaciones.
    /// Útil para el DataTrigger del XAML.
    /// </summary>
    public string ColorIndicador
    {
        get
        {
            if (TotalErrores > 0)       return "Error";
            if (TotalObservaciones > 0) return "Warning";
            return "Success";
        }
    }

    public string FechaGeneradoTexto =>
        _reporte.Generado == default
            ? ""
            : _reporte.Generado.ToString("dd/MM HH:mm");

    public ValidacionFiltro FiltroActivo
    {
        get => _filtroActivo;
        set
        {
            if (_filtroActivo == value) return;
            _filtroActivo = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EsFiltroTodas));
            OnPropertyChanged(nameof(EsFiltroErrores));
            OnPropertyChanged(nameof(EsFiltroObservaciones));
            ActualizarFiltrados();
        }
    }

    public bool EsFiltroTodas         => _filtroActivo == ValidacionFiltro.Todas;
    public bool EsFiltroErrores       => _filtroActivo == ValidacionFiltro.Errores;
    public bool EsFiltroObservaciones => _filtroActivo == ValidacionFiltro.Observaciones;

    public bool PanelAbierto
    {
        get => _panelAbierto;
        set { _panelAbierto = value; OnPropertyChanged(); }
    }

    public RelayCommand RevalidarCommand    { get; }
    public RelayCommand CopiarReporteCommand { get; }
    public RelayCommand AbrirPanelCommand   { get; }
    public RelayCommand CerrarPanelCommand  { get; }
    public RelayCommand<string> FiltrarCommand { get; }

    /// <summary>
    /// Corre el engine sobre el proyecto activo y refresca observables. Es
    /// idempotente — puede llamarse en cada PropertyChanged del modelo.
    /// </summary>
    public void Revalidar()
    {
        if (_proyecto is null)
        {
            Reporte = ValidationReport.Vacio;
            Issues.Clear();
            ActualizarFiltrados();
            return;
        }

        Reporte = _engine.Validar(_proyecto);
        Issues.Clear();
        foreach (var issue in _reporte.Issues) Issues.Add(issue);
        ActualizarFiltrados();
    }

    private void ActualizarFiltrados()
    {
        IssuesFiltrados.Clear();
        IEnumerable<ValidationIssue> q = Issues;
        q = _filtroActivo switch
        {
            ValidacionFiltro.Errores       => q.Where(i => i.Severidad == ValidationSeverity.Error),
            ValidacionFiltro.Observaciones => q.Where(i => i.Severidad == ValidationSeverity.Warning),
            _ => q,
        };
        foreach (var i in q) IssuesFiltrados.Add(i);
    }

    private void SetFiltroDesdeString(string? clave)
    {
        FiltroActivo = clave switch
        {
            "Errores"       => ValidacionFiltro.Errores,
            "Observaciones" => ValidacionFiltro.Observaciones,
            _               => ValidacionFiltro.Todas,
        };
    }

    private void CopiarReporte()
    {
        try
        {
            System.Windows.Clipboard.SetText(_reporte.AReporteTexto());
        }
        catch
        {
            // Clipboard puede fallar en headless / smoke runs; ignorar.
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
