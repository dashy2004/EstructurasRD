using System;

namespace LosasPlus.Models;

/// <summary>
/// Contrato para notificar a la UI (vistas 2D y 3D) de cambios <b>profundos</b> en
/// el modelo que requieren un repintado/refresco completo — especialmente cuando
/// el motor de cálculo o los importadores modifican el dominio sin pasar por los
/// setters bindeados de la UI. <see cref="Edificio"/>, <see cref="Nivel"/> y
/// <see cref="Sistema"/> lo implementan y propagan el evento hacia arriba, de modo
/// que suscribirse al <see cref="ModeloCambiado"/> del edificio basta para enterarse
/// de cualquier cambio anidado (losa, viga, columna).
/// </summary>
public interface IModeloObservable
{
    /// <summary>Se dispara cuando el modelo cambia de forma que requiere redibujar.</summary>
    event EventHandler? ModeloCambiado;

    /// <summary>Dispara <see cref="ModeloCambiado"/>.</summary>
    void NotificarModeloCambiado();
}
