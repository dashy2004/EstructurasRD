using System;
using System.Collections.Generic;
using System.Numerics;
using LosasPlus.Models;

namespace LosasPlus.Render3D;

/// <summary>
/// Una escena 3D ya construida: la lista de <see cref="Segmentos"/> alámbricos y
/// la caja envolvente AABB [<see cref="Min"/>, <see cref="Max"/>] que la
/// contiene (para que la <see cref="CamaraOrbital"/> pueda encuadrarla).
/// </summary>
public sealed record Escena3D(IReadOnlyList<Segmento3D> Segmentos, Vector3 Min, Vector3 Max)
{
    /// <summary>Centro geométrico de la caja envolvente.</summary>
    public Vector3 Centro => (Min + Max) * 0.5f;

    /// <summary>Escena vacía (sin segmentos, AABB en el origen).</summary>
    public static Escena3D Vacia { get; } = new(new List<Segmento3D>(), Vector3.Zero, Vector3.Zero);
}

/// <summary>
/// Construye una escena 3D alámbrica <b>esquemática</b> de un
/// <see cref="Edificio"/> para el viewport de Fase I.
///
/// <para>
/// El modelo de dominio no almacena coordenadas en planta (viven en el editor
/// CAD, no en el Core), así que el massing es esquemático pero <b>derivado de
/// datos reales</b>: cada <see cref="Nivel"/> se dibuja a su <see cref="Nivel.Cota"/>
/// como un cuadrado horizontal centrado en el origen, de lado igual a la raíz
/// del área total de sus losas (√Σ Lx·Ly) — un cuadrado de área equivalente.
/// Las «columnas» unen las esquinas homólogas de niveles consecutivos (cuando
/// los pisos tienen igual tamaño quedan verticales; si difieren, forman un
/// tronco). Tipo puro de dominio — multiplataforma, sin GL ni SharpDX.
/// </para>
/// </summary>
public static class EscenaEdificio
{
    /// <summary>Lado mínimo del piso, en metros.</summary>
    public const float LadoMinimo = 1f;

    /// <summary>Lado por defecto cuando el nivel no tiene losas, en metros.</summary>
    public const float LadoPorDefecto = 5f;

    /// <summary>
    /// Construye la <see cref="Escena3D"/> del <paramref name="edificio"/>. Un
    /// edificio nulo o sin niveles devuelve <see cref="Escena3D.Vacia"/>.
    /// </summary>
    public static Escena3D Construir(Edificio? edificio)
    {
        if (edificio is null || edificio.Niveles.Count == 0)
            return Escena3D.Vacia;

        var segs = new List<Segmento3D>();
        var esquinasPorNivel = new List<Vector3[]>(edificio.Niveles.Count);

        foreach (var nivel in edificio.Niveles)
        {
            float area = 0f;
            foreach (var sistema in nivel.Sistemas)
                foreach (var losa in sistema.Losas)
                {
                    float a = (float)(losa.Lx * losa.Ly);
                    if (a > 0f) area += a;
                }

            float lado = area > 0f ? MathF.Sqrt(area) : LadoPorDefecto;
            if (lado < LadoMinimo) lado = LadoMinimo;

            float h = lado * 0.5f;
            float y = (float)nivel.Cota;
            var esquinas = new[]
            {
                new Vector3(-h, y, -h), new Vector3(h, y, -h),
                new Vector3(h, y, h),   new Vector3(-h, y, h),
            };
            esquinasPorNivel.Add(esquinas);

            // Rectángulo del piso (4 aristas).
            for (int i = 0; i < 4; i++)
                segs.Add(new Segmento3D(esquinas[i], esquinas[(i + 1) % 4]));

            // Columnas reales del modelo (Fase J): segmento vertical en su posición
            // de planta (X, Z) = (CoordenadaX, CoordenadaY), de la cota del nivel a
            // cota + altura.
            foreach (var columna in nivel.Columnas)
            {
                float cx = (float)columna.CoordenadaX;
                float cz = (float)columna.CoordenadaY;
                float yTope = y + (float)columna.Altura;
                segs.Add(new Segmento3D(new Vector3(cx, y, cz), new Vector3(cx, yTope, cz)));

                // Zapata (Fase J): recuadro horizontal de su huella en la base de
                // la columna, centrado en (cx, cz) y dimensionado por Ancho × Largo.
                if (columna.Zapata is { } zapata)
                {
                    float ha = (float)(zapata.Ancho * 0.5);
                    float hl = (float)(zapata.Largo * 0.5);
                    var e = new[]
                    {
                        new Vector3(cx - ha, y, cz - hl), new Vector3(cx + ha, y, cz - hl),
                        new Vector3(cx + ha, y, cz + hl), new Vector3(cx - ha, y, cz + hl),
                    };
                    for (int i = 0; i < 4; i++)
                        segs.Add(new Segmento3D(e[i], e[(i + 1) % 4]));
                }
            }
        }

        // Columnas: aristas entre esquinas homólogas de niveles consecutivos.
        for (int n = 0; n + 1 < esquinasPorNivel.Count; n++)
            for (int i = 0; i < 4; i++)
                segs.Add(new Segmento3D(esquinasPorNivel[n][i], esquinasPorNivel[n + 1][i]));

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var s in segs)
        {
            min = Vector3.Min(min, Vector3.Min(s.A, s.B));
            max = Vector3.Max(max, Vector3.Max(s.A, s.B));
        }
        return new Escena3D(segs, min, max);
    }
}
