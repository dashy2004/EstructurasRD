using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media;
using LosasPlus.ViewModels;
using System;
using System.Globalization;

using LosasPlus.Services;
using MemoriaPlus.Services;

namespace LosasPlus.Views;

public partial class AcerosView : UserControl
{
    public AcerosView()
    {
        InitializeComponent();
    }

    private void OnRecargarClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Aceros.Recargar();
    }

    private async void OnExportAcerosXlsxClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var path = await AppServices.Dialogs.SaveFileAsync(
                "Exportar aceros (Excel)", (vm.Sistema.Nombre ?? "aceros") + "_aceros.xlsx", ".xlsx",
                new FileFilter("Excel Workbook", new[] { "*.xlsx" }));
            if (path is not null) vm.ExportarAcerosXlsx(path);
        }
    }
}

public class EstadoToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string estado)
        {
            return estado switch
            {
                "OK" => new SolidColorBrush(Color.Parse("#2E7D32")),
                "REVISAR" => new SolidColorBrush(Color.Parse("#F57C00")),
                "SECCIÓN INSUF." => new SolidColorBrush(Color.Parse("#C62828")),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class AsRatioToWidthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DisenoAceroFila f)
        {
            if (f.AsRequeridoCm2M <= 0) return 100.0;
            double ratio = f.AsProvistaCm2M / f.AsRequeridoCm2M;
            return Math.Min(ratio * 50.0, 100.0); // 50px represents ratio 1.0
        }
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class RatioToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DisenoAceroFila f)
        {
            if (f.AsProvistaCm2M >= f.AsRequeridoCm2M)
                return new SolidColorBrush(Color.Parse("#4CAF50"));
            return new SolidColorBrush(Color.Parse("#F44336"));
        }
        return new SolidColorBrush(Colors.Gray);
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class MultiplyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double v && parameter is string p && double.TryParse(p, NumberStyles.Any, CultureInfo.InvariantCulture, out double factor))
            return v * factor;
        return value;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
