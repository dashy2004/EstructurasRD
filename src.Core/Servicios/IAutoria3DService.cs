using LosasPlus.Grillas;
using LosasPlus.Models;
using LosasPlus.Topologia;
using LosasPlus.Vigas;

namespace LosasPlus.Servicios;

/// <summary>
/// Servicio de dominio abstracto para la autoría 3D — encapsula la
/// creación y eliminación de elementos estructurales desde el visor 3D
/// (Liga B paso B3 del Plan Maestro).
///
/// <para>
/// <b>Razón de existencia (directiva 3 de B3)</b>: el
/// <c>MainViewModel</c> inyecta esta interface en lugar de invocar
/// engines estáticos directamente (<c>GridCreationEngine.*</c>). Esto:
/// <list type="bullet">
///   <item>Desacopla la toolbar UI del Viewport3D: la mutación pasa
///   por un servicio abstracto, no por métodos del sub-VM gráfico.</item>
///   <item>Permite mocking trivial en tests (un fake con contadores
///   reemplaza la implementación default sin levantar hilo UI ni
///   HelixToolkit).</item>
///   <item>Abre la puerta a implementaciones alternativas (futura
///   inyección desde SAF/IFC, comandos remotos, etc.).</item>
/// </list>
/// </para>
///
/// <para>
/// Tipo puro de dominio — sin dependencias de WPF, HelixToolkit ni
/// DirectX. Vive en <c>src.Core/Servicios/</c>.
/// </para>
/// </summary>
public interface IAutoria3DService
{
    /// <summary>
    /// Crea una <see cref="Viga"/> entre dos puntos del plano del
    /// nivel, con snap a la grilla estructural. La viga se agrega al
    /// <paramref name="nivel"/> con Id autoincremental.
    ///
    /// <para>
    /// <b>Validación defensiva (directiva 1)</b>: rechaza payloads
    /// con NaN, Infinity, o longitud post-snap menor a 1 cm devolviendo
    /// <c>null</c> sin lanzar excepción.
    /// </para>
    /// </summary>
    /// <returns>La viga creada, o <c>null</c> si la validación rechazó el payload.</returns>
    Viga? CrearVigaConSnap(
        Nivel nivel, GrillaEstructural grilla,
        double x1, double y1, double x2, double y2);

    /// <summary>
    /// Elimina el elemento identificado por <paramref name="key"/> del
    /// <paramref name="proyecto"/>. Para columnas en nivel base, remueve
    /// también la zapata pareada (mismo Id) por consistencia M2C.
    ///
    /// <para>
    /// <b>Semánticas Ghost Deletion (directiva 2)</b>: retorna
    /// <c>false</c> si la entidad no existe (proceso concurrente, Id
    /// huérfano, tipo no soportado o muro inmutable) pero NUNCA lanza
    /// excepción. El consumidor trata <c>false</c> como éxito silencioso.
    /// </para>
    /// </summary>
    /// <returns><c>true</c> si removió al menos un elemento; <c>false</c> en cualquier otro caso.</returns>
    bool BorrarElementoPorKey(Proyecto proyecto, DomainKey key);
}
