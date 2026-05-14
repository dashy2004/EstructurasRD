using System.Windows.Markup;

// Mapea las 3 namespaces compartibles a un único URI XAML. El mismo URI
// también lo declara src.Memoria para sus tipos locales — WPF enumera
// ambas declaraciones, así un único xmlns:vw="..." resuelve tipos de
// cualquiera de las 2 dlls. Permite a consumidores (src.Memoria, src/)
// reusar las mismas Views sin saber en qué dll viven.
[assembly: XmlnsDefinition("https://schemas.memoriaplus.dev/ui", "MemoriaPlus.Common")]
[assembly: XmlnsDefinition("https://schemas.memoriaplus.dev/ui", "MemoriaPlus.ViewModels")]
[assembly: XmlnsDefinition("https://schemas.memoriaplus.dev/ui", "MemoriaPlus.Views")]
