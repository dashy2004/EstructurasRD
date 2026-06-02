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

        // Wire toolbar tools
        BtnPuntero.IsCheckedChanged += (s, e) => { if (BtnPuntero.IsChecked == true) EditorCanvas.ActiveTool = "Puntero"; };
        BtnAddLosa.IsCheckedChanged += (s, e) => { if (BtnAddLosa.IsChecked == true) EditorCanvas.ActiveTool = "Losa"; };
        BtnAddViga.IsCheckedChanged += (s, e) => { if (BtnAddViga.IsChecked == true) EditorCanvas.ActiveTool = "Viga"; };
        BtnAddCol.IsCheckedChanged += (s, e) => { if (BtnAddCol.IsChecked == true) EditorCanvas.ActiveTool = "Columna"; };

        // Wire buttons
        BtnRecalcular.Click += OnRecalcularClick;
        BtnEliminar.Click += OnEliminarClick;
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

    private void OnCanvasSelectionChanged(object? selected)
    {
        _updatingProperties = true;

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

    private void CommitLosa()
    {
        if (_updatingProperties || EditorCanvas.SelectedElement is not Losa l) return;

        double.TryParse(TxtLosaX.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double x);
        double.TryParse(TxtLosaY.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double y);
        double.TryParse(TxtLosaLx.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double lx);
        double.TryParse(TxtLosaLy.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double ly);
        double.TryParse(TxtLosaCarga.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double carga);
        double.TryParse(TxtLosaEspesor.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double espesor);

        l.CoordenadaX = x;
        l.CoordenadaY = y;
        l.Lx = lx > 0 ? lx : 1.0;
        l.Ly = ly > 0 ? ly : 1.0;
        l.Carga = carga;
        l.Espesor = espesor;

        EditorCanvas.InvalidateVisual();
    }

    private void CommitViga()
    {
        if (_updatingProperties || EditorCanvas.SelectedElement is not Viga v) return;

        double.TryParse(TxtVigaX.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double x);
        double.TryParse(TxtVigaY.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double y);
        double.TryParse(TxtVigaAngulo.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double ang);

        v.Nombre = TxtVigaNombre.Text ?? v.Nombre;
        v.OrigenX = x;
        v.OrigenY = y;
        v.AnguloGrados = ang;

        EditorCanvas.InvalidateVisual();
    }

    private void CommitColumna()
    {
        if (_updatingProperties || EditorCanvas.SelectedElement is not Columna c) return;

        double.TryParse(TxtColX.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double x);
        double.TryParse(TxtColY.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double y);
        double.TryParse(TxtColBase.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double cb);
        double.TryParse(TxtColPeralte.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double cp);
        double.TryParse(TxtColAltura.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double alt);

        c.Nombre = TxtColNombre.Text ?? c.Nombre;
        c.CoordenadaX = x;
        c.CoordenadaY = y;
        c.Base = cb > 0 ? cb : 0.1;
        c.Peralte = cp > 0 ? cp : 0.1;
        c.Altura = alt > 0 ? alt : 1.0;

        EditorCanvas.InvalidateVisual();
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

        EditorCanvas.SelectedElement = null;
        EditorCanvas.InvalidateVisual();
    }

    private void OnRecalcularClick(object? sender, RoutedEventArgs e)
    {
        var nivel = EditorCanvas.Nivel;
        if (nivel == null) return;

        double.TryParse(TxtQAdm.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double qAdm);
        if (qAdm <= 0) qAdm = 15.0;

        try
        {
            // 1. Reparto geométrico losa -> viga
            int vigasCargadas = RepartoGeometrico.AplicarCargasGeometricas(nivel, "D");

            // 2. Descenso geométrico viga -> columna -> zapata
            var zapatasPredim = DescensoColumnas.PredimensionarGeometrico(nivel, qAdm);

            TxtStatus.Text = $"Descenso recalculado: {vigasCargadas} vigas cargadas, {zapatasPredim.Count} zapatas predimensionadas.";
            EditorCanvas.InvalidateVisual();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error al recalcular: {ex.Message}";
        }
    }
}
