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
using LosasPlus.Models.Cad;
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

        // Nivel y Edificio son bindeados en XAML.

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
        BtnAddEje.IsCheckedChanged += (s, e) => { if (BtnAddEje.IsChecked == true) EditorCanvas.ActiveTool = "Eje"; };
        BtnAddMuro.IsCheckedChanged += (s, e) => { if (BtnAddMuro.IsChecked == true) EditorCanvas.ActiveTool = "Muro"; };

        // Wire buttons
        BtnRecalcular.Click += OnRecalcularClick;
        BtnEliminar.Click += OnEliminarClick;
        BtnGenerarVigaContinua.Click += OnGenerarVigaContinuaClick;
        BtnVerElevacion.Click += OnVerElevacionClick;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // El canvas se actualiza por binding en el XAML
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
        // Pressing S toggles snapping when focus is NOT inside a TextBox
        if (e.Key == Key.S && e.Source is not TextBox)
        {
            ChkSnap.IsChecked = ChkSnap.IsChecked != true;
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
        PanelEje.IsVisible = selected is EjeEstructural;
        PanelMuro.IsVisible = selected is Muro;

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
        else if (selected is EjeEstructural eje)
        {
            TxtEjeEtiqueta.Text = eje.Etiqueta;
            TxtEjeStartX.Text = eje.PuntoInicio.X.ToString("0.##", CultureInfo.InvariantCulture);
            TxtEjeStartY.Text = eje.PuntoInicio.Y.ToString("0.##", CultureInfo.InvariantCulture);
            TxtEjeEndX.Text = eje.PuntoFin.X.ToString("0.##", CultureInfo.InvariantCulture);
            TxtEjeEndY.Text = eje.PuntoFin.Y.ToString("0.##", CultureInfo.InvariantCulture);
        }
        else if (selected is Muro m)
        {
            TxtMuroId.Text = m.Id.ToString();
            TxtMuroStartX.Text = m.PuntoInicio.X.ToString("0.##", CultureInfo.InvariantCulture);
            TxtMuroStartY.Text = m.PuntoInicio.Y.ToString("0.##", CultureInfo.InvariantCulture);
            TxtMuroEndX.Text = m.PuntoFin.X.ToString("0.##", CultureInfo.InvariantCulture);
            TxtMuroEndY.Text = m.PuntoFin.Y.ToString("0.##", CultureInfo.InvariantCulture);
            TxtMuroEspesor.Text = m.Espesor.ToString("0.##", CultureInfo.InvariantCulture);
            TxtMuroAltura.Text = m.Altura.ToString("0.##", CultureInfo.InvariantCulture);
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

        // Eje textboxes
        TxtEjeEtiqueta.ClearValue(TextBox.BackgroundProperty);
        TxtEjeStartX.ClearValue(TextBox.BackgroundProperty);
        TxtEjeStartY.ClearValue(TextBox.BackgroundProperty);
        TxtEjeEndX.ClearValue(TextBox.BackgroundProperty);
        TxtEjeEndY.ClearValue(TextBox.BackgroundProperty);

        // Muro textboxes
        TxtMuroStartX.ClearValue(TextBox.BackgroundProperty);
        TxtMuroStartY.ClearValue(TextBox.BackgroundProperty);
        TxtMuroEndX.ClearValue(TextBox.BackgroundProperty);
        TxtMuroEndY.ClearValue(TextBox.BackgroundProperty);
        TxtMuroEspesor.ClearValue(TextBox.BackgroundProperty);
        TxtMuroAltura.ClearValue(TextBox.BackgroundProperty);
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

        // Eje inputs
        TxtEjeEtiqueta.LostFocus += (s, e) => CommitEje();
        TxtEjeStartX.LostFocus += (s, e) => CommitEje();
        TxtEjeStartY.LostFocus += (s, e) => CommitEje();
        TxtEjeEndX.LostFocus += (s, e) => CommitEje();
        TxtEjeEndY.LostFocus += (s, e) => CommitEje();

        TxtEjeEtiqueta.KeyDown += OnInputKeyDown;
        TxtEjeStartX.KeyDown += OnInputKeyDown;
        TxtEjeStartY.KeyDown += OnInputKeyDown;
        TxtEjeEndX.KeyDown += OnInputKeyDown;
        TxtEjeEndY.KeyDown += OnInputKeyDown;

        // Muro inputs
        TxtMuroStartX.LostFocus += (s, e) => CommitMuro();
        TxtMuroStartY.LostFocus += (s, e) => CommitMuro();
        TxtMuroEndX.LostFocus += (s, e) => CommitMuro();
        TxtMuroEndY.LostFocus += (s, e) => CommitMuro();
        TxtMuroEspesor.LostFocus += (s, e) => CommitMuro();
        TxtMuroAltura.LostFocus += (s, e) => CommitMuro();

        TxtMuroStartX.KeyDown += OnInputKeyDown;
        TxtMuroStartY.KeyDown += OnInputKeyDown;
        TxtMuroEndX.KeyDown += OnInputKeyDown;
        TxtMuroEndY.KeyDown += OnInputKeyDown;
        TxtMuroEspesor.KeyDown += OnInputKeyDown;
        TxtMuroAltura.KeyDown += OnInputKeyDown;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (EditorCanvas.SelectedElement is Losa) CommitLosa();
            else if (EditorCanvas.SelectedElement is Viga) CommitViga();
            else if (EditorCanvas.SelectedElement is Columna) CommitColumna();
            else if (EditorCanvas.SelectedElement is EjeEstructural) CommitEje();
            else if (EditorCanvas.SelectedElement is Muro) CommitMuro();
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

    private void CommitEje()
    {
        if (_updatingProperties || EditorCanvas.SelectedElement is not EjeEstructural eje) return;

        bool isValid = true;
        isValid &= ValidateStringNotEmpty(TxtEjeEtiqueta, out string etiqueta);
        isValid &= ValidateDouble(TxtEjeStartX, out double sx, PlantaValidationRules.IsValidCoordinate);
        isValid &= ValidateDouble(TxtEjeStartY, out double sy, PlantaValidationRules.IsValidCoordinate);
        isValid &= ValidateDouble(TxtEjeEndX, out double ex, PlantaValidationRules.IsValidCoordinate);
        isValid &= ValidateDouble(TxtEjeEndY, out double ey, PlantaValidationRules.IsValidCoordinate);

        if (!isValid)
        {
            TxtStatus.Text = "✕ Error: Coordenadas del eje deben ser válidas. La etiqueta no puede estar vacía.";
            return;
        }

        eje.Etiqueta = etiqueta;
        eje.PuntoInicio = new PuntoCad(sx, sy);
        eje.PuntoFin = new PuntoCad(ex, ey);

        TxtStatus.Text = $"Eje {eje.Etiqueta} actualizado con éxito.";
        EditorCanvas.InvalidateVisual();
    }

    private void CommitMuro()
    {
        if (_updatingProperties || EditorCanvas.SelectedElement is not Muro m) return;

        bool isValid = true;
        isValid &= ValidateDouble(TxtMuroStartX, out double sx, PlantaValidationRules.IsValidCoordinate);
        isValid &= ValidateDouble(TxtMuroStartY, out double sy, PlantaValidationRules.IsValidCoordinate);
        isValid &= ValidateDouble(TxtMuroEndX, out double ex, PlantaValidationRules.IsValidCoordinate);
        isValid &= ValidateDouble(TxtMuroEndY, out double ey, PlantaValidationRules.IsValidCoordinate);
        isValid &= ValidateDouble(TxtMuroEspesor, out double espesor, v => v > 0);
        isValid &= ValidateDouble(TxtMuroAltura, out double altura, v => v > 0);

        if (!isValid)
        {
            TxtStatus.Text = "✕ Error: Valores de muro inválidos.";
            return;
        }

        m.PuntoInicio = new PuntoCad(sx, sy);
        m.PuntoFin = new PuntoCad(ex, ey);
        m.Espesor = espesor;
        m.Altura = altura;

        TxtStatus.Text = $"Muro {m.Id} actualizado con éxito.";
        EditorCanvas.InvalidateVisual();
    }

    private void OnGenerarVigaContinuaClick(object? sender, RoutedEventArgs e)
    {
        var eje = EditorCanvas.SelectedElement as EjeEstructural;
        var nivel = EditorCanvas.Nivel;
        if (eje == null || nivel == null) return;

        try
        {
            var columnas = nivel.Columnas;
            var viga = GeneradorVigas.VigaContinuaDeColumnas(eje, columnas, 0.0, 0.5, "D");
            int newId = nivel.Vigas.Count > 0 ? nivel.Vigas.Max(v => v.Id) + 1 : 1;
            viga.Id = newId;
            viga.Nombre = $"V-{newId} ({eje.Etiqueta})";
            nivel.Vigas.Add(viga);
            
            EditorCanvas.SelectedElement = viga;
            TxtStatus.Text = $"Viga continua {viga.Nombre} generada con éxito (Apoyos: {viga.Apoyos.Count}).";
            EditorCanvas.InvalidateVisual();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"✕ Error al generar viga continua: {ex.Message}";
        }
    }

    private void OnVerElevacionClick(object? sender, RoutedEventArgs e)
    {
        var eje = EditorCanvas.SelectedElement as EjeEstructural;
        var edificio = EditorCanvas.Edificio;
        if (eje == null || edificio == null) return;

        var window = new SeccionElevacionWindow();
        window.Setup(eje, edificio);
        
        // Use TopLevel to get the window
        if (TopLevel.GetTopLevel(this) is Window parentWindow)
        {
            window.ShowDialog(parentWindow);
        }
        else
        {
            window.Show();
        }
    }

    private void OnEliminarClick(object? sender, RoutedEventArgs e)
    {
        var selected = EditorCanvas.SelectedElement;
        var nivel = EditorCanvas.Nivel;
        if (selected == null || nivel == null) return;

        if (selected is Losa l)
        {
            foreach (var sys in nivel.Sistemas)
            {
                if (sys.Losas.Contains(l))
                {
                    sys.Losas.Remove(l);
                    break;
                }
            }
        }
        else if (selected is Viga v)
        {
            nivel.Vigas.Remove(v);
        }
        else if (selected is Columna c)
        {
            nivel.Columnas.Remove(c);
        }
        else if (selected is Muro m)
        {
            foreach (var sys in nivel.Sistemas)
            {
                if (sys.Muros.Contains(m))
                {
                    sys.Muros.Remove(m);
                    break;
                }
            }
        }
        else if (selected is EjeEstructural eje && EditorCanvas.Edificio != null)
        {
            EditorCanvas.Edificio.Ejes.Remove(eje);
        }

        EditorCanvas.SelectedElement = null;
        EditorCanvas.InvalidateVisual();
        TxtStatus.Text = "Elemento eliminado.";
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
