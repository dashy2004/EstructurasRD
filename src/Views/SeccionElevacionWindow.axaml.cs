using System;
using System.Linq;
using Avalonia.Controls;
using LosasPlus.Models;
using LosasPlus.Models.Cad;

namespace LosasPlus.Views;

public partial class SeccionElevacionWindow : Window
{
    public SeccionElevacionWindow()
    {
        InitializeComponent();
    }

    public void Setup(EjeEstructural eje, Edificio edificio)
    {
        TxtTitulo.Text = $"Elevación del Eje {eje.Etiqueta}";
        CanvasElevacion.Eje = eje;
        CanvasElevacion.Edificio = edificio;
        CanvasElevacion.InvalidateVisual();
    }
}
