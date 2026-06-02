using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using LosasPlus.Models;

namespace LosasPlus.Views;

/// <summary>
/// Panel con todos los TIPOs del catálogo en grilla wrap. Click en un icono
/// dispara <see cref="TipoSelected"/>. Port a Avalonia: MouseLeftButtonDown→PointerPressed;
/// Border.ToolTip→ToolTip.Tip; trigger IsMouseOver→selector :pointerover.
/// </summary>
public partial class TiposLosaPanel : UserControl
{
    public Action<int>? TipoSelected { get; set; }

    public TiposLosaPanel()
    {
        InitializeComponent();
        Items.ItemsSource = TipoLosa.Catalogo.Values
            .OrderBy(t => t.Codigo / 10)
            .ThenBy(t => t.Codigo % 10)
            .ToList();
    }

    private void OnItemClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control fe && fe.Tag is int codigo)
            TipoSelected?.Invoke(codigo);
    }
}
