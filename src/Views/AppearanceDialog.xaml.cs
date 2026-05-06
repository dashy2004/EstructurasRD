using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LosasPlus.Services;

namespace LosasPlus.Views;

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
            // Header de categoría
            TokensHost.Children.Add(new TextBlock
            {
                Text = grp.Key.ToUpperInvariant(),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["FgMuted"],
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

    private void OnSavePersist(object sender, RoutedEventArgs e)
    {
        ThemeCustomizer.GuardarOverrides();
        MessageBox.Show("Colores guardados como default.\nSe aplicarán cada vez que abras LosasPlus.",
            "Apariencia", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show("Esto descarta todas las personalizaciones y vuelve al theme actual original.\n¿Continuar?",
            "Restaurar valores", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        ThemeCustomizer.RestaurarDefaults();
        BuildPickers();  // refrescar pickers con valores nuevos
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
