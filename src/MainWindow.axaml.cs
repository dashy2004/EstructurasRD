using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LosasPlus.Models;
using LosasPlus.Persistence;
using LosasPlus.Services;
using LosasPlus.ViewModels;
using MemoriaPlus.Services;

namespace LosasPlus;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext!;

    /// <summary>
    /// KeyBindings de los atajos personalizables aplicados dinámicamente. Los
    /// 4 atajos FIJOS viven en XAML (Window.KeyBindings) y nunca se rastrean aquí.
    /// Se elimina sólo lo que este método agregó, dejando intactos los fijos.
    /// </summary>
    private readonly List<KeyBinding> _atajosConfigurables = new();

    /// <summary>
    /// True una vez que el handler <see cref="OnClosing"/> ya resolvió la
    /// confirmación de cambios sin guardar y autorizó el cierre. Evita el bucle
    /// de re-entrada cuando llamamos <see cref="Window.Close"/> programáticamente.
    /// </summary>
    private bool _allowClose;

    public MainWindow()
    {
        // InitializeComponent() (no AvaloniaXamlLoader.Load) porque el ctor
        // referencia controles con x:Name (TiposPanel, LosasGrid, SistemasList):
        // sólo el InitializeComponent generado puebla esos campos tipados.
        InitializeComponent();

        // Registra esta ventana como el TopLevel activo para los servicios de
        // diálogo/portapapeles/launcher (AppServices) que consumen los ViewModels.
        AppServices.TopLevelAccessor = () => this;

        // Click en un icono del catálogo → aplicar el tipo a la fila seleccionada
        // (o a todas las seleccionadas si hay multi-selección).
        TiposPanel.TipoSelected = codigo =>
        {
            if (LosasGrid.SelectedItems is { Count: > 1 } sel)
            {
                Vm.PushUndoSnapshot();
                int n = 0;
                foreach (var item in sel)
                    if (item is Losa l) { l.Tipo = codigo; n++; }
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

        Loaded += OnLoaded;
        Closed += OnClosed;
        Closing += OnClosing;
    }

    /// <summary>
    /// Intercepta el cierre de la ventana para no perder trabajo sin guardar
    /// (Fase A — pérdida de datos). Si el proyecto está sucio, cancela el cierre,
    /// pregunta si guardar / descartar y solo cierra cuando el usuario decidió.
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;                       // ya autorizado: dejar cerrar
        if (DataContext is not MainViewModel vm || !vm.IsDirty) return;

        e.Cancel = true;                               // detener este cierre

        // 3 estados: Guardar antes de cerrar / Descartar y cerrar / Cancelar
        // (quedarse). CRÍTICO (Fase A): descartar el diálogo (Escape / X) devuelve
        // Cancelar = quedarse, nunca descarta el trabajo en silencio.
        var r = await MemoriaPlus.Services.AppServices.MessageBox.ConfirmarGuardarDescartarCancelarAsync(
            "Cambios sin guardar",
            "¿Querés guardar los cambios antes de cerrar?");
        if (r == MemoriaPlus.Services.ResultadoDescarte.Cancelar) return;   // quedarse abierto
        if (r == MemoriaPlus.Services.ResultadoDescarte.Guardar)
        {
            var ok = await vm.GuardarAsync();
            if (!ok) return;                           // guardado falló/cancelado: no cerrar
        }

        _allowClose = true;
        Close();
    }

    /// <summary>Abre el modal de atajos. Cableado al delegado del VM (Ctrl+/).</summary>
    private async void AbrirShortcutsPanel()
        => await new LosasPlus.Views.KeyboardShortcutsWindow().ShowDialog(this);

    // =====================================================================
    // Atajos de teclado personalizables — aplicados en vivo desde atajos.json
    // =====================================================================

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OnAbrirShortcuts = AbrirShortcutsPanel;
            // La ConfiguracionView del router (Fase D) emite AparienciaCambiada;
            // el VM lo reenvía por este callback (antes era el handler XAML
            // OnAparienciaCambiada). App.AplicarApariencia es estático.
            vm.OnAplicarApariencia = App.AplicarApariencia;
        }

        AplicarAtajos(AtajosService.Load());
        AtajosService.AtajosCambiados += OnAtajosCambiados;

        if (Environment.GetEnvironmentVariable("EXPORT_SCREENSHOTS") == "true")
        {
            Dispatcher.UIThread.Post(async () => await RunVisualVerificationAsync());
        }
    }

    private void OnClosed(object? sender, EventArgs e)
        => AtajosService.AtajosCambiados -= OnAtajosCambiados;

    private void OnAtajosCambiados(object? sender, AtajosConfig cfg)
        => Dispatcher.UIThread.Post(() => AplicarAtajos(cfg));

    /// <summary>
    /// Aplica los atajos personalizables de <paramref name="cfg"/>. Port a Avalonia:
    /// WPF InputBindings/KeyBinding(command,key,mods) → Window.KeyBindings +
    /// <see cref="KeyGesture"/>. Sólo se quitan los que este método agregó
    /// (rastreados en <c>_atajosConfigurables</c>): los 4 fijos del XAML quedan.
    /// </summary>
    private void AplicarAtajos(AtajosConfig cfg)
    {
        if (DataContext is not MainViewModel vm) return;

        foreach (var b in _atajosConfigurables) KeyBindings.Remove(b);
        _atajosConfigurables.Clear();

        var mapa = new Dictionary<string, ICommand?>
        {
            { AtajoIds.NuevoProyecto, vm.NuevoProyectoLpxCommand },
            { AtajoIds.Abrir,         vm.AbrirProyectoLpxCommand },
            { AtajoIds.Guardar,       vm.GuardarProyectoLpxCommand },
            { AtajoIds.GuardarComo,   vm.GuardarComoLpxCommand },
            { AtajoIds.Generar,       vm.GenerarMemoriaCommand },
            { AtajoIds.AgregarLosa,   vm.AgregarLosaCommand },
            { AtajoIds.Busqueda,      vm.IrABusquedaCommand },
        };

        foreach (var (id, command) in mapa)
        {
            if (command is null) continue;
            var gestureStr = cfg.Get(id);
            if (string.IsNullOrWhiteSpace(gestureStr)) continue;

            if (TryParseGesture(gestureStr, out var key, out var mods))
            {
                var kb = new KeyBinding { Command = command, Gesture = new KeyGesture(key, mods) };
                KeyBindings.Add(kb);
                _atajosConfigurables.Add(kb);
            }
        }
    }

    /// <summary>Parsea "Ctrl+Shift+S" → <see cref="Key"/> + <see cref="KeyModifiers"/>.</summary>
    private static bool TryParseGesture(string gesture, out Key key, out KeyModifiers mods)
    {
        key = Key.None;
        mods = KeyModifiers.None;
        var tokens = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return false;

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control": mods |= KeyModifiers.Control; break;
                case "shift":   mods |= KeyModifiers.Shift;   break;
                case "alt":     mods |= KeyModifiers.Alt;     break;
                case "win":
                case "windows": mods |= KeyModifiers.Meta;    break;
                default: return false;
            }
        }

        var keyToken = tokens[^1];
        if (keyToken.Length == 1 && char.IsDigit(keyToken[0]))
            keyToken = "D" + keyToken;

        return Enum.TryParse(keyToken, ignoreCase: true, out key) && key != Key.None;
    }

    // =====================================================================
    // Grid de losas
    // =====================================================================

    private void OnLosasSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid dg)
            Vm.ActualizarLosasSeleccionadas(dg.SelectedItems);
    }

    private void OnLosaIdClick(object? sender, RoutedEventArgs e)
    {
        if (!Vm.ModoConectarBordes) return;
        if (sender is not Button btn) return;
        if (btn.Tag is not Losa l) return;
        Vm.HandleIdClickParaBorde(l.Id);
    }

    private void OnChipValidacionClick(object? sender, RoutedEventArgs e)
        => Vm.ModoActivo = ModoSidebar.Validacion;

    /// <summary>
    /// Click en la columna TIPO: abre el modal SelectorTipoLosaWindow precargado
    /// con el tipo actual. Port a Avalonia: ShowDialog&lt;int?&gt; devuelve el
    /// código confirmado (o null si se canceló).
    /// </summary>
    private async void OnAbrirSelectorTipoLosaClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Losa rowLosa) return;

        var dlg = new MemoriaPlus.Views.SelectorTipoLosaWindow(rowLosa.Tipo);
        var resultado = await dlg.ShowDialog<int?>(this);
        if (resultado is not int nuevo) return;

        Vm.PushUndoSnapshot();
        if (LosasGrid.SelectedItems is { Count: > 1 } sel && sel.Contains(rowLosa))
        {
            int n = 0;
            foreach (var item in sel)
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

    // =====================================================================
    // Menú File / Engine / Export — file pickers vía AppServices (async).
    // =====================================================================

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var path = await AppServices.Dialogs.OpenFileAsync(
            "Abrir archivo .DL",
            new FileFilter("Archivos de datos Losas", new[] { "*.DL" }),
            new FileFilter("Todos los archivos", new[] { "*" }));
        if (path is null) return;

        // Pre-flight con el doctor antes de un fallo silencioso al abrir.
        var diag = DLDoctor.Diagnosticar(path);
        if (diag.PuedeAbrir && diag.EstaLimpio) { Vm.AbrirDL(path); return; }
        await AbrirDoctorModal(diag);
    }

    private async void OnDiagnosticarDLClick(object? sender, RoutedEventArgs e)
    {
        var path = await AppServices.Dialogs.OpenFileAsync(
            "Diagnosticar archivo .DL",
            new FileFilter("Archivos de datos Losas", new[] { "*.DL" }),
            new FileFilter("Todos los archivos", new[] { "*" }));
        if (path is null) return;
        await AbrirDoctorModal(DLDoctor.Diagnosticar(path));
    }

    /// <summary>Abre el Doctor modal con un diagnóstico ya calculado.</summary>
    private async Task AbrirDoctorModal(DLDiagnostico diag)
    {
        var win = new Views.DLDoctorWindow(diag)
        {
            OnAbrirDL = path => Vm.AbrirDL(path),
            OnAbrirComoProyecto = path => Vm.AbrirProyectoLpxPorPath(path),
        };
        await win.ShowDialog(this);
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var sugerido = Vm.DLPath != null ? Path.GetFileName(Vm.DLPath) : (Vm.Sistema.Nombre + ".DL");
        var path = await AppServices.Dialogs.SaveFileAsync(
            "Guardar archivo .DL", sugerido, ".DL",
            new FileFilter("Archivos de datos Losas", new[] { "*.DL" }));
        if (path is not null) await Vm.GuardarDLAsync(path);
    }

    private async void OnImportTxtClick(object? sender, RoutedEventArgs e)
    {
        var path = await AppServices.Dialogs.OpenFileAsync(
            "Importar archivo .TXT (salida de Losas.exe)",
            new FileFilter("Salidas Losas", new[] { "*.TXT" }),
            new FileFilter("Todos los archivos", new[] { "*" }));
        if (path is not null) await Vm.ImportarTxtAsync(path);
    }

    private void OnCalcularNativoClick(object? sender, RoutedEventArgs e) => Vm.CalcularNativo();

    private void OnGenerarEjesClick(object? sender, RoutedEventArgs e) => Vm.GenerarEjes();

    private async void OnGenerarDesdeFotoClick(object? sender, RoutedEventArgs e)
    {
        var path = await AppServices.Dialogs.OpenFileAsync(
            "Subir foto del esquema (losas / vigas)",
            new FileFilter("Imágenes", new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" }),
            new FileFilter("Todos los archivos", new[] { "*" }));
        if (path is not null) await Vm.GenerarDesdeFotoAsync(path);
    }

    private async void OnGenerarDesdeDxfClick(object? sender, RoutedEventArgs e)
    {
        var path = await AppServices.Dialogs.OpenFileAsync(
            "Importar DXF estructural (capas VIGAS / COLUMNAS / LOSAS)",
            new FileFilter("DXF", new[] { "*.dxf" }),
            new FileFilter("Todos los archivos", new[] { "*" }));
        if (path is not null) await Vm.GenerarDesdeDxfAsync(path);
    }

    /// <summary>
    /// Engine → «Calcular carga última (Wu) desde geometría»: aplica Wu a cada
    /// losa del proyecto (lo escribe en Losa.Carga) y abre un modal con el
    /// desglose Qmamp/Qmap/Qd/Ql/Qu por losa. Acción aditiva.
    /// </summary>
    private async void OnCalcularCargaUltimaClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var filas = Vm.AplicarCargaUltimaConDesglose();
            if (filas.Count == 0)
            {
                await AppServices.MessageBox.InfoAsync("Carga última (Wu)",
                    "No hay losas en el edificio activo para calcular la carga última.");
                return;
            }
            // Fórmula real del proyecto (no hardcodeada): refleja los factores
            // vigentes en CargasGlobales.Factores, que un proyecto puede cambiar.
            var f = Vm.Proyecto.Cargas.Factores;
            string combinacion = $"Qu = {f.FactorD:0.##}·Qd + {f.FactorL:0.##}·Ql ({f.Norma})";
            await new Views.CargaUltimaWindow(filas, combinacion).ShowDialog(this);
        }
        catch (Exception ex)
        {
            await AppServices.MessageBox.InfoAsync("Carga última (Wu)",
                "No se pudo calcular la carga última:\n" + ex.Message);
        }
    }

    private async void OnExportCsvClick(object? sender, RoutedEventArgs e)
    {
        var path = await AppServices.Dialogs.SaveFileAsync(
            "Exportar CSV", (Vm.Sistema.Nombre ?? "losas") + ".csv", ".csv",
            new FileFilter("CSV separado por ;", new[] { "*.csv" }));
        if (path is not null) await Vm.ExportarCsvAsync(path);
    }

    private async void OnExportXlsxClick(object? sender, RoutedEventArgs e)
    {
        var path = await AppServices.Dialogs.SaveFileAsync(
            "Exportar a Excel", (Vm.Sistema.Nombre ?? "losas") + ".xlsx", ".xlsx",
            new FileFilter("Excel Workbook", new[] { "*.xlsx" }));
        if (path is null) return;

        // Captura del lienzo de PLANTA como esquema del .xlsx (UI1.6 — antes se
        // capturaba el lienzo CAD; mejor esfuerzo: si la captura falla, se
        // exporta el Excel sin imagen).
        byte[]? png = null;
        try
        {
            if (Vm.ObtenerVistaPlanta2D() is Views.EditorUnificadoView { Editor.CanvasPlanta: { } canvas })
            {
                var bmp = canvas.CaptureCanvasPng();
                using var ms = new MemoryStream();
                bmp.Save(ms);
                png = ms.ToArray();
            }
        }
        catch (Exception ex) { Vm.Log("No se pudo capturar el lienzo de planta: " + ex.Message); }

        await Vm.ExportarXlsxAsync(path, png);
    }

    private async void OnExportSafClick(object? sender, RoutedEventArgs e)
    {
        var path = await AppServices.Dialogs.SaveFileAsync(
            "Exportar a SAF (Structural Analysis Format)",
            (Vm.Sistema.Nombre ?? "modelo") + ".xlsx", ".xlsx",
            new FileFilter("SAF (Excel)", new[] { "*.xlsx" }));
        if (path is null) return;

        Vm.ExportarSaf(path);
    }

    private async void OnExportAcerosCsvClick(object? sender, RoutedEventArgs e)
    {
        var path = await AppServices.Dialogs.SaveFileAsync(
            "Exportar aceros (CSV)", (Vm.Sistema.Nombre ?? "aceros") + "_aceros.csv", ".csv",
            new FileFilter("CSV separado por ;", new[] { "*.csv" }));
        if (path is not null) Vm.ExportarAcerosCsv(path);
    }

    private async void OnExportAcerosXlsxClick(object? sender, RoutedEventArgs e)
    {
        var path = await AppServices.Dialogs.SaveFileAsync(
            "Exportar aceros (Excel)", (Vm.Sistema.Nombre ?? "aceros") + "_aceros.xlsx", ".xlsx",
            new FileFilter("Excel Workbook", new[] { "*.xlsx" }));
        if (path is not null) Vm.ExportarAcerosXlsx(path);
    }

    private void OnTrustPluginClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.Tag is string fullPath)
            Vm.Plugins.TrustPlugin(fullPath, Vm.Log);
    }

    private void OnRevokeTrustClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.Tag is string fullPath)
            Vm.Plugins.RevokeTrust(fullPath, Vm.Log);
    }

    private void OnAddLosa(object? sender, RoutedEventArgs e) => Vm.AgregarLosa();

    private async void OnPasteFromExcelClick(object? sender, RoutedEventArgs e)
    {
        var dlg = new LosasPlus.Views.PasteExcelDialog();
        var ok = await dlg.ShowDialog<bool>(this);
        if (!ok || dlg.ResultLosas is null) return;

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

    private void OnDelLosa(object? sender, RoutedEventArgs e)
    {
        if (LosasGrid.SelectedItem is Losa l) Vm.EliminarLosa(l);
    }

    private void OnAddBordeX(object? sender, RoutedEventArgs e) => Vm.AgregarBorde(true);
    private void OnAddBordeY(object? sender, RoutedEventArgs e) => Vm.AgregarBorde(false);

    private void OnDelBordeX(object? sender, RoutedEventArgs e)
    {
        if (Vm.Sistema.BordesX.Count > 0) Vm.EliminarBorde(Vm.Sistema.BordesX[^1]);
    }
    private void OnDelBordeY(object? sender, RoutedEventArgs e)
    {
        if (Vm.Sistema.BordesY.Count > 0) Vm.EliminarBorde(Vm.Sistema.BordesY[^1]);
    }

    private void OnLosasCellEdited(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit) Vm.PushUndoSnapshot();
        Dispatcher.UIThread.Post(() => Vm.RefreshDLContent(), DispatcherPriority.Background);
    }

    private void OnBordeCellEdited(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit) Vm.PushUndoSnapshot();
        Dispatcher.UIThread.Post(() => Vm.RefreshDLContent(), DispatcherPriority.Background);
    }

    private async void OnApplyDLText(object? sender, RoutedEventArgs e)
    {
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"losasplus_{Guid.NewGuid():N}.DL");
            File.WriteAllText(tmp, Vm.DLContent, System.Text.Encoding.GetEncoding(1252));
            var sistemas = DLFileService.ReadAll(tmp);
            File.Delete(tmp);

            Vm.Proyecto.Sistemas.Clear();
            foreach (var s in sistemas) Vm.Proyecto.Sistemas.Add(s);
            Vm.SistemaActivo = sistemas.First();
            Vm.Log($"Modelo actualizado desde el editor .DL ({sistemas.Count} sistema(s))");
        }
        catch (Exception ex)
        {
            await AppServices.MessageBox.InfoAsync("Error", $"El texto .DL no es válido:\n\n{ex.Message}");
        }
    }

    private void OnAddSistema(object? sender, RoutedEventArgs e) => Vm.AgregarSistema();

    private async void OnNewProjectClick(object? sender, RoutedEventArgs e)
    {
        var carpeta = await PickFolderAsync("Seleccioná una carpeta vacía para el nuevo proyecto");
        if (carpeta is null) return;

        var nombre = Path.GetFileName(carpeta.TrimEnd('/', '\\'));
        try
        {
            var p = ProyectoService.CrearProyecto(carpeta, nombre);
            Vm.AbrirProyecto(p.Archivo);
        }
        catch (Exception ex)
        {
            await AppServices.MessageBox.InfoAsync("Error", $"Error creando proyecto:\n{ex.Message}");
        }
    }

    private async void OnOpenProjectClick(object? sender, RoutedEventArgs e)
    {
        var path = await AppServices.Dialogs.OpenFileAsync(
            "Abrir manifest del proyecto",
            new FileFilter("Manifest LosasPlus", new[] { "proyecto.lpx.json", "*.json" }),
            new FileFilter("Todos", new[] { "*" }));
        if (path is not null) Vm.AbrirProyecto(path);
    }

    private async void OnSaveProjectClick(object? sender, RoutedEventArgs e)
    {
        var carpeta = await PickFolderAsync(
            "Seleccioná la carpeta donde guardar el proyecto (un .DL por cada sistema)");
        if (carpeta is not null) Vm.GuardarProyecto(carpeta);
    }

    /// <summary>
    /// Selector de carpeta. Port a Avalonia: WPF System.Windows.Forms.
    /// FolderBrowserDialog → TopLevel.StorageProvider.OpenFolderPickerAsync.
    /// </summary>
    private async Task<string?> PickFolderAsync(string title)
    {
        var top = AppServices.TopLevelAccessor();
        if (top is null) return null;
        var res = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return res.Count > 0 ? res[0].Path.LocalPath : null;
    }

    private async void OnDeleteSistema(object? sender, RoutedEventArgs e)
    {
        if (SistemasList?.SelectedItem is Sistema s) await Vm.EliminarSistema(s);
    }

    private void OnAddNivel(object? sender, RoutedEventArgs e)
    {
        Vm.AgregarNivel($"Nivel {(Vm.NivelesDelEdificio?.Count ?? 0) + 1}", (Vm.NivelesDelEdificio?.Count ?? 0) * 3.0);
    }

    private void OnDeleteNivel(object? sender, RoutedEventArgs e)
    {
        if (Vm.NivelActivo != null)
            Vm.EliminarNivel(Vm.NivelActivo);
    }

    private async void OnReloadPlugins(object? sender, RoutedEventArgs e)
    {
        await Vm.Plugins.LoadAllAsync(Vm.Log);
        await Vm.Plugins.RunHookAsync("load", new PluginContext { Sistema = Vm.Sistema }, Vm.Log);
    }

    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        App.ToggleTheme();
        Vm.Log($"Tema cambiado a: {App.CurrentTheme}");
    }

    private async void OnAppearanceClick(object? sender, RoutedEventArgs e)
        => await new LosasPlus.Views.AppearanceDialog().ShowDialog(this);

    /// <summary>
    /// Doble-click en una fila de proyectos recientes (Explorador): abre el
    /// proyecto y cambia a modo Editor. Port a Avalonia: MouseDoubleClick → DoubleTapped.
    /// </summary>
    private void OnAbrirRecienteDoubleClick(object? sender, TappedEventArgs e)
    {
        if (Vm.ProyectoRecienteSeleccionado is null) return;
        if (Vm.AbrirEnEditorCommand is { } cmd && cmd.CanExecute(null))
            cmd.Execute(null);
    }

    private async Task RunVisualVerificationAsync()
    {
        Console.WriteLine("[SCREENSHOTS] Starting automated visual verification...");
        try
        {
            // 1. Load legacy .DL
            var dlPath = "/home/gdc/Downloads/EstructurasRD-main/tests/fixtures/sistema_demo_27_losas.DL";
            Vm.AbrirDL(dlPath);
            await Task.Delay(200);

            // 2. Import .TXT
            var txtPath = "/home/gdc/Downloads/EstructurasRD-main/tests/fixtures/sistema_demo_27_losas.TXT";
            await Vm.ImportarTxtAsync(txtPath);
            await Task.Delay(200);

            // 3. Add columns and levels to EdificioActivo to make it look like a real building
            var ed = Vm.EdificioActivo;
            if (ed != null)
            {
                ed.Niveles.Clear();
                for (int levelIndex = 0; levelIndex < 3; levelIndex++)
                {
                    var level = new Nivel
                    {
                        Nombre = $"Nivel {levelIndex + 1}",
                        Cota = levelIndex * 3.0
                    };
                    for (int colIndex = 0; colIndex < 4; colIndex++)
                    {
                        var col = new Columna
                        {
                            Nombre = $"C-{levelIndex * 4 + colIndex + 1}",
                            CoordenadaX = colIndex * 5.0,
                            CoordenadaY = 4.0,
                            Altura = 3.0,
                            Base = 0.30,
                            Peralte = 0.30,
                            Zapata = levelIndex == 0 ? new Zapata { Ancho = 1.8, Largo = 1.8, Peralte = 0.40 } : null
                        };
                        level.Columnas.Add(col);
                    }
                    if (levelIndex == 0)
                    {
                        level.Sistemas.Add(Vm.SistemaActivo);
                    }
                    else
                    {
                        var dummySys = new Sistema { Nombre = $"Sistema Nivel {levelIndex + 1}" };
                        foreach (var l in Vm.SistemaActivo.Losas)
                        {
                            dummySys.Losas.Add(new Losa
                            {
                                Id = l.Id, Tipo = l.Tipo, Carga = l.Carga, Espesor = l.Espesor, Lx = l.Lx, Ly = l.Ly, Rec = l.Rec
                            });
                        }
                        level.Sistemas.Add(dummySys);
                    }
                    ed.Niveles.Add(level);
                }
            }

            // 4. Force recalculating and selecting level
            if (Vm.BajadaCargas != null)
            {
                Vm.BajadaCargas.Recalcular();
            }
            if (Vm.ColumnasEditor != null && ed != null && ed.Niveles.Count > 0)
            {
                Vm.NivelActivo = ed.Niveles[0];
            }

            // 5. Run loop to capture each view mode
            var modes = new[]
            {
                ModoSidebar.Editor,
                ModoSidebar.PlanoCad,
                ModoSidebar.Planta2D,
                ModoSidebar.Vista3D,
                ModoSidebar.Columnas,
                ModoSidebar.BajadaCargas,
                ModoSidebar.Validacion
            };

            var outputDir = "/home/gdc/.gemini/antigravity/brain/9c4cc8ee-768f-4f02-94d3-383b064ae8d7/.tempmediaStorage";
            Directory.CreateDirectory(outputDir);

            foreach (var mode in modes)
            {
                Vm.ModoActivo = mode;
                // Wait for layout solver & rendering
                await Task.Delay(1000);

                var bounds = this.Bounds;
                var width = (int)Math.Max(bounds.Width, 1200);
                var height = (int)Math.Max(bounds.Height, 800);
                var pixelSize = new Avalonia.PixelSize(width, height);
                using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(pixelSize);
                rtb.Render(this);

                var filePath = Path.Combine(outputDir, $"screenshot_{mode.ToString().ToLower()}.png");
                using (var fs = File.OpenWrite(filePath))
                {
                    rtb.Save(fs);
                }
                Console.WriteLine($"[SCREENSHOTS] Saved {filePath}");
            }
            Console.WriteLine("[SCREENSHOTS] All screenshots saved successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SCREENSHOTS] Error: {ex}");
        }
        finally
        {
            Environment.Exit(0);
        }
    }
}
