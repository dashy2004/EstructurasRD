using System.Windows;

namespace MemoriaPlus;

/// <summary>
/// Punto de entrada de Memoria Plus. La inicializacion no triviale
/// (carga de plantillas, deteccion de proyecto activo, etc.) ocurre en
/// <see cref="MainWindow"/> via su ViewModel — App.xaml.cs solo monta el
/// host WPF.
/// </summary>
public partial class App : Application
{
}
