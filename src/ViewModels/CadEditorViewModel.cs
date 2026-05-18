using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Services;

namespace LosasPlus.ViewModels;

/// <summary>
/// ViewModel del modo <b>Plano CAD</b> (Fase 1.B del <c>PLAN_CAD_V1.md</c>).
///
/// <para>
/// Coordina la importación de planos <c>.DXF</c> y expone el
/// <see cref="PlanoReferencia"/> resultante para que el <c>CadCanvasHost</c>
/// lo dibuje. Las losas siguen viniendo del <b>SSOT</b> (<see cref="Sistema.Losas"/>
/// del sistema activo) — el CAD es una vista más sobre la misma colección, no
/// un estado paralelo.
/// </para>
/// </summary>
public sealed class CadEditorViewModel : INotifyPropertyChanged
{
    private readonly IPlanoImporter _importer;
    private readonly Func<Sistema> _getSistemaActivo;

    public CadEditorViewModel(Func<Sistema> getSistemaActivo)
        : this(getSistemaActivo, new DxfImportService())
    {
    }

    /// <summary>Constructor con inyección del importador — útil para tests.</summary>
    public CadEditorViewModel(Func<Sistema> getSistemaActivo, IPlanoImporter importer)
    {
        _getSistemaActivo = getSistemaActivo ?? throw new ArgumentNullException(nameof(getSistemaActivo));
        _importer = importer ?? throw new ArgumentNullException(nameof(importer));
        ImportarDxfCommand = new RelayCommand(_ => ImportarDxf());
    }

    // ---- Plano DXF importado ----

    private PlanoReferencia? _plano;
    /// <summary>Plano de referencia importado del .DXF. Null hasta que se importe uno.</summary>
    public PlanoReferencia? Plano
    {
        get => _plano;
        private set
        {
            _plano = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TienePlano));
        }
    }

    /// <summary>True si hay un plano DXF cargado.</summary>
    public bool TienePlano => _plano is { EstaVacio: false };

    // ---- Acceso al SSOT (sistema activo + sus losas) ----

    /// <summary>Sistema activo — fuente de las losas a dibujar en el lienzo.</summary>
    public Sistema SistemaActivo => _getSistemaActivo();

    /// <summary>Colección de losas del sistema activo (el SSOT, no una copia).</summary>
    public ObservableCollection<Losa> Losas => SistemaActivo.Losas;

    // ---- Estado de la última importación (mensaje para la UI) ----

    private string _estadoImportacion = "Sin plano DXF cargado. Usá «Importar DXF…» para abrir uno.";
    public string EstadoImportacion
    {
        get => _estadoImportacion;
        private set { _estadoImportacion = value; OnPropertyChanged(); }
    }

    // ---- Comando: importar un .DXF ----

    /// <summary>Abre un <c>OpenFileDialog</c> filtrado a .dxf e importa el plano.</summary>
    public ICommand ImportarDxfCommand { get; }

    private void ImportarDxf()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Importar plano DXF",
            Filter = "Planos AutoCAD DXF (*.dxf)|*.dxf|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var plano = _importer.Importar(dlg.FileName);
            Plano = plano;
            EstadoImportacion =
                $"✓ {plano.NombreArchivo} — {plano.CantidadEntidades} entidad(es), " +
                $"{plano.Ancho:0.0}×{plano.Alto:0.0} m (unidad origen: {plano.UnidadOriginal}).";
        }
        catch (FileNotFoundException ex)
        {
            EstadoImportacion = $"✕ Archivo no encontrado: {ex.Message}";
        }
        catch (ArgumentException ex)
        {
            EstadoImportacion = $"✕ Ruta inválida: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            EstadoImportacion = $"✕ No se pudo importar el DXF: {ex.Message}";
        }
        catch (Exception ex)
        {
            // Red de seguridad — cualquier fallo inesperado se reporta sin crashear.
            EstadoImportacion = $"✕ Error inesperado al importar: {ex.Message}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
