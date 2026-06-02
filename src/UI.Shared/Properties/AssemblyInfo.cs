using Avalonia.Metadata;

// Port a Avalonia: el atributo XmlnsDefinition de WPF (System.Windows.Markup) se
// reemplaza por el de Avalonia (Avalonia.Metadata). Mapea las namespaces
// compartibles a un único URI XAML para que los consumidores (src.Memoria, y
// más adelante el LosasPlus.App portado) reusen las mismas Views sin saber en
// qué dll viven.
[assembly: XmlnsDefinition("https://schemas.memoriaplus.dev/ui", "MemoriaPlus.Common")]
[assembly: XmlnsDefinition("https://schemas.memoriaplus.dev/ui", "MemoriaPlus.ViewModels")]
[assembly: XmlnsDefinition("https://schemas.memoriaplus.dev/ui", "MemoriaPlus.Views")]
[assembly: XmlnsDefinition("https://schemas.memoriaplus.dev/ui", "MemoriaPlus.Services")]
