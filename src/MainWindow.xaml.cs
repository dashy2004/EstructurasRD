using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LosasPlus.Models;
using LosasPlus.Persistence;
using LosasPlus.Services;
using LosasPlus.ViewModels;
using Microsoft.Win32;

namespace LosasPlus;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    /// <summary>
    /// InputBindings de los atajos personalizables aplicados dinámicamente. Los
    /// 4 atajos fijos viven en XAML y nunca se rastrean aquí.
    /// </summary>
    private readonly List<InputBinding> _atajosConfigurables = new();

    public MainWindow()
    {
        InitializeComponent();

        // Cuando el usuario clickea un icono del catálogo, aplicar el tipo a la fila seleccionada.
        TiposPanel.TipoSelected = codigo =>
        {
            // Si hay multi-selección, aplicar a TODAS las seleccionadas.
            if (LosasGrid.SelectedItems.Count > 1)
            {
                Vm.PushUndoSnapshot();
                int n = 0;
                foreach (var item in LosasGrid.SelectedItems)
                {
                    if (item is Losa l) { l.Tipo = codigo; n++; }
                }
                Vm.Log($"Tipo {codigo} aplicado a {n} losas seleccionadas.");
                Vm.RefreshDLContent();
            }
            else if (LosasGrid.SelectedItem is Losa l)
            {
                Vm.PushUndoSnapshot();
                l.Tipo = codigo;
                Vm.Log($"Tipo de losa #{l.Id} cambiado a {codigo} ({TipoLosa.Catalogo[codigo].Descripcion})");
                Vm.RefreshDLContent();
            }
            else
            {
                Vm.Log("Seleccioná primero una fila en la grilla para aplicarle el tipo.");
            }
        };

        // Hook para abrir el panel de atajos modalmente.
        Loaded += (_, __) =>
        {
            if (DataContext is MainViewModel vm)
                vm.OnAbrirShortcuts = AbrirShortcutsPanel;
        };

        // Atajos personalizables: aplicarlos al cargar y rebuildarlos en vivo
        // cuando el usuario los edita en Configuración → Atajos.
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>Abre el modal <see cref="LosasPlus.Views.KeyboardShortcutsWindow"/>.</summary>
    private void AbrirShortcutsPanel()
    {
        var dlg = new LosasPlus.Views.KeyboardShortcutsWindow { Owner = this };
        dlg.ShowDialog();
    }

    // =====================================================================
    // Atajos de teclado personalizables — aplicados en vivo desde atajos.json
    // =====================================================================

    /// <summary>Aplica los atajos guardados al cargar y se suscribe a sus cambios.</summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        AplicarAtajos(AtajosService.Load());
        AtajosService.AtajosCambiados += OnAtajosCambiados;
    }

    /// <summary>Se desuscribe del evento estático al cerrar — evita fugas de memoria.</summary>
    private void OnClosed(object? sender, System.EventArgs e)
    {
        AtajosService.AtajosCambiados -= OnAtajosCambiados;
    }

    /// <summary>
    /// Reacciona a un guardado en Configuración → Atajos: recompone las
    /// InputBindings sin necesidad de reiniciar la app.
    /// </summary>
    private void OnAtajosCambiados(object? sender, AtajosConfig cfg) =>
        Dispatcher.BeginInvoke(new System.Action(() => AplicarAtajos(cfg)));

    /// <summary>
    /// Aplica los atajos personalizables de <paramref name="cfg"/> a las
    /// <c>InputBindings</c> de la ventana. Sólo toca los bindings que él mismo
    /// agregó (rastreados en <c>_atajosConfigurables</c>): los 4 atajos FIJOS
    /// declarados en XAML (Explorador, Undo, Redo, panel de atajos) nunca se
    /// alteran. Gestos malformados se omiten en silencio.
    /// </summary>
    private void AplicarAtajos(AtajosConfig cfg)
    {
        if (DataContext is not MainViewModel vm) return;

        // Quitar sólo los configurables aplicados antes — los fijos quedan.
        foreach (var b in _atajosConfigurables) InputBindings.Remove(b);
        _atajosConfigurables.Clear();

        // Mapeo id de atajo → command del VM (por instancia). AgregarLosa no
        // tiene command en LosasPlus, así que no figura en el mapa.
        var mapa = new Dictionary<string, ICommand?>
        {
            { AtajoIds.NuevoProyecto, vm.NuevoProyectoLpxCommand },
            { AtajoIds.Abrir,         vm.AbrirProyectoLpxCommand },
            { AtajoIds.Guardar,       vm.GuardarProyectoLpxCommand },
            { AtajoIds.GuardarComo,   vm.GuardarComoLpxCommand },
            { AtajoIds.Generar,       vm.GenerarMemoriaCommand },
            { AtajoIds.Busqueda,      vm.IrABusquedaCommand },
        };

        foreach (var (id, command) in mapa)
        {
            if (command is null) continue;
            var gestureStr = cfg.Get(id);
            if (string.IsNullOrWhiteSpace(gestureStr)) continue;

            if (TryParseGesture(gestureStr, out var key, out var mods))
            {
                var kb = new KeyBinding(command, key, mods);
                InputBindings.Add(kb);
                _atajosConfigurables.Add(kb);
            }
        }
    }

    /// <summary>
    /// Parsea un gesto ("Ctrl+Shift+S") a un <c>Key</c> + <c>ModifierKeys</c>.
    /// Tokens separados por '+', modificadores case-insensitive; los dígitos
    /// 0..9 se traducen a Key.D0..D9. Réplica del helper de MemoriaPlus.
    /// </summary>
    private static bool TryParseGesture(string gesture, out Key key, out ModifierKeys mods)
    {
        key = Key.None;
        mods = ModifierKeys.None;
        var tokens = gesture.Split('+',
            System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return false;

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control": mods |= ModifierKeys.Control; break;
                case "shift":   mods |= ModifierKeys.Shift;   break;
                case "alt":     mods |= ModifierKeys.Alt;     break;
                case "win":
                case "windows": mods |= ModifierKeys.Windows; break;
                default: return false;
            }
        }

        var keyToken = tokens[^1];
        if (keyToken.Length == 1 && char.IsDigit(keyToken[0]))
            keyToken = "D" + keyToken;  // "5" → Key.D5

        return System.Enum.TryParse<Key>(keyToken, ignoreCase: true, out key) && key != Key.None;
    }

    /// <summary>
    /// Sincroniza <see cref="MainViewModel.LosasSeleccionadas"/> con el
    /// SelectedItems del DataGrid de losas. El panel de bulk-apply se hace
    /// visible (DataTrigger sobre MostrarBulkPanel) cuando hay ≥ 2 filas.
    /// </summary>
    private void OnLosasSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid dg)
            Vm.ActualizarLosasSeleccionadas(dg.SelectedItems);
    }

    /// <summary>
    /// Click en el botón de ID de una losa: si ModoConectarBordes está activo,
    /// pasa al VM para crear el borde adicional. Si no, no-op (la selección
    /// del DataGrid sigue funcionando para el resto del UI).
    /// </summary>
    private void OnLosaIdClick(object sender, RoutedEventArgs e)
    {
        if (!Vm.ModoConectarBordes) return;
        if (sender is not Button btn) return;
        if (btn.Tag is not Losa l) return;
        Vm.HandleIdClickParaBorde(l.Id);
    }

    /// <summary>
    /// Click en el chip de validación: además de abrir el panel lateral via
    /// command, switch al modo Validación full-screen para que el usuario vea
    /// los detalles. Si la app crece, se puede mantener solo el panel lateral.
    /// </summary>
    private void OnChipValidacionClick(object sender, RoutedEventArgs e)
    {
        Vm.ModoActivo = LosasPlus.ViewModels.ModoSidebar.Validacion;
    }

    /// <summary>
    /// Click en la columna TIPO de una losa: abre el modal
    /// SelectorTipoLosaWindow (del src.UI.Shared) precargado con el tipo
    /// actual. Al confirmar, asigna el nuevo tipo a la losa con snapshot
    /// para undo. Si hay multi-selección, aplica a todas las seleccionadas.
    /// </summary>
    private void OnAbrirSelectorTipoLosaClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not Losa rowLosa) return;

        var dlg = new MemoriaPlus.Views.SelectorTipoLosaWindow(rowLosa.Tipo)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true || dlg.TipoConfirmado is not int nuevo) return;

        Vm.PushUndoSnapshot();
        if (LosasGrid.SelectedItems.Count > 1 && LosasGrid.SelectedItems.Contains(rowLosa))
        {
            int n = 0;
            foreach (var item in LosasGrid.SelectedItems)
                if (item is Losa l) { l.Tipo = nuevo; n++; }
            Vm.Log($"Tipo {nuevo} aplicado a {n} losas seleccionadas.");
        }
        else
        {
            rowLosa.Tipo = nuevo;
            Vm.Log($"Tipo de losa #{rowLosa.Id} cambiado a {nuevo}.");
        }
        Vm.RefreshDLContent();
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
        if (dlg.ShowDialog() != true) return;

        // Pre-flight con el doctor para detectar problemas comunes ANTES de
        // que AbrirDL silenciosamente loggee "Error abriendo .DL: ...".
        // Si hay errores, abrimos el modal del doctor con opciones de acción.
        var diag = LosasPlus.Services.DLDoctor.Diagnosticar(dlg.FileName);
        if (diag.PuedeAbrir && diag.EstaLimpio)
        {
            Vm.AbrirDL(dlg.FileName);
            return;
        }
        AbrirDoctorModal(diag);
    }

    /// <summary>
    /// Handler del evento <c>AparienciaCambiada</c> del ConfiguracionView.
    /// Llama a <see cref="App.AplicarApariencia"/> que muta el ResourceDictionary
    /// global en vivo (tema + tipografía mono + color acento). Sin esto, la
    /// pestaña Apariencia sólo persistía JSON y no se veía nada hasta restart.
    /// </summary>
    private void OnAparienciaCambiada(object sender, MemoriaPlus.Views.AparienciaCambiadaEventArgs e)
    {
        App.AplicarApariencia(e.Apariencia);
    }

    private void OnDiagnosticarDLClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Diagnosticar archivo .DL",
            Filter = "Archivos de datos Losas (*.DL)|*.DL|Todos los archivos|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;
        var diag = LosasPlus.Services.DLDoctor.Diagnosticar(dlg.FileName);
        AbrirDoctorModal(diag);
    }

    /// <summary>
    /// Abre la ventana modal del Doctor con un diagnóstico ya calculado y wirea
    /// los callbacks para abrir el archivo reparado / como proyecto.
    /// </summary>
    private void AbrirDoctorModal(LosasPlus.Services.DLDiagnostico diag)
    {
        var win = new Views.DLDoctorWindow(diag)
        {
            Owner = this,
            OnAbrirDL = path => Vm.AbrirDL(path),
            OnAbrirComoProyecto = path => Vm.AbrirProyectoLpxPorPath(path),
        };
        win.ShowDialog();
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

        // Captura del lienzo CAD como esquema del .xlsx (mejor esfuerzo: si la
        // captura falla, se exporta el Excel sin imagen).
        byte[]? png = null;
        try
        {
            var bmp = CadEditorView.CanvasHost.CaptureCanvasPng();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            png = ms.ToArray();
        }
        catch (System.Exception ex) { Vm.Log("No se pudo capturar el lienzo CAD: " + ex.Message); }

        await Vm.ExportarXlsxAsync(dlg.FileName, png);
    }

    // Export SAF 2.2.0 — Fase INTEROP-I1. Pass-through al ExportarSafCommand
    // de la VM: el command abre el SaveFileDialog corporativo y dispara el
    // export en background con marshaling al hilo UI. Mantiene el mismo
    // patrón de Click-handler que los hermanos CSV/XLSX.
    private void OnExportSafClick(object sender, RoutedEventArgs e)
    {
        Vm.ExportarSafCommand?.Execute(null);
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
        // Snapshot ANTES de aplicar el commit para que Ctrl+Z deshaga la edición.
        // Solo cuando el usuario realmente commiteó (no en cancel).
        if (e.EditAction == DataGridEditAction.Commit)
            Vm.PushUndoSnapshot();
        // Refresca la vista del .DL al editar cualquier celda
        Dispatcher.BeginInvoke(new System.Action(() => Vm.RefreshDLContent()),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Análogo para los DataGrids de BordesX / BordesY.</summary>
    private void OnBordeCellEdited(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
            Vm.PushUndoSnapshot();
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

    /// <summary>
    /// Doble-click en una fila de proyectos recientes (Explorador): abre el
    /// proyecto y cambia a modo Editor. Equivalente al AbrirEnEditorCommand
    /// pero disparado por la interacción nativa del DataGrid.
    /// </summary>
    private void OnAbrirRecienteDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Vm.ProyectoRecienteSeleccionado is null) return;
        if (Vm.AbrirEnEditorCommand is { } cmd && cmd.CanExecute(null))
            cmd.Execute(null);
    }
}
