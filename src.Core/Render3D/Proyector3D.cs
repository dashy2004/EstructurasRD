using System.Numerics;

namespace LosasPlus.Render3D;

/// <summary>
/// Proyección 3D→2D por software para el viewport alámbrico de Fase I.
/// Transforma puntos y segmentos del mundo a píxeles de pantalla usando la
/// matriz combinada vista·proyección de una <see cref="CamaraOrbital"/>, con
/// recorte contra el plano de cámara para evitar artefactos de los puntos
/// detrás del ojo.
///
/// <para>
/// El control de viewport (Fase I.4) dibuja los píxeles resultantes con el
/// <c>DrawingContext</c> de Avalonia (render inmediato, como el lienzo CAD) —
/// sin OpenGL ni SharpDX, multiplataforma y testeable. Tipo puro de dominio
/// (<see cref="System.Numerics"/>, convención de vector-fila: mundo·vista·proy).
/// </para>
/// </summary>
public static class Proyector3D
{
    /// <summary>Componente W mínima en espacio de recorte para considerar un punto delante del ojo.</summary>
    public const float WMinimo = 1e-4f;

    /// <summary>
    /// Proyecta un punto del mundo a píxeles. Devuelve <c>false</c> (y
    /// <paramref name="pixel"/> indefinido) si el punto está detrás del plano
    /// de cámara.
    /// </summary>
    public static bool ProyectarPunto(
        Vector3 mundo, Matrix4x4 vistaProyeccion, float anchoPx, float altoPx, out Vector2 pixel)
    {
        var clip = Vector4.Transform(new Vector4(mundo, 1f), vistaProyeccion);
        if (clip.W <= WMinimo) { pixel = default; return false; }
        pixel = ClipAPixel(clip, anchoPx, altoPx);
        return true;
    }

    /// <summary>
    /// Proyecta un segmento [<paramref name="a"/>, <paramref name="b"/>] a
    /// píxeles, recortándolo contra el plano de cámara (W = <see cref="WMinimo"/>)
    /// cuando un extremo queda detrás del ojo. Devuelve <c>false</c> si el
    /// segmento entero está detrás.
    /// </summary>
    public static bool ProyectarSegmento(
        Vector3 a, Vector3 b, Matrix4x4 vistaProyeccion, float anchoPx, float altoPx,
        out Vector2 pa, out Vector2 pb)
    {
        var c0 = Vector4.Transform(new Vector4(a, 1f), vistaProyeccion);
        var c1 = Vector4.Transform(new Vector4(b, 1f), vistaProyeccion);
        pa = default; pb = default;

        bool dentro0 = c0.W > WMinimo, dentro1 = c1.W > WMinimo;
        if (!dentro0 && !dentro1) return false;

        // Un extremo detrás → recortar interpolando en espacio de recorte donde W cruza WMinimo.
        if (dentro0 != dentro1)
        {
            float t = (WMinimo - c0.W) / (c1.W - c0.W);
            var corte = Vector4.Lerp(c0, c1, t);
            if (dentro0) c1 = corte; else c0 = corte;
        }

        pa = ClipAPixel(c0, anchoPx, altoPx);
        pb = ClipAPixel(c1, anchoPx, altoPx);
        return true;
    }

    /// <summary>Divide en perspectiva (clip → NDC) y mapea a píxeles (Y invertida para pantalla).</summary>
    private static Vector2 ClipAPixel(Vector4 clip, float anchoPx, float altoPx)
    {
        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        return new Vector2(
            (ndcX * 0.5f + 0.5f) * anchoPx,
            (1f - (ndcY * 0.5f + 0.5f)) * altoPx);
    }
}
