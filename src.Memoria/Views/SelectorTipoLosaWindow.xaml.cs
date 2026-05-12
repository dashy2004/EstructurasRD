using System;
using System.Windows;
using MemoriaPlus.ViewModels;

namespace MemoriaPlus.Views;

/// <summary>
/// Diálogo modal "Seleccionar tipo de losa". El consumidor crea la ventana
/// pasando un tipo inicial opcional, llama a <see cref="System.Windows.Window.ShowDialog"/>
/// y luego lee <see cref="TipoConfirmado"/>: null si el usuario canceló, un
/// código numérico si confirmó.
/// </summary>
public partial class SelectorTipoLosaWindow : Window
{
    private readonly SelectorTipoLosaViewModel _vm;

    public SelectorTipoLosaWindow(int? tipoInicial = null)
    {
        InitializeComponent();
        _vm = new SelectorTipoLosaViewModel(tipoInicial);
        DataContext = _vm;
        _vm.Cerrar += OnCerrar;
    }

    /// <summary>Código confirmado por el usuario, o null si canceló.</summary>
    public int? TipoConfirmado => _vm.TipoConfirmado;

    private void OnCerrar(object? sender, EventArgs e)
    {
        DialogResult = _vm.TipoConfirmado.HasValue;
        Close();
    }
}
