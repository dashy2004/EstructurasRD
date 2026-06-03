using System;

namespace LosasPlus.Models;

/// <summary>
/// Contrato para notificar a la UI (vistas 2D y 3D) de cambios profundos
/// en el modelo que requieren un repintado/refresco completo, especialmente
/// útil cuando el motor de cálculo o importadores modifican el dominio
/// sin pasar por los setters de la UI.
/// </summary>
public interface IModeloObservable
{
    /// <summary>
    /// Se dispara cuando el modelo sufre un cambio que requiere redibujado.
    /// </summary>
    event EventHandler? ModeloCambiado;

    /// <summary>
    /// Desencadena el evento <see cref="ModeloCambiado"/>.
    /// </summary>
    void NotificarModeloCambiado();
}
