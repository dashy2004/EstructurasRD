using Avalonia.Metadata;

// Declara las mismas 2 namespaces locales bajo el URI compartido. Ver
// src.UI.Shared/Properties/AssemblyInfo.cs para el contexto: el URI
// unifica xmlns:vw cross-assembly de modo que <vw:NivelesView/> (local)
// y <vw:ValidacionView/> (shared) resuelvan con un solo xmlns.
[assembly: XmlnsDefinition("https://schemas.memoriaplus.dev/ui", "MemoriaPlus.ViewModels")]
[assembly: XmlnsDefinition("https://schemas.memoriaplus.dev/ui", "MemoriaPlus.Views")]
