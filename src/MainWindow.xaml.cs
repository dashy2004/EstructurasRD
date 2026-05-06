using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.ViewModels;
using Microsoft.Win32;

namespace LosasPlus;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();

        // Cuando el usuario clickea un icono del catálogo, aplicar el tipo a la fila seleccionada.
        TiposPanel.TipoSelected = codigo =>
        {
            if (LosasGrid.SelectedItem is Losa l)
            {
                l.Tipo = codigo;
                Vm.Log($"Tipo de losa #{l.Id} cambiado a {codigo} ({TipoLosa.Catalogo[codigo].Descripcion})");
                Vm.RefreshDLContent();
            }
            else
            {
                Vm.Log("Seleccioná primero una fila en la grilla para aplicarle el tipo.");
            }
        };
    }

    private void OnBrowseLosasExe(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Selecciona Losas.exe",
            Filter = "Ejecutables (*.exe)|*.exe",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == true) Vm.LosasExePath = dlg.FileName;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Abrir archivo .DL",
            Filter = "Archivos de datos Losas (*.DL)|*.DL|Todos los archivos|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == true) Vm.AbrirDL(dlg.FileName);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Guardar archivo .DL",
            Filter = "Archivos de datos Losas (*.DL)|*.DL",
            DefaultExt = ".DL",
            FileName = Vm.DLPath != null ? Path.GetFileName(Vm.DLPath) : (Vm.Sistema.Nombre + ".DL")
        };
        if (dlg.ShowDialog() == true) await Vm.GuardarDLAsync(dlg.FileName);
    }

    private async void OnLaunchLosasExeClick(object sender, RoutedEventArgs e)
    {
        await Vm.LanzarLosasExeAsync();
    }

    private async void OnImportTxtClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Importar archivo .TXT (salida de Losas.exe)",
            Filter = "Salidas Losas (*.TXT)|*.TXT|Todos los archivos|*.*",
            CheckFileExists = true,
            InitialDirectory = Vm.DLPath != null ? Path.GetDirectoryName(Vm.DLPath) : null
        };
        if (dlg.ShowDialog() == true) await Vm.ImportarTxtAsync(dlg.FileName);
    }

    private async void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Exportar CSV",
            Filter = "CSV separado por ; (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = (Vm.Sistema.Nombre ?? "losas") + ".csv"
        };
        if (dlg.ShowDialog() == true) await Vm.ExportarCsvAsync(dlg.FileName);
    }

    private async void OnExportXlsxClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Exportar a Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            FileName = (Vm.Sistema.Nombre ?? "losas") + ".xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        // Capturar PNG del Canvas (mejor esfuerzo: si falla, exportamos sin esquema).
        byte[]? png = null;
        try { png = Diagram?.CaptureCanvasPng(); }
        catch (System.Exception ex) { Vm.Log("No se pudo capturar el esquema: " + ex.Message); }

        await Vm.ExportarXlsxAsync(dlg.FileName, png);
    }

    private void OnTrustPluginClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string fullPath)
            Vm.Plugins.TrustPlugin(fullPath, Vm.Log);
    }

    private void OnRevokeTrustClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string fullPath)
            Vm.Plugins.RevokeTrust(fullPath, Vm.Log);
    }

    private void OnAddLosa(object sender, RoutedEventArgs e) => Vm.AgregarLosa();

    private void OnPasteFromExcelClick(object sender, RoutedEventArgs e)
    {
        var dlg = new LosasPlus.Views.PasteExcelDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultLosas is null) return;

        if (dlg.ModoReemplazar)
        {
            Vm.SistemaActivo.Losas.Clear();
            foreach (var l in dlg.ResultLosas) Vm.SistemaActivo.Losas.Add(l);
            Vm.Log($"Pegado de Excel: {dlg.ResultLosas.Count} losas reemplazaron al sistema actual");
        }
        else
        {
            int offset = Vm.SistemaActivo.Losas.Count > 0 ? Vm.SistemaActivo.Losas.Max(l => l.Id) : 0;
            int next = offset + 1;
            foreach (var l in dlg.ResultLosas) { l.Id = next++; Vm.SistemaActivo.Losas.Add(l); }
            Vm.Log($"Pegado de Excel: {dlg.ResultLosas.Count} losas agregadas (IDs {offset + 1}…{next - 1})");
        }
        Vm.RefreshDLContent();
    }

    private void OnDelLosa(object sender, RoutedEventArgs e)
    {
        if (LosasGrid.SelectedItem is Losa l) Vm.EliminarLosa(l);
    }

    private void OnAddBordeX(object sender, RoutedEventArgs e) => Vm.AgregarBorde(true);
    private void OnAddBordeY(object sender, RoutedEventArgs e) => Vm.AgregarBorde(false);

    private void OnDelBordeX(object sender, RoutedEventArgs e)
    {
        if (Vm.Sistema.BordesX.Count > 0)
            Vm.EliminarBorde(Vm.Sistema.BordesX[^1]);
    }
    private void OnDelBordeY(object sender, RoutedEventArgs e)
    {
        if (Vm.Sistema.BordesY.Count > 0)
            Vm.EliminarBorde(Vm.Sistema.BordesY[^1]);
    }

    private void OnLosasCellEdited(object? sender, DataGridCellEditEndingEventArgs e)
    {
        // Refresca la vista del .DL al editar cualquier celda
        Dispatcher.BeginInvoke(new System.Action(() => Vm.RefreshDLContent()),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnApplyDLText(object sender, RoutedEventArgs e)
    {
        try
        {
            // Persistir a temporal y reusar el parser para validar (soporta multi-sistema)
            var tmp = Path.Combine(Path.GetTempPath(), $"losasplus_{System.Guid.NewGuid():N}.DL");
            File.WriteAllText(tmp, Vm.DLContent, System.Text.Encoding.GetEncoding(1252));
            var sistemas = DLFileService.ReadAll(tmp);
            File.Delete(tmp);

            Vm.Proyecto.Sistemas.Clear();
            foreach (var s in sistemas) Vm.Proyecto.Sistemas.Add(s);
            Vm.SistemaActivo = sistemas.First();
            Vm.Log($"Modelo actualizado desde el editor .DL ({sistemas.Count} sistema(s))");
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"El texto .DL no es válido:\n\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnAddSistema(object sender, RoutedEventArgs e) => Vm.AgregarSistema();

    private void OnNewProjectClick(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Seleccioná una carpeta vacía para el nuevo proyecto",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var carpeta = dlg.SelectedPath;
        var nombre = System.IO.Path.GetFileName(carpeta);
        try
        {
            var p = LosasPlus.Services.ProyectoService.CrearProyecto(carpeta, nombre);
            Vm.AbrirProyecto(p.Archivo);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Error creando proyecto:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenProjectClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Abrir manifest del proyecto",
            Filter = "Manifest LosasPlus (proyecto.lpx.json)|proyecto.lpx.json|JSON|*.json|Todos|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() == true) Vm.AbrirProyecto(dlg.FileName);
    }

    private void OnSaveProjectClick(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Seleccioná la carpeta donde guardar el proyecto (un .DL por cada sistema)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        // Si ya hay un proyecto guardado, sugerir su carpeta
        if (!string.IsNullOrEmpty(Vm.Proyecto.CarpetaProyecto))
            dlg.SelectedPath = Vm.Proyecto.CarpetaProyecto;
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            Vm.GuardarProyecto(dlg.SelectedPath);
    }

    private void OnDeleteSistema(object sender, RoutedEventArgs e)
    {
        if (SistemasList?.SelectedItem is LosasPlus.Models.Sistema s) Vm.EliminarSistema(s);
    }

    private async void OnReloadPlugins(object sender, RoutedEventArgs e)
    {
        await Vm.Plugins.LoadAllAsync(Vm.Log);
        await Vm.Plugins.RunHookAsync("load", new PluginContext { Sistema = Vm.Sistema }, Vm.Log);
    }

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        App.ToggleTheme();
        Vm.Log($"Tema cambiado a: {App.CurrentTheme}");
    }

    private void OnAppearanceClick(object sender, RoutedEventArgs e)
    {
        var dlg = new LosasPlus.Views.AppearanceDialog { Owner = this };
        dlg.ShowDialog();
    }

    private void OnContactNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
            { UseShellExecute = true });
        }
        catch { /* ignore */ }
        e.Handled = true;
    }
}
