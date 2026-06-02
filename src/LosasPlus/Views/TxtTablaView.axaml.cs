using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using LosasPlus.Services;
using MemoriaPlus.Services;

namespace LosasPlus.Views;

/// <summary>
/// Vista tabular del .TXT de salida del motor: celdas editables, modificadas en
/// amarillo, exportables sin pisar el original. Port a Avalonia: las columnas
/// programáticas (WPF FrameworkElementFactory/DataTemplate.VisualTree) se
/// reescriben con FuncDataTemplate; DependencyProperty→StyledProperty;
/// MessageBox→AppServices.
/// </summary>
public partial class TxtTablaView : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TxtTablaView, string?>(nameof(Text));

    public static readonly StyledProperty<string> OriginalPathProperty =
        AvaloniaProperty.Register<TxtTablaView, string>(nameof(OriginalPath), "");

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string OriginalPath { get => GetValue(OriginalPathProperty); set => SetValue(OriginalPathProperty, value); }

    private TxtTabla? _tabla;

    private static readonly IValueConverter _modBrush =
        new FuncValueConverter<bool, IBrush?>(m =>
            m ? new SolidColorBrush(Color.FromArgb(120, 255, 235, 59)) : null);

    public TxtTablaView() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            Reparse(change.GetNewValue<string?>());
    }

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

        for (int i = 0; i < _tabla.CantidadColumnas; i++)
        {
            int idx = i;
            Grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = $"C{i + 1}",
                Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                CellTemplate = BuildCellTemplate(idx, editable: false),
                CellEditingTemplate = BuildCellTemplate(idx, editable: true),
            });
        }

        Grid.ItemsSource = _tabla.Filas;
        foreach (var f in _tabla.Filas)
            foreach (var c in f.Celdas)
                c.PropertyChanged += OnCeldaPropertyChanged;
        UpdateStats();
    }

    private static IDataTemplate BuildCellTemplate(int columnIndex, bool editable)
        => new FuncDataTemplate<TxtFilaTabla>((_, _) =>
        {
            var valBinding = new Binding($"Celdas[{columnIndex}].Valor")
            {
                Mode = editable ? BindingMode.TwoWay : BindingMode.OneWay,
                FallbackValue = "",
                TargetNullValue = "",
            };
            var bgBinding = new Binding($"Celdas[{columnIndex}].Modificada")
            {
                Mode = BindingMode.OneWay,
                FallbackValue = false,
                Converter = _modBrush,
            };

            if (editable)
            {
                var tb = new TextBox { Margin = new Thickness(4, 2, 4, 2) };
                tb.Bind(TextBox.TextProperty, valBinding);
                tb.Bind(TextBox.BackgroundProperty, bgBinding);
                return tb;
            }
            var tbl = new TextBlock { Margin = new Thickness(4, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center };
            tbl.Bind(TextBlock.TextProperty, valBinding);
            tbl.Bind(TextBlock.BackgroundProperty, bgBinding);
            return tbl;
        });

    private void OnCeldaPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TxtCelda.Modificada) || e.PropertyName == nameof(TxtCelda.Valor))
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

    private async void OnExportarClick(object? sender, RoutedEventArgs e)
    {
        if (_tabla is null) return;
        var pathOrig = OriginalPath;
        if (string.IsNullOrWhiteSpace(pathOrig))
        {
            await AppServices.MessageBox.InfoAsync("Exportar TXT modificado",
                "No hay un .txt original asociado — primero abre un archivo de salida del motor.");
            return;
        }
        try
        {
            var nuevoPath = _tabla.ExportarPreservandoOriginal(pathOrig);
            await AppServices.MessageBox.InfoAsync("Exportar TXT modificado",
                $"Guardado en:\n{nuevoPath}\n\nEl archivo original no se modificó.");
        }
        catch (Exception ex)
        {
            await AppServices.MessageBox.InfoAsync("Exportar TXT modificado", $"Error al exportar: {ex.Message}");
        }
    }

    private async void OnResetClick(object? sender, RoutedEventArgs e)
    {
        if (_tabla is null) return;
        var ok = await AppServices.MessageBox.ConfirmYesNoAsync("Revertir cambios",
            "Esta acción descarta todas las modificaciones en celdas y vuelve a parsear el TXT original.\n\n¿Continuar?");
        if (!ok) return;
        Reparse(Text);
    }
}
