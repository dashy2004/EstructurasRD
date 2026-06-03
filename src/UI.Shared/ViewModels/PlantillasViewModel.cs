using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using LosasPlus.Persistence;
using Shared.UI.Common;

namespace Shared.UI.ViewModels;

/// <summary>
/// ViewModel del tab Plantillas: lista de plantillas <c>.docx</c> registradas
/// con su flag de predeterminada + commands para importar, marcar como
/// predeterminada, quitar y abrir el archivo en su aplicación asociada.
///
/// <para>
/// Se crea como instancia única en <see cref="MainViewModel"/> (commit 25).
/// El bootstrap de la plantilla bundleada se ejecuta en el ctor de
/// MainViewModel — para cuando este VM se construye, el registry ya tiene
/// al menos la entrada default.
/// </para>
/// </summary>
public sealed class PlantillasViewModel : INotifyPropertyChanged
{
    private PlantillaEntry? _plantillaSeleccionada;
    private string _statusGuardado = "";

    public PlantillasViewModel()
    {
        Plantillas = new ObservableCollection<PlantillaEntry>();
        Recargar();

        ImportarCommand                = new AsyncRelayCommand(Importar);
        MarcarComoPredeterminadaCommand = new RelayCommand(MarcarComoPredeterminada,
            () => _plantillaSeleccionada != null && !_plantillaSeleccionada.EsPredeterminada);
        QuitarCommand                  = new RelayCommand(Quitar,
            () => _plantillaSeleccionada != null);
        AbrirArchivoCommand            = new RelayCommand(AbrirArchivo,
            () => _plantillaSeleccionada != null && File.Exists(_plantillaSeleccionada.Path));
        RecargarCommand                = new RelayCommand(Recargar);
    }

    public ObservableCollection<PlantillaEntry> Plantillas { get; }

    public PlantillaEntry? PlantillaSeleccionada
    {
        get => _plantillaSeleccionada;
        set
        {
            _plantillaSeleccionada = value;
            OnPropertyChanged();
            MarcarComoPredeterminadaCommand.RaiseCanExecuteChanged();
            QuitarCommand.RaiseCanExecuteChanged();
            AbrirArchivoCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Mensaje breve del último resultado (importar / quitar / error).</summary>
    public string StatusGuardado
    {
        get => _statusGuardado;
        private set
        {
            _statusGuardado = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MostrarStatus));
        }
    }

    public bool MostrarStatus => !string.IsNullOrEmpty(_statusGuardado);

    public AsyncRelayCommand ImportarCommand            { get; }
    public RelayCommand MarcarComoPredeterminadaCommand { get; }
    public RelayCommand QuitarCommand                   { get; }
    public RelayCommand AbrirArchivoCommand             { get; }
    public RelayCommand RecargarCommand                 { get; }

    /// <summary>
    /// Re-puebla <see cref="Plantillas"/> desde el registry. Llamada en el
    /// constructor y después de cada acción que muta el registry.
    /// </summary>
    public void Recargar()
    {
        Plantillas.Clear();
        foreach (var p in PlantillaRegistry.Load())
            Plantillas.Add(p);
    }

    private async System.Threading.Tasks.Task Importar()
    {
        var ruta = await Shared.UI.Services.AppServices.Dialogs.OpenFileAsync(
            "Importar plantilla de memoria (.docx)",
            new Shared.UI.Services.FileFilter("Documento Word", new[] { "*.docx" }));
        if (ruta is null) return;

        try
        {
            var nombreSugerido = Path.GetFileNameWithoutExtension(ruta);
            var entry = PlantillaRegistry.AgregarDesdeArchivo(ruta, nombreSugerido);
            Recargar();
            // Re-buscar la entry por id (la instancia en Plantillas es distinta tras recargar).
            foreach (var p in Plantillas)
                if (p.Id == entry.Id) { PlantillaSeleccionada = p; break; }
            StatusGuardado = $"Plantilla \"{entry.Nombre}\" importada.";
        }
        catch (Exception ex)
        {
            StatusGuardado = $"Error al importar: {ex.Message}";
        }
    }

    private void MarcarComoPredeterminada()
    {
        if (_plantillaSeleccionada is null) return;
        var id = _plantillaSeleccionada.Id;
        PlantillaRegistry.MarcarComoPredeterminada(id);
        Recargar();
        foreach (var p in Plantillas)
            if (p.Id == id) { PlantillaSeleccionada = p; break; }
        StatusGuardado = $"\"{_plantillaSeleccionada?.Nombre}\" marcada como predeterminada.";
    }

    private void Quitar()
    {
        if (_plantillaSeleccionada is null) return;
        var nombre = _plantillaSeleccionada.Nombre;
        PlantillaRegistry.Quitar(_plantillaSeleccionada.Id);
        Recargar();
        PlantillaSeleccionada = null;
        StatusGuardado = $"\"{nombre}\" eliminada del registry.";
    }

    private void AbrirArchivo()
    {
        if (_plantillaSeleccionada is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_plantillaSeleccionada.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusGuardado = $"No se pudo abrir el archivo: {ex.Message}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
