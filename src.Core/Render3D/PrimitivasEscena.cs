using System.Collections.Generic;
using System.Numerics;

namespace LosasPlus.Render3D;

/// <summary>
/// Un segmento de línea en el espacio 3D, de <see cref="A"/> a <see cref="B"/>
/// (coordenadas de mundo, metros). Es la primitiva de dibujo del viewport en
/// modo alámbrico (Fase I): ejes, rejilla del suelo y aristas de los elementos
/// estructurales se expresan como listas de segmentos.
/// </summary>
public readonly record struct Segmento3D(Vector3 A, Vector3 B);

/// <summary>
/// Generadores de geometría alámbrica de la escena 3D (Fase I). Métodos puros
/// que devuelven listas de <see cref="Segmento3D"/> — sin dependencias de UI ni
/// de SharpDX, multiplataforma y testeables. Convenio de ejes: Y arriba, plano
/// XZ es el suelo.
/// </summary>
public static class PrimitivasEscena
{
    /// <summary>
    /// Los tres ejes de mundo desde el origen, cada uno de
    /// <paramref name="longitud"/> metros: X, Y (vertical) y Z.
    /// </summary>
    public static IReadOnlyList<Segmento3D> Ejes(float longitud) => new[]
    {
        new Segmento3D(Vector3.Zero, new Vector3(longitud, 0f, 0f)),
        new Segmento3D(Vector3.Zero, new Vector3(0f, longitud, 0f)),
        new Segmento3D(Vector3.Zero, new Vector3(0f, 0f, longitud)),
    };

    /// <summary>
    /// Rejilla cuadrada en el plano del suelo (XZ), centrada en el origen, que
    /// se extiende ±<paramref name="extension"/> metros con líneas cada
    /// <paramref name="paso"/> metros. Devuelve vacío si el paso no es positivo.
    /// </summary>
    public static IReadOnlyList<Segmento3D> RejillaSuelo(float extension, float paso)
    {
        var segs = new List<Segmento3D>();
        if (paso <= 0f || extension <= 0f) return segs;

        // Líneas paralelas a Z (varía X) y paralelas a X (varía Z).
        for (float x = -extension; x <= extension + 1e-4f; x += paso)
            segs.Add(new Segmento3D(new Vector3(x, 0f, -extension), new Vector3(x, 0f, extension)));
        for (float z = -extension; z <= extension + 1e-4f; z += paso)
            segs.Add(new Segmento3D(new Vector3(-extension, 0f, z), new Vector3(extension, 0f, z)));
        return segs;
    }

    /// <summary>
    /// Las 12 aristas de una caja AABB definida por sus esquinas
    /// <paramref name="min"/> y <paramref name="max"/>.
    /// </summary>
    public static IReadOnlyList<Segmento3D> CajaAlambre(Vector3 min, Vector3 max)
    {
        var v = new Vector3[8]
        {
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z),
            new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z),
            new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        };
        int[,] aristas =
        {
            {0,1},{1,2},{2,3},{3,0}, // base inferior
            {4,5},{5,6},{6,7},{7,4}, // base superior
            {0,4},{1,5},{2,6},{3,7}, // verticales
        };
        var segs = new List<Segmento3D>(12);
        for (int i = 0; i < 12; i++)
            segs.Add(new Segmento3D(v[aristas[i, 0]], v[aristas[i, 1]]));
        return segs;
    }
}
