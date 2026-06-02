using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using LosasPlus.Services;
using MemoriaPlus.Services;

namespace LosasPlus.Views;

/// <summary>
/// Diálogo «Personalizar colores»: un ColorPickerControl por token del theme,
/// agrupados por categoría. Port a Avalonia: FontWeights→FontWeight, Brush→IBrush,
/// Application.Current.Resources→FindResource, MessageBox→AppServices.
/// </summary>
public partial class AppearanceDialog : Window
{
    public AppearanceDialog()
    {
        InitializeComponent();
        BuildPickers();
    }

    private void BuildPickers()
    {
        TokensHost.Children.Clear();
        var grouped = ThemeCustomizer.EditableTokens.GroupBy(t => t.Categoria);
        foreach (var grp in grouped)
        {
            TokensHost.Children.Add(new TextBlock
            {
                Text = grp.Key.ToUpperInvariant(),
                FontWeight = FontWeight.Bold,
                FontSize = 11,
                Foreground = Application.Current?.FindResource("FgMuted") as IBrush,
                Margin = new Thickness(0, 12, 0, 6),
            });
            foreach (var token in grp)
            {
                var picker = new ColorPickerControl
                {
                    Margin = new Thickness(0, 0, 0, 6),
                    Height = 100,
                };
                picker.Bind(token);
                TokensHost.Children.Add(picker);
            }
        }
    }

    private void OnSavePersist(object? sender, RoutedEventArgs e)
    {
        ThemeCustomizer.GuardarOverrides();
        _ = AppServices.MessageBox.InfoAsync("Apariencia",
            "Colores guardados como default.\nSe aplicarán cada vez que abras LosasPlus.");
    }

    private async void OnReset(object? sender, RoutedEventArgs e)
    {
        var ok = await AppServices.MessageBox.ConfirmYesNoAsync("Restaurar valores",
            "Esto descarta todas las personalizaciones y vuelve al theme actual original.\n¿Continuar?");
        if (!ok) return;
        ThemeCustomizer.RestaurarDefaults();
        BuildPickers();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
