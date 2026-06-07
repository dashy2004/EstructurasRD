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

        foreach (var nivel in edificio.Niveles)
        {
            // Cota del nivel (referencia vertical para sus elementos reales). Ya no se
            // dibuja el rectángulo esquemático de piso ni el tronco entre niveles: sólo
            // se dibujan los elementos reales (losas/vigas/columnas), para no confundir
            // con las losas.
            float y = (float)nivel.Cota;

            // Paños reales de losas (Fase J / Planta 2D)
            foreach (var sistema in nivel.Sistemas)
            {
                // Cada sistema se dibuja a su elevación = cota del nivel + Sistema.Elevacion,
                // para que sistemas a distinta cota no se solapen en el 3D.
                float ySis = y + (float)sistema.Elevacion;
                foreach (var losa in sistema.Losas)
                {
                    float lx = (float)losa.Lx;
                    float ly = (float)losa.Ly;
                    float x0 = (float)losa.CoordenadaX;
                    float z0 = (float)losa.CoordenadaY;
                    float x1 = x0 + lx;
                    float z1 = z0 + ly;
                    float esp = (float)losa.Espesor;

                    // Vértices del paño en el tope (cota del nivel + elevación del sistema).
                    var t00 = new Vector3(x0, ySis, z0);
                    var t10 = new Vector3(x1, ySis, z0);
                    var t11 = new Vector3(x1, ySis, z1);
                    var t01 = new Vector3(x0, ySis, z1);

                    // Tope (rectángulo del paño).
                    segs.Add(new Segmento3D(t00, t10));
                    segs.Add(new Segmento3D(t10, t11));
                    segs.Add(new Segmento3D(t11, t01));
                    segs.Add(new Segmento3D(t01, t00));

                    if (esp > 0f)
                    {
                        // K.6: losa como caja delgada extruida por su espesor (fondo a
                        // cota - espesor) → +4 aristas de fondo y +4 verticales = 12.
                        float yb = ySis - esp;
                        var b00 = new Vector3(x0, yb, z0);
                        var b10 = new Vector3(x1, yb, z0);
                        var b11 = new Vector3(x1, yb, z1);
                        var b01 = new Vector3(x0, yb, z1);

                        segs.Add(new Segmento3D(b00, b10));
                        segs.Add(new Segmento3D(b10, b11));
                        segs.Add(new Segmento3D(b11, b01));
                        segs.Add(new Segmento3D(b01, b00));

                        segs.Add(new Segmento3D(t00, b00));
                        segs.Add(new Segmento3D(t10, b10));
                        segs.Add(new Segmento3D(t11, b11));
                        segs.Add(new Segmento3D(t01, b01));
                    }
                }
            }

            // Vigas reales de la planta (Fase J / Planta 2D)
            foreach (var viga in nivel.Vigas)
            {
                var a = new Vector3((float)viga.OrigenX, y, (float)viga.OrigenY);
                var b = new Vector3((float)viga.ExtremoX, y, (float)viga.ExtremoY);

                var tramo = viga.Tramos.Count > 0 ? viga.Tramos[0] : null;
                float bw = tramo is not null ? (float)tramo.Base : 0f;     // ancho (perpendicular al eje)
                float pe = tramo is not null ? (float)tramo.Peralte : 0f;  // peralte (vertical)
                float lon = Vector3.Distance(a, b);

                if (bw > 0f && pe > 0f && lon > 1e-6f)
                {
                    // K.6: prisma recto extruido a lo largo del eje de la viga = 12 aristas.
                    var dir = (b - a) / lon;                                // dirección en XZ (y=0)
                    var perp = new Vector3(-dir.Z, 0f, dir.X) * (bw * 0.5f); // ancho horizontal
                    var vert = new Vector3(0f, pe * 0.5f, 0f);              // peralte vertical
                    var pa = new[]
                    {
                        a - perp - vert, a + perp - vert, a + perp + vert, a - perp + vert,
                    };
                    var pb = new[]
                    {
                        b - perp - vert, b + perp - vert, b + perp + vert, b - perp + vert,
                    };
                    for (int i = 0; i < 4; i++)
                    {
                        segs.Add(new Segmento3D(pa[i], pa[(i + 1) % 4])); // sección en origen
                        segs.Add(new Segmento3D(pb[i], pb[(i + 1) % 4])); // sección en extremo
                        segs.Add(new Segmento3D(pa[i], pb[i]));           // arista longitudinal
                    }
                }
                else
                {
                    // Viga sin sección/longitud → solo el eje (comportamiento previo).
                    segs.Add(new Segmento3D(a, b));
                }
            }

            // Columnas reales del modelo (Fase J): segmento vertical en su posición
            // de planta (X, Z) = (CoordenadaX, CoordenadaY), de la cota del nivel a
            // cota + altura.
            foreach (var columna in nivel.Columnas)
            {
                float cx = (float)columna.CoordenadaX;
                float cz = (float)columna.CoordenadaY;
                float yTope = y + (float)columna.Altura;
                float hb = (float)(columna.Base * 0.5);     // media base (eje X)
                float hp = (float)(columna.Peralte * 0.5);  // medio peralte (eje Z)

                if (hb > 0f && hp > 0f)
                {
                    // K.6: caja extruida (prisma rectangular Base×Peralte×Altura) =
                    // 12 aristas — sección real en vez de un hilo vertical.
                    var cb = new[]
                    {
                        new Vector3(cx - hb, y, cz - hp), new Vector3(cx + hb, y, cz - hp),
                        new Vector3(cx + hb, y, cz + hp), new Vector3(cx - hb, y, cz + hp),
                    };
                    var ct = new[]
                    {
                        new Vector3(cx - hb, yTope, cz - hp), new Vector3(cx + hb, yTope, cz - hp),
                        new Vector3(cx + hb, yTope, cz + hp), new Vector3(cx - hb, yTope, cz + hp),
                    };
                    for (int i = 0; i < 4; i++)
                    {
                        segs.Add(new Segmento3D(cb[i], cb[(i + 1) % 4])); // arista base
                        segs.Add(new Segmento3D(ct[i], ct[(i + 1) % 4])); // arista tope
                        segs.Add(new Segmento3D(cb[i], ct[i]));           // arista vertical
                    }
                }
                else
                {
                    // Columna sin sección definida → segmento vertical (previo).
                    segs.Add(new Segmento3D(new Vector3(cx, y, cz), new Vector3(cx, yTope, cz)));
                }

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
