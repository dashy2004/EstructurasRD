using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LosasPlus.Models;

namespace LosasPlus.Views;

/// <summary>
/// Panel con todos los TIPOs del catálogo en grilla wrap. Click en un icono dispara
/// <see cref="TipoSelected"/> con el código del tipo elegido. Al hover muestra tooltip
/// con la descripción.
/// </summary>
public partial class TiposLosaPanel : UserControl
{
    /// <summary>Callback que recibe el código del tipo clickeado.</summary>
    public Action<int>? TipoSelected { get; set; }

    public TiposLosaPanel()
    {
        InitializeComponent();
        // Ordenamos por (1er dígito, 2do dígito) para reproducir el layout del programa original
        Items.ItemsSource = TipoLosa.Catalogo.Values
            .OrderBy(t => t.Codigo / 10)
            .ThenBy(t => t.Codigo % 10)
            .ToList();
    }

    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is int codigo)
            TipoSelected?.Invoke(codigo);
    }
}
