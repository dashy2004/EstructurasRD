namespace MemoriaPlus.ViewModels;

/// <summary>
/// Resumen de un proyecto guardado en disco, en la forma que la UI lo
/// consume (sidebar de recientes, hub del Explorador, lista de resultados
/// de Búsqueda Global). Es una proyección de
/// <see cref="LosasPlus.Persistence.RecentEntry"/> al espacio UI.
///
/// <para>
/// Vivía en MainViewModel.cs (src.Memoria) hasta el commit 30. Se promovió
/// a la librería compartida (src.UI.Shared) para que <see cref="BusquedaViewModel"/>
/// — también compartido — pueda referenciarlo sin que LosasPlus.App tenga
/// que tomar dependencia de MemoriaPlus.App.
/// </para>
/// </summary>
public sealed record ProyectoResumen(
    string Nombre,
    string Ingeniero,
    string Codia,
    int    Niveles,
    string UltimaEdicion,
    string Estado,
    string Path = "");
