using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Vigas;
using LosasPlus.Transmision;
using LosasPlus.ViewModels;

namespace LosasPlus.Views;

public partial class Planta2DEditorView : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;
    private bool _updatingProperties;

    public Planta2DEditorView()
    {
        InitializeComponent();

        // Bind canvas selection event
        EditorCanvas.SelectionChanged += OnCanvasSelectionChanged;

        // Register event handlers for input fields
        RegisterInputHandlers();

        // Wire level selection
        CbNivel.SelectionChanged += OnNivelSelectionChanged;

        // Wire snap controls
        ChkSnap.IsCheckedChanged += OnSnapCheckedChanged;
        CbGridStep.SelectionChanged += OnGridStepSelectionChanged;

        // Sync initial snap states
        SyncSnapSettings();

        // Wire toolbar tools
        BtnPuntero.IsCheckedChanged += (s, e) => { if (BtnPuntero.IsChecked == true) EditorCanvas.ActiveTool = "Puntero"; };
        BtnAddLosa.IsCheckedChanged += (s, e) => { if (BtnAddLosa.IsChecked == true) EditorCanvas.ActiveTool = "Losa"; };
        BtnAddViga.IsCheckedChanged += (s, e) => { if (BtnAddViga.IsChecked == true) EditorCanvas.ActiveTool = "Viga"; };
        BtnAddCol.IsCheckedChanged += (s, e) => { if (BtnAddCol.IsChecked == true) EditorCanvas.ActiveTool = "Columna"; };

        // Wire buttons
        BtnRecalcular.Click += OnRecalcularClick;
        BtnEliminar.Click += OnEliminarClick;
        BtnReportes.Click += OnCargarReportesClick;
    }

    /// <summary>
    /// Carga un GeoJSON de reportes de IncidenciasRD y los pinta sobre la
    /// planta (Fase N.1). Requiere el proyecto georreferenciado. Si la capa ya
    /// está cargada, el click la quita.
    /// </summary>
    private async void OnCargarReportesClick(object? sender, RoutedEventArgs e)
    {
        if (EditorCanvas.Reportes is { Count: > 0 })
        {
            EditorCanvas.Reportes = null;
            TxtStatus.Text = "Capa de reportes quitada.";
            return;
        }

        var geo = Vm?.Proyecto?.Georreferencia;
        if (geo is null)
        {
            TxtStatus.Text = "⚠ Ubica el proyecto (Datos generales → Georreferenciación) antes de cargar reportes.";
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var archivos = await top.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Cargar reportes de IncidenciasRD (GeoJSON)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("GeoJSON")
                    {
                        Patterns = new[] { "*.geojson", "*.json" },
                    },
                },
            });
        if (archivos.Count == 0) return;   // cancelado

        try
        {
            string json = System.IO.File.ReadAllText(archivos[0].Path.LocalPath);

            // 1 km alrededor del origen del proyecto: suficiente para la
            // parcela y su entorno inmediato sin traer la ciudad entera.
            var reportes = Services.ImportadorReportesGeoJson.Importar(json, geo, radioMetros: 1000.0);

            EditorCanvas.Reportes = reportes;
            TxtStatus.Text = reportes.Count == 0
                ? "El archivo no tiene reportes a menos de 1 km del origen del proyecto."
                : $"🚩 {reportes.Count} reporte(s) de IncidenciasRD sobre la planta (radio 1 km).";
        }
        catch (Services.ImportadorReportesException ex)
        {
            TxtStatus.Text = "⚠ " + ex.Message;
        }
        catch (System.IO.IOException ex)
        {
            TxtStatus.Text = "⚠ No se pudo leer el archivo: " + ex.Message;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        PopulateNiveles();
    }

    private void PopulateNiveles()
    {
        if (Vm?.EdificioActivo != null)
        {
            CbNivel.ItemsSource = Vm.EdificioActivo.Niveles;
            CbNivel.SelectedItem = Vm.EdificioActivo.Niveles.FirstOrDefault();
        }
    }

    private void OnNivelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CbNivel.SelectedItem is Nivel nivel)
        {
            EditorCanvas.Nivel = nivel;
        }
        else
        {
            EditorCanvas.Nivel = null;
        }
    }

    private void OnSnapCheckedChanged(object? sender, RoutedEventArgs e)
    {
        EditorCanvas.IsSnappingEnabled = ChkSnap.IsChecked == true;
        EditorCanvas.InvalidateVisual();
    }

    private void OnGridStepSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CbGridStep == null || EditorCanvas == null) return;

        double step = 0.5;
        switch (CbGridStep.SelectedIndex)
        {
            case 0: step = 0.1; break;
            case 1: step = 0.2; break;
            case 2: step = 0.5; break;
            case 3: step = 1.0; break;
            case 4: step = 0.0; break; // Free
        }

        EditorCanvas.StepGrid = step;
        EditorCanvas.InvalidateVisual();
    }

    private void SyncSnapSettings()
    {
        EditorCanvas.IsSnappingEnabled = ChkSnap.IsChecked == true;
        OnGridStepSelectionChanged(null, null!);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // No interceptar atajos mientras se escribe en un campo de propiedades.
        if (e.Source is TextBox) return;

        // S alterna el snapping.
        if (e.Key == Key.S)
        {
            ChkSnap.IsChecked = ChkSnap.IsChecked != true;
            e.Handled = true;
        }
        // Supr/Delete elimina la(s) losa(s) (u otros elementos) seleccionados.
        else if (e.Key == Key.Delete)
        {
            EliminarSeleccionados();
            e.Handled = true;
        }
    }

    private void OnCanvasSelectionChanged(object? selected)
    {
        _updatingProperties = true;

        // Clear previous error backgrounds when selection changes
        ClearErrorBackgrounds();

        TxtNoSelection.IsVisible = selected == null;
        PanelLosa.IsVisible = selected is Losa;
        PanelViga.IsVisible = selected is Viga;
        PanelColumna.IsVisible = selected is Columna;

        if (selected is Losa l)
        {
            TxtLosaId.Text = l.Id.ToString(CultureInfo.InvariantCulture);
            TxtLosaX.Text = l.CoordenadaX.ToString("0.##", CultureInfo.InvariantCulture);
            TxtLosaY.Text = l.CoordenadaY.ToString("0.##", CultureInfo.InvariantCulture);
            TxtLosaLx.Text = l.Lx.ToString("0.##", CultureInfo.InvariantCulture);
            TxtLosaLy.Text = l.Ly.ToString("0.##", CultureInfo.InvariantCulture);
            TxtLosaCarga.Text = l.Carga.ToString("0.##", CultureInfo.InvariantCulture);
            TxtLosaEspesor.Text = l.Espesor.ToString("0.##", CultureInfo.InvariantCulture);
        }
        else if (selected is Viga v)
        {
            TxtVigaId.Text = v.Id.ToString(CultureInfo.InvariantCulture);
            TxtVigaNombre.Text = v.Nombre;
            TxtVigaX.Text = v.OrigenX.ToString("0.##", CultureInfo.InvariantCulture);
            TxtVigaY.Text = v.OrigenY.ToString("0.##", CultureInfo.InvariantCulture);
            TxtVigaAngulo.Text = v.AnguloGrados.ToString("0.##", CultureInfo.InvariantCulture);
            TxtVigaLargo.Text = v.LongitudTotal.ToString("0.##", CultureInfo.InvariantCulture);
        }
        else if (selected is Columna c)
        {
            TxtColId.Text = c.Id.ToString(CultureInfo.InvariantCulture);
            TxtColNombre.Text = c.Nombre;
            TxtColX.Text = c.CoordenadaX.ToString("0.##", CultureInfo.InvariantCulture);
            TxtColY.Text = c.CoordenadaY.ToString("0.##", CultureInfo.InvariantCulture);
            TxtColBase.Text = c.Base.ToString("0.##", CultureInfo.InvariantCulture);
            TxtColPeralte.Text = c.Peralte.ToString("0.##", CultureInfo.InvariantCulture);
            TxtColAltura.Text = c.Altura.ToString("0.##", CultureInfo.InvariantCulture);
        }

        _updatingProperties = false;
    }

    private void ClearErrorBackgrounds()
    {
        // Losa textboxes
        TxtLosaX.ClearValue(TextBox.BackgroundProperty);
        TxtLosaY.ClearValue(TextBox.BackgroundProperty);
        TxtLosaLx.ClearValue(TextBox.BackgroundProperty);
        TxtLosaLy.ClearValue(TextBox.BackgroundProperty);
        TxtLosaCarga.ClearValue(TextBox.BackgroundProperty);
        TxtLosaEspesor.ClearValue(TextBox.BackgroundProperty);

        // Viga textboxes
        TxtVigaNombre.ClearValue(TextBox.BackgroundProperty);
        TxtVigaX.ClearValue(TextBox.BackgroundProperty);
        TxtVigaY.ClearValue(TextBox.BackgroundProperty);
        TxtVigaAngulo.ClearValue(TextBox.BackgroundProperty);

        // Columna textboxes
        TxtColNombre.ClearValue(TextBox.BackgroundProperty);
        TxtColX.ClearValue(TextBox.BackgroundProperty);
        TxtColY.ClearValue(TextBox.BackgroundProperty);
        TxtColBase.ClearValue(TextBox.BackgroundProperty);
        TxtColPeralte.ClearValue(TextBox.BackgroundProperty);
        TxtColAltura.ClearValue(TextBox.BackgroundProperty);
    }

    private void RegisterInputHandlers()
    {
        // Losa inputs
        TxtLosaX.LostFocus += (s, e) => CommitLosa();
        TxtLosaY.LostFocus += (s, e) => CommitLosa();
        TxtLosaLx.LostFocus += (s, e) => CommitLosa();
        TxtLosaLy.LostFocus += (s, e) => CommitLosa();
        TxtLosaCarga.LostFocus += (s, e) => CommitLosa();
        TxtLosaEspesor.LostFocus += (s, e) => CommitLosa();

        TxtLosaX.KeyDown += OnInputKeyDown;
        TxtLosaY.KeyDown += OnInputKeyDown;
        TxtLosaLx.KeyDown += OnInputKeyDown;
        TxtLosaLy.KeyDown += OnInputKeyDown;
        TxtLosaCarga.KeyDown += OnInputKeyDown;
        TxtLosaEspesor.KeyDown += OnInputKeyDown;

        // Viga inputs
        TxtVigaNombre.LostFocus += (s, e) => CommitViga();
        TxtVigaX.LostFocus += (s, e) => CommitViga();
        TxtVigaY.LostFocus += (s, e) => CommitViga();
        TxtVigaAngulo.LostFocus += (s, e) => CommitViga();

        TxtVigaNombre.KeyDown += OnInputKeyDown;
        TxtVigaX.KeyDown += OnInputKeyDown;
        TxtVigaY.KeyDown += OnInputKeyDown;
        TxtVigaAngulo.KeyDown += OnInputKeyDown;

        // Columna inputs
        TxtColNombre.LostFocus += (s, e) => CommitColumna();
        TxtColX.LostFocus += (s, e) => CommitColumna();
        TxtColY.LostFocus += (s, e) => CommitColumna();
        TxtColBase.LostFocus += (s, e) => CommitColumna();
        TxtColPeralte.LostFocus += (s, e) => CommitColumna();
        TxtColAltura.LostFocus += (s, e) => CommitColumna();

        TxtColNombre.KeyDown += OnInputKeyDown;
        TxtColX.KeyDown += OnInputKeyDown;
        TxtColY.KeyDown += OnInputKeyDown;
        TxtColBase.KeyDown += OnInputKeyDown;
        TxtColPeralte.KeyDown += OnInputKeyDown;
        TxtColAltura.KeyDown += OnInputKeyDown;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (EditorCanvas.SelectedElement is Losa) CommitLosa();
            else if (EditorCanvas.SelectedElement is Viga) CommitViga();
            else if (EditorCanvas.SelectedElement is Columna) CommitColumna();
            e.Handled = true;
        }
    }

    private bool ValidateDouble(TextBox textBox, out double val, Func<double, bool> validator)
    {
        if (double.TryParse(textBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out val))
        {
            if (validator(val))
            {
                textBox.ClearValue(TextBox.BackgroundProperty);
                return true;
            }
        }
        textBox.Background = new SolidColorBrush(Color.Parse("#FFCDD2"));
        return false;
    }

    private bool ValidateStringNotEmpty(TextBox textBox, out string val)
    {
        val = (textBox.Text ?? "").Trim();
        if (PlantaValidationRules.IsValidName(val))
        {
            textBox.ClearValue(TextBox.BackgroundProperty);
            return true;
        }
        textBox.Background = new SolidColorBrush(Color.Parse("#FFCDD2"));
        return false;
    }

    private void CommitLosa()
    {
        if (_updatingProperties || EditorCanvas.SelectedElement is not Losa l) return;

        bool isValid = true;
        isValid &= ValidateDouble(TxtLosaX, out double x, PlantaValidationRules.IsValidCoordinate);
        isValid &= ValidateDouble(TxtLosaY, out double y, PlantaValidationRules.IsValidCoordinate);
        isValid &= ValidateDouble(TxtLosaLx, out double lx, PlantaValidationRules.IsValidDimension);
        isValid &= ValidateDouble(TxtLosaLy, out double ly, PlantaValidationRules.IsValidDimension);
        isValid &= ValidateDouble(TxtLosaCarga, out double carga, PlantaValidationRules.IsValidCarga);
        isValid &= ValidateDouble(TxtLosaEspesor, out double espesor, PlantaValidationRules.IsValidEspesor);

        if (!isValid)
        {
            TxtStatus.Text = "✕ Error: Las dimensiones y espesor de losa deben ser mayores que cero. Las coordenadas no pueden ser texto vacío.";
            return;
        }

        l.CoordenadaX = x;
        l.CoordenadaY = y;
        l.Lx = lx;
        l.Ly = ly;
        l.Carga = carga;
        l.Espesor = espesor;

        TxtStatus.Text = $"Losa {l.Id} actualizada con éxito.";
        EditorCanvas.InvalidateVisual();
    }

    private void CommitViga()
    {
        if (_updatingProperties || EditorCanvas.SelectedElement is not Viga v) return;

        bool isValid = true;
        isValid &= ValidateStringNotEmpty(TxtVigaNombre, out string nombre);
        isValid &= ValidateDouble(TxtVigaX, out double x, PlantaValidationRules.IsValidCoordinate);
        isValid &= ValidateDouble(TxtVigaY, out double y, PlantaValidationRules.IsValidCoordinate);
        isValid &= ValidateDouble(TxtVigaAngulo, out double ang, PlantaValidationRules.IsValidCoordinate);

        if (!isValid)
        {
            TxtStatus.Text = "✕ Error: El nombre de la viga no puede estar vacío. Coordenadas y ángulo deben ser válidos.";
            return;
        }

        v.Nombre = nombre;
        v.OrigenX = x;
        v.OrigenY = y;
        v.AnguloGrados = ang;

        TxtStatus.Text = $"Viga {v.Nombre} actualizada con éxito.";
        EditorCanvas.InvalidateVisual();
    }

    private void CommitColumna()
    {
        if (_updatingProperties || EditorCanvas.SelectedElement is not Columna c) return;

        bool isValid = true;
        isValid &= ValidateStringNotEmpty(TxtColNombre, out string nombre);
        isValid &= ValidateDouble(TxtColX, out double x, PlantaValidationRules.IsValidCoordinate);
        isValid &= ValidateDouble(TxtColY, out double y, PlantaValidationRules.IsValidCoordinate);
        isValid &= ValidateDouble(TxtColBase, out double cb, PlantaValidationRules.IsValidDimension);
        isValid &= ValidateDouble(TxtColPeralte, out double cp, PlantaValidationRules.IsValidDimension);
        isValid &= ValidateDouble(TxtColAltura, out double alt, PlantaValidationRules.IsValidDimension);

        if (!isValid)
        {
            TxtStatus.Text = "✕ Error: Dimensiones de columna deben ser mayores que cero. El nombre no puede estar vacío.";
            return;
        }

        c.Nombre = nombre;
        c.CoordenadaX = x;
        c.CoordenadaY = y;
        c.Base = cb;
        c.Peralte = cp;
        c.Altura = alt;

        TxtStatus.Text = $"Columna {c.Nombre} actualizada con éxito.";
        EditorCanvas.InvalidateVisual();
    }

    private void OnEliminarClick(object? sender, RoutedEventArgs e) => EliminarSeleccionados();

    // Elimina TODOS los elementos seleccionados (una o varias losas, vigas o columnas).
    // Lo usan tanto el botón "Eliminar" como la tecla Supr/Delete.
    private void EliminarSeleccionados()
    {
        var nivel = EditorCanvas.Nivel;
        if (nivel == null) return;

        var seleccion = EditorCanvas.Seleccion.ToList();
        if (seleccion.Count == 0) return;

        Vm?.PushUndoSnapshot();

        int losas = 0, otros = 0;
        foreach (var sel in seleccion)
        {
            if (sel is Losa l)
            {
                foreach (var sys in nivel.Sistemas)
                {
                    if (sys.Losas.Remove(l)) { losas++; break; }
                }
            }
            else if (sel is Viga v)
            {
                if (nivel.Vigas.Remove(v)) otros++;
            }
            else if (sel is Columna c)
            {
                if (nivel.Columnas.Remove(c)) otros++;
            }
        }

        EditorCanvas.SelectedElement = null;
        EditorCanvas.InvalidateVisual();

        int total = losas + otros;
        if (total == 0)
            TxtStatus.Text = "No se eliminó ningún elemento.";
        else if (total == 1)
            TxtStatus.Text = "Elemento eliminado.";
        else if (losas == total)
            TxtStatus.Text = $"{losas} losas eliminadas.";
        else
            TxtStatus.Text = $"{total} elementos eliminados.";
    }

    private void OnRecalcularClick(object? sender, RoutedEventArgs e)
    {
        var nivel = EditorCanvas.Nivel;
        if (nivel == null) return;

        if (!ValidateDouble(TxtQAdm, out double qAdm, PlantaValidationRules.IsValidDimension))
        {
            TxtStatus.Text = "✕ Error: La presión admisible del suelo q_adm debe ser mayor que cero.";
            return;
        }

        try
        {
            // 1. Reparto geométrico losa -> viga
            int vigasCargadas = RepartoGeometrico.AplicarCargasGeometricas(nivel, "D");

            // 2. Descenso geométrico viga -> columna -> zapata
            var zapatasPredim = DescensoColumnas.PredimensionarGeometrico(nivel, qAdm);

            TxtStatus.Text = $"⚡ Descenso calculado con éxito: {vigasCargadas} vigas cargadas, {zapatasPredim.Count} zapatas predimensionadas.";
            EditorCanvas.InvalidateVisual();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"✕ Error al recalcular: {ex.Message}";
        }
    }
}
