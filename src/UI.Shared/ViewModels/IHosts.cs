namespace Shared.UI.ViewModels;

/// <summary>
/// Implementado por un VM raíz que aloja un <see cref="BusquedaViewModel"/>
/// como sub-VM. Permite que <c>BusquedaView</c> (compartida entre apps)
/// localice el sub-VM cuando su DataContext es el VM raíz en lugar del
/// sub-VM directo.
/// </summary>
public interface IBusquedaHost
{
    BusquedaViewModel Busqueda { get; }
}

/// <summary>
/// Análogo a <see cref="IBusquedaHost"/> para <see cref="ValidacionViewModel"/>.
/// Reservado para uso futuro (ValidacionView aún resuelve via {Binding
/// Validacion.X} directo).
/// </summary>
public interface IValidacionHost
{
    ValidacionViewModel Validacion { get; }
}
