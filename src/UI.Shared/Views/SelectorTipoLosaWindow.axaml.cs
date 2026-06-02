using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MemoriaPlus.ViewModels;

namespace MemoriaPlus.Views;

/// <summary>
/// Diálogo modal "Seleccionar tipo de losa". El consumidor lo abre con
/// <c>await ShowDialog&lt;int?&gt;(owner)</c>: devuelve el código confirmado o
/// null si canceló.
///
/// <para>Port a Avalonia: WPF usaba DialogResult; aquí <see cref="Window.Close(object)"/>
/// con el código (o null) como resultado.</para>
/// </summary>
public partial class SelectorTipoLosaWindow : Window
{
    private readonly SelectorTipoLosaViewModel _vm;

    // Constructor sin parámetros para el cargador XAML.
    public SelectorTipoLosaWindow() : this(null) { }

    public SelectorTipoLosaWindow(int? tipoInicial)
    {
        AvaloniaXamlLoader.Load(this);
        _vm = new SelectorTipoLosaViewModel(tipoInicial);
        DataContext = _vm;
        _vm.Cerrar += OnCerrar;
    }

    /// <summary>Código confirmado por el usuario, o null si canceló.</summary>
    public int? TipoConfirmado => _vm.TipoConfirmado;

    private void OnCerrar(object? sender, EventArgs e) => Close(_vm.TipoConfirmado);
}
