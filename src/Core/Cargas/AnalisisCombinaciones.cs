using System.Collections.Generic;
using System.Linq;

namespace LosasPlus.Cargas;

/// <summary>
/// Validaciones normativas de la base de cargas de un proyecto (Fase 2,
/// Iteración 4). Lógica <b>pura</b> — alimenta el panel de resumen de la
/// pestaña «Cargas y Combinaciones».
/// </summary>
public static class AnalisisCombinaciones
{
    /// <summary>
    /// Revisa una <see cref="CombinacionesProyecto"/> y devuelve la lista de
    /// alertas normativas detectadas — vacía si todo está correcto.
    /// </summary>
    public static IReadOnlyList<string> Validar(CombinacionesProyecto? combinaciones)
    {
        var alertas = new List<string>();
        if (combinaciones is null) return alertas;

        if (combinaciones.Casos.Count == 0)
            alertas.Add("No hay casos de carga definidos.");
        if (combinaciones.Combinaciones.Count == 0)
            alertas.Add("No hay combinaciones definidas.");

        // Falta una combinación de sólo carga muerta: un único término sobre
        // un caso de tipo Muerta (p. ej. «D» o «1.4D»).
        var codigosMuerta = combinaciones.Casos
            .Where(c => c.Tipo == TipoCasoCarga.Muerta)
            .Select(c => c.Codigo)
            .ToHashSet();
        if (codigosMuerta.Count > 0)
        {
            bool haySoloMuerta = combinaciones.Combinaciones.Any(k =>
                k.Terminos.Count == 1 && codigosMuerta.Contains(k.Terminos[0].CodigoCaso));
            if (!haySoloMuerta)
                alertas.Add("Falta una combinación de sólo carga muerta.");
        }

        // Casos que no participan en ninguna combinación.
        var usados = combinaciones.Combinaciones
            .SelectMany(k => k.Terminos)
            .Select(t => t.CodigoCaso)
            .ToHashSet();
        foreach (var caso in combinaciones.Casos)
            if (!usados.Contains(caso.Codigo))
                alertas.Add($"El caso «{caso.Codigo}» no se usa en ninguna combinación.");

        return alertas;
    }
}
