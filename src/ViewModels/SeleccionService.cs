using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LosasPlus.Topologia;

namespace LosasPlus.ViewModels;

/// <summary>
/// Servicio singleton de coordinación de la selección bidireccional entre el
/// visor 3D y los paneles 2D del shell (Fase 3D-I3 del Plan Maestro de
/// Expansión 3D). Mantiene un único <see cref="DomainKey"/> activo —
/// representa el elemento estructural actualmente "foco" — y lo publica a
/// todos los suscriptores cuando cambia.
///
/// <para>
/// <b>Anti-recursión</b>: la sincronización es inherentemente
/// bidireccional. Cuando el viewport 3D publica un nuevo
/// <see cref="DomainKey"/>, el shell reacciona cambiando
/// <c>MainViewModel.ModoActivo</c> y enfocando el elemento en el editor
/// 2D — pero ese setter del editor 2D, a su vez, podría disparar un
/// nuevo aviso al <see cref="SeleccionService"/>. Sin protección esto
/// generaría un bucle infinito de eventos.
/// </para>
///
/// <para>
/// La defensa es el flag <c>_actualizando</c>: la primera invocación de
/// <see cref="FijarSeleccion"/> levanta el flag dentro de un
/// <c>try/finally</c>; cualquier re-entrada durante la propagación del
/// evento es ignorada silenciosamente. La idempotencia adicional
/// (ignorar el mismo <see cref="DomainKey"/>) corta los bucles incluso
/// si dos suscriptores honestamente intentan sincronizar al mismo
/// elemento.
/// </para>
///
/// <para>
/// El servicio vive en <c>src/ViewModels</c> (capa de presentación) y
/// referencia sólo a <c>src.Core/Topologia</c> (<see cref="DomainKey"/>).
/// Sin dependencias de HelixToolkit / WPF gráfico.
/// </para>
/// </summary>
public sealed class SeleccionService : INotifyPropertyChanged
{
    private DomainKey? _elementoSeleccionado;
    private bool _actualizando;

    /// <summary>
    /// Elemento estructural actualmente seleccionado en el shell, o
    /// <c>null</c> si no hay selección. La asignación pública pasa por
    /// <see cref="FijarSeleccion"/> que aplica las protecciones
    /// anti-recursión + idempotencia.
    /// </summary>
    public DomainKey? ElementoSeleccionado
    {
        get => _elementoSeleccionado;
        private set
        {
            _elementoSeleccionado = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Evento disparado <b>una sola vez</b> por mutación efectiva de la
    /// selección. Los suscriptores reciben el nuevo <see cref="DomainKey"/>
    /// (o <c>null</c> al deseleccionar). El emisor garantiza:
    /// <list type="bullet">
    ///   <item>No se dispara cuando el valor entrante es igual al actual
    ///   (idempotencia).</item>
    ///   <item>No se dispara durante una propagación en curso
    ///   (anti-recursión).</item>
    /// </list>
    /// </summary>
    public event Action<DomainKey?>? OnSeleccionCambiada;

    /// <summary>
    /// <c>true</c> mientras un <see cref="FijarSeleccion"/> está
    /// propagando su evento. Expuesto solo para tests / introspección;
    /// los consumidores normales no deberían leerlo.
    /// </summary>
    public bool EstaActualizando => _actualizando;

    /// <summary>
    /// Mutación segura de la selección. Aplica las dos defensas en orden:
    /// <list type="number">
    ///   <item>Si <c>_actualizando == true</c>, la llamada se ignora —
    ///   protege contra re-entrada durante la propagación del evento
    ///   anterior.</item>
    ///   <item>Si <paramref name="nuevoSeleccionado"/> es igual al valor
    ///   actual, la llamada se ignora — idempotencia, evita publicar
    ///   eventos duplicados aun cuando dos suscriptores hagan ping-pong
    ///   con el mismo <see cref="DomainKey"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="nuevoSeleccionado">
    /// Nuevo elemento a seleccionar. <c>null</c> deselecciona.
    /// </param>
    /// <param name="emisor">
    /// Objeto que origina la mutación (viewport 3D, panel 2D, etc.).
    /// Hoy se ignora; en iteraciones futuras puede usarse para
    /// auditoría / logging del origen del evento.
    /// </param>
    public void FijarSeleccion(DomainKey? nuevoSeleccionado, object? emisor)
    {
        if (_actualizando) return;
        if (Nullable.Equals(ElementoSeleccionado, nuevoSeleccionado)) return;

        try
        {
            _actualizando = true;
            ElementoSeleccionado = nuevoSeleccionado;
            OnSeleccionCambiada?.Invoke(nuevoSeleccionado);
        }
        finally
        {
            _actualizando = false;
        }
    }

    /// <summary>
    /// Atajo para limpiar la selección — equivalente a
    /// <c>FijarSeleccion(null, emisor)</c>.
    /// </summary>
    public void LimpiarSeleccion(object? emisor) => FijarSeleccion(null, emisor);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
