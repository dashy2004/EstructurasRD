using System;
using System.Numerics;

namespace LosasPlus.Render3D;

/// <summary>
/// Cámara orbital para el viewport 3D (Fase I del port — reemplazo
/// multiplataforma del visor SharpDX/Direct3D, que era solo-Windows). Orbita
/// alrededor de un <see cref="Objetivo"/> en coordenadas esféricas
/// (<see cref="AzimutGrados"/>, <see cref="ElevacionGrados"/>,
/// <see cref="Distancia"/>) y produce las matrices de vista y proyección que
/// consumirá el control de render GL (Fase I.2).
///
/// <para>
/// <b>Convenio de ejes:</b> Y es el eje vertical (arriba), el plano XZ es el
/// suelo. Mano derecha, mirando hacia −Z en espacio de cámara
/// (<see cref="Matrix4x4.CreateLookAt"/>). Tipo <b>puro de dominio</b> con
/// <see cref="System.Numerics"/> — sin dependencias de UI ni de SharpDX, así
/// que es multiplataforma y testeable en cualquier .NET.
/// </para>
/// </summary>
public sealed class CamaraOrbital
{
    /// <summary>Distancia mínima del ojo al objetivo, en metros (evita cruzar el objetivo).</summary>
    public const float DistanciaMin = 0.5f;

    /// <summary>Distancia máxima del ojo al objetivo, en metros.</summary>
    public const float DistanciaMax = 5000f;

    /// <summary>Elevación máxima en valor absoluto, en grados (evita degenerar el vector «arriba»).</summary>
    public const float ElevacionMax = 89f;

    /// <summary>Punto al que mira la cámara (centro de órbita), en coordenadas de mundo.</summary>
    public Vector3 Objetivo { get; set; } = Vector3.Zero;

    /// <summary>Distancia del ojo al <see cref="Objetivo"/>, en metros.</summary>
    public float Distancia { get; private set; } = 10f;

    /// <summary>Ángulo de azimut alrededor del eje vertical Y, en grados.</summary>
    public float AzimutGrados { get; private set; } = 45f;

    /// <summary>Ángulo de elevación sobre el plano del suelo XZ, en grados (acotado a ±<see cref="ElevacionMax"/>).</summary>
    public float ElevacionGrados { get; private set; } = 25f;

    /// <summary>Campo de visión vertical, en grados.</summary>
    public float CampoVisionGrados { get; set; } = 45f;

    /// <summary>Plano de recorte cercano, en metros.</summary>
    public float PlanoCercano { get; set; } = 0.1f;

    /// <summary>Plano de recorte lejano, en metros.</summary>
    public float PlanoLejano { get; set; } = 5000f;

    /// <summary>
    /// Posición del ojo en coordenadas de mundo, derivada de las coordenadas
    /// esféricas (azimut/elevación/distancia) alrededor del <see cref="Objetivo"/>.
    /// </summary>
    public Vector3 PosicionOjo
    {
        get
        {
            float az = AzimutGrados * MathF.PI / 180f;
            float el = ElevacionGrados * MathF.PI / 180f;
            float cosEl = MathF.Cos(el);
            var dir = new Vector3(cosEl * MathF.Cos(az), MathF.Sin(el), cosEl * MathF.Sin(az));
            return Objetivo + dir * Distancia;
        }
    }

    /// <summary>Matriz de vista (mundo → cámara), mano derecha mirando a −Z.</summary>
    public Matrix4x4 MatrizVista => Matrix4x4.CreateLookAt(PosicionOjo, Objetivo, Vector3.UnitY);

    /// <summary>
    /// Matriz de proyección en perspectiva para una <paramref name="aspecto"/>
    /// (ancho/alto) del viewport. Un aspecto ≤ 0 se trata como 1 (cuadrado).
    /// </summary>
    public Matrix4x4 MatrizProyeccion(float aspecto)
    {
        if (aspecto <= 0f) aspecto = 1f;
        return Matrix4x4.CreatePerspectiveFieldOfView(
            CampoVisionGrados * MathF.PI / 180f, aspecto, PlanoCercano, PlanoLejano);
    }

    /// <summary>
    /// Orbita la cámara: suma <paramref name="dAzimutGrados"/> al azimut
    /// (envuelto a [0,360)) y <paramref name="dElevacionGrados"/> a la
    /// elevación (acotada a ±<see cref="ElevacionMax"/>).
    /// </summary>
    public void Orbitar(float dAzimutGrados, float dElevacionGrados)
    {
        AzimutGrados = Normalizar360(AzimutGrados + dAzimutGrados);
        ElevacionGrados = Math.Clamp(ElevacionGrados + dElevacionGrados, -ElevacionMax, ElevacionMax);
    }

    /// <summary>
    /// Acerca o aleja la cámara multiplicando la distancia por
    /// <paramref name="factor"/> (&gt;1 aleja, &lt;1 acerca), acotada a
    /// [<see cref="DistanciaMin"/>, <see cref="DistanciaMax"/>].
    /// </summary>
    public void AcercarAlejar(float factor)
    {
        if (factor <= 0f) return;
        Distancia = Math.Clamp(Distancia * factor, DistanciaMin, DistanciaMax);
    }

    /// <summary>
    /// Encuadra una caja AABB [<paramref name="min"/>, <paramref name="max"/>]:
    /// centra el objetivo en ella y fija la distancia para que la esfera que la
    /// contiene quepa en el campo de visión vertical.
    /// </summary>
    public void Encuadrar(Vector3 min, Vector3 max)
    {
        Objetivo = (min + max) * 0.5f;
        float radio = Vector3.Distance(min, max) * 0.5f;
        if (radio < 1e-4f) radio = 1f;
        float semiFov = CampoVisionGrados * MathF.PI / 180f * 0.5f;
        Distancia = Math.Clamp(radio / MathF.Sin(semiFov), DistanciaMin, DistanciaMax);
    }

    private static float Normalizar360(float grados)
    {
        grados %= 360f;
        if (grados < 0f) grados += 360f;
        return grados;
    }
}
