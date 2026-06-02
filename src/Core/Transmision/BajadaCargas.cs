using System;
using System.Collections.Generic;
using System.Linq;
using LosasPlus.Models;

namespace LosasPlus.Transmision;

/// <summary>
/// Carga gravitatoria de un nivel dentro de la bajada de cargas.
/// </summary>
/// <param name="Nombre">Nombre del nivel.</param>
/// <param name="Cota">Cota del nivel, en m.</param>
/// <param name="CargaPropia">Carga del propio nivel = Σ q·Lx·Ly de sus losas (en la unidad de q · m²).</param>
/// <param name="CargaAcumulada">Carga propia más la de todos los niveles por encima (la que el nivel transmite hacia abajo).</param>
public readonly record struct CargaNivel(
    string Nombre,
    double Cota,
    double CargaPropia,
    double CargaAcumulada);

/// <summary>
/// Resultado de la bajada de cargas de un edificio: los niveles ordenados de
/// arriba hacia abajo con su carga acumulada, y la carga total que llega a la
/// base (cimentación).
/// </summary>
public sealed record BajadaResultado(IReadOnlyList<CargaNivel> Niveles, double CargaEnBase);

/// <summary>
/// Bajada de cargas por niveles (Fase J — «Liga G»): acumula la carga
/// gravitatoria de cada planta descendiendo por la <see cref="Nivel.Cota"/>,
/// hasta la carga total que llega a la base (cimentación). Es el eje vertical
/// de la transmisión losa→…→zapata; el reparto horizontal a vigas lo da
/// <see cref="RepartoCargaLosa"/>.
///
/// <para>
/// La carga propia de un nivel es la suma de q·Lx·Ly de todas las losas de
/// todos sus sistemas (idéntica a <c>RepartoLosa.CargaTotal</c> sumada). La
/// asignación a columnas/zapatas concretas requerirá topología que el modelo
/// de dominio aún no tiene; esta clase entrega la magnitud total por nivel.
/// Tipo puro de dominio — multiplataforma y testeable. Unidades: las de q.
/// </para>
/// </summary>
public static class BajadaCargas
{
    /// <summary>Carga gravitatoria propia de un nivel = Σ q·Lx·Ly de las losas de sus sistemas.</summary>
    public static double PesoLosas(Nivel nivel)
    {
        if (nivel is null) return 0;
        double total = 0;
        foreach (var sistema in nivel.Sistemas)
            foreach (var losa in sistema.Losas)
            {
                double area = losa.Lx * losa.Ly;
                if (area > 0 && losa.Carga > 0) total += losa.Carga * area;
            }
        return total;
    }

    /// <summary>
    /// Acumula la carga de los niveles de un <paramref name="edificio"/>
    /// descendiendo por cota (de la más alta a la más baja). Devuelve los
    /// niveles en ese orden (de arriba hacia abajo) con su carga acumulada, y
    /// la carga total en la base. Un edificio nulo o sin niveles devuelve un
    /// resultado vacío.
    /// </summary>
    public static BajadaResultado Acumular(Edificio? edificio)
    {
        if (edificio is null || edificio.Niveles.Count == 0)
            return new BajadaResultado(new List<CargaNivel>(), 0);

        // De arriba (cota mayor) hacia abajo: la carga desciende.
        var ordenados = edificio.Niveles.OrderByDescending(n => n.Cota).ToList();

        var filas = new List<CargaNivel>(ordenados.Count);
        double acumulada = 0;
        foreach (var nivel in ordenados)
        {
            double propia = PesoLosas(nivel);
            acumulada += propia;
            filas.Add(new CargaNivel(nivel.Nombre, nivel.Cota, propia, acumulada));
        }

        return new BajadaResultado(filas, acumulada);
    }
}
