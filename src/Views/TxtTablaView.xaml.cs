using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using LosasPlus.Services;

namespace LosasPlus.Views;

/// <summary>
/// Vista tabular del .TXT de salida del motor. Parsea el texto en columnas y
/// permite seleccionar/copiar/editar celdas tipo Excel. Las celdas modificadas
/// se marcan con fondo amarillo y se pueden exportar al .txt modificado sin
/// pisar el archivo original.
/// </summary>
public partial class TxtTablaView : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(TxtTablaView),
            new PropertyMetadata(null, OnTextChanged));

    /// <summary>Path del .txt original — usado para sugerir nombre de exportación.</summary>
    public static readonly DependencyProperty OriginalPathProperty =
        DependencyProperty.Register(nameof(OriginalPath), typeof(string), typeof(TxtTablaView),
            new PropertyMetadata(""));

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string OriginalPath
    {
        get => (string)GetValue(OriginalPathProperty);
        set => SetValue(OriginalPathProperty, value);
    }

    private TxtTabla? _tabla;

    public TxtTablaView()
    {
        InitializeComponent();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TxtTablaView)d).Reparse((string?)e.NewValue);

    private void Reparse(string? texto)
    {
        Grid.Columns.Clear();
        if (string.IsNullOrEmpty(texto))
        {
            Grid.ItemsSource = null;
            _tabla = null;
            UpdateStats();
            return;
        }

        _tabla = TxtTabla.Parse(texto);

        // Una columna por cada columna detectada en la tabla. Cada celda es un
        // TextBox bindeado a TxtCelda.Valor con trigger de fondo amarillo si
        // está modificada.
        for (int i = 0; i < _tabla.CantidadColumnas; i++)
        {
            int idx = i;  // capture
            var col = new DataGridTemplateColumn
            {
                Header = $"C{i + 1}",
                Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                CellTemplate = BuildCellTemplate(idx, editable: false),
                CellEditingTemplate = BuildCellTemplate(idx, editable: true),
            };
            Grid.Columns.Add(col);
        }

        Grid.ItemsSource = _tabla.Filas;
        // Suscribir a cambios para actualizar el contador
        foreach (var f in _tabla.Filas)
            foreach (var c in f.Celdas)
                c.PropertyChanged += OnCeldaPropertyChanged;
        UpdateStats();
    }

    private static DataTemplate BuildCellTemplate(int columnIndex, bool editable)
    {
        // Construir un DataTemplate programático equivalente a:
        // <TextBlock Text="{Binding Celdas[i].Valor}" Background="{...trigger Modificada}" />
        // o un TextBox cuando está en modo edición.
        var dt = new DataTemplate(typeof(TxtFilaTabla));

        var factory = new FrameworkElementFactory(editable ? typeof(TextBox) : typeof(TextBlock));
        var path = $"Celdas[{columnIndex}].Valor";
        var binding = new Binding(path)
        {
            Mode = editable ? BindingMode.TwoWay : BindingMode.OneWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            // FallbackValue evita NullReference si la fila tiene menos celdas que el max
            FallbackValue = "",
            TargetNullValue = "",
        };
        factory.SetValue(editable ? TextBox.TextProperty : TextBlock.TextProperty, binding);
        factory.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2));

        // Trigger de fondo: si Celdas[i].Modificada == true → fondo amarillo claro
        var modBinding = new Binding($"Celdas[{columnIndex}].Modificada")
        {
            Mode = BindingMode.OneWay,
            FallbackValue = false,
        };
        var trigger = new DataTrigger { Binding = modBinding, Value = true };
        trigger.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(120, 255, 235, 59))));  // amarillo semitransparente

        var style = new Style(editable ? typeof(TextBox) : typeof(TextBlock));
        style.Triggers.Add(trigger);
        factory.SetValue(FrameworkElement.StyleProperty, style);

        dt.VisualTree = factory;
        return dt;
    }

    private void OnCeldaPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TxtCelda.Modificada) ||
            e.PropertyName == nameof(TxtCelda.Valor))
            UpdateStats();
    }

    private void UpdateStats()
    {
        if (_tabla is null || _tabla.Filas.Count == 0)
        {
            StatsTextBlock.Text = "(sin contenido — abre un .txt del motor primero)";
            ExportarBtn.IsEnabled = false;
            ResetBtn.IsEnabled = false;
            return;
        }
        var totalModificadas = _tabla.Filas.Sum(f => f.Celdas.Count(c => c.Modificada));
        if (totalModificadas == 0)
        {
            StatsTextBlock.Text = $"{_tabla.Filas.Count} filas × {_tabla.CantidadColumnas} columnas — sin modificaciones.";
            ExportarBtn.IsEnabled = false;
            ResetBtn.IsEnabled = false;
        }
        else
        {
            StatsTextBlock.Text = $"{_tabla.Filas.Count} filas × {_tabla.CantidadColumnas} columnas — {totalModificadas} celda(s) modificada(s).";
            ExportarBtn.IsEnabled = true;
            ResetBtn.IsEnabled = true;
        }
    }

    private void OnExportarClick(object sender, RoutedEventArgs e)
    {
        if (_tabla is null) return;
        var pathOrig = OriginalPath;
        if (string.IsNullOrWhiteSpace(pathOrig))
        {
            MessageBox.Show("No hay un .txt original asociado — primero abre un archivo de salida del motor.",
                "Exportar TXT modificado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var nuevoPath = _tabla.ExportarPreservandoOriginal(pathOrig);
            MessageBox.Show($"Guardado en:\n{nuevoPath}\n\nEl archivo original no se modificó.",
                "Exportar TXT modificado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al exportar: {ex.Message}",
                "Exportar TXT modificado", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (_tabla is null) return;
        var confirma = MessageBox.Show(
            "Esta acción descarta todas las modificaciones en celdas y vuelve a parsear el TXT original.\n\n¿Continuar?",
            "Revertir cambios", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirma != MessageBoxResult.Yes) return;
        Reparse(Text);
    }
}
