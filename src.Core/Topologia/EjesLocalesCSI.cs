using System;
using System.Numerics;

namespace LosasPlus.Topologia;

/// <summary>
/// Sistema de ejes locales de un miembro estructural según la convención CSI
/// (ETABS / SAP2000), replicado verbatim para garantizar la compatibilidad
/// directa con el estándar SAF y con futuros solvers de rigidez directa
/// (Fase 3D-I2 del Plan Maestro).
///
/// <para>
/// <b>Convención CSI</b>:
/// <list type="bullet">
///   <item><b>Frame vertical</b> (cos(eje1, +Z) ≈ ±1): eje1 = +Z global;
///   eje2 = +X global; eje3 = +Y global (regla de la mano derecha).</item>
///   <item><b>Frame horizontal/inclinado</b>: eje1 = (j − i) / ‖j − i‖;
///   plano 1–2 vertical (eje2 hacia arriba); eje3 = eje1 × eje2.</item>
///   <item><b>Ángulo roll</b> rota eje2 y eje3 alrededor de eje1 (positivo
///   antihorario visto desde la cabeza del +1).</item>
/// </list>
/// </para>
///
/// <para>
/// Los tres ejes son vectores unitarios ortogonales — esto está protegido
/// por la factory <see cref="Calcular"/>. El record es inmutable; las
/// rotaciones producen una nueva instancia.
/// </para>
/// </summary>
/// <param name="Eje1">Eje longitudinal del miembro (i → j), unitario.</param>
/// <param name="Eje2">Eje transversal principal — apunta "arriba" para
/// miembros horizontales, paralelo a +X global para columnas verticales.</param>
/// <param name="Eje3">Eje transversal secundario (eje1 × eje2), unitario.</param>
public sealed record EjesLocalesCSI(Vector3 Eje1, Vector3 Eje2, Vector3 Eje3)
{
    /// <summary>Umbral en radianes para considerar un miembro "vertical".</summary>
    /// <remarks>
    /// Corresponde a sen(θ_eje1↔+Z) &lt; 1e-3, idéntico al umbral CSI
    /// documentado. Para alineaciones casi verticales se evita el caso
    /// degenerado de la proyección del eje +Z sobre el plano normal al
    /// eje longitudinal.
    /// </remarks>
    private const float ToleranciaVertical = 1e-3f;

    /// <summary>
    /// Construye el triedro local de un miembro definido por sus nodos
    /// inicial y final, aplicando opcionalmente un ángulo de roll alrededor
    /// del eje longitudinal.
    /// </summary>
    /// <param name="nodoI">Posición absoluta del nodo inicial.</param>
    /// <param name="nodoJ">Posición absoluta del nodo final.</param>
    /// <param name="anguloRollGrados">
    /// Rotación del par eje2/eje3 alrededor de eje1, en grados. Positivo
    /// antihorario visto desde la cabeza del +1 (convención CSI).
    /// </param>
    /// <exception cref="ArgumentException">
    /// Si <paramref name="nodoI"/> y <paramref name="nodoJ"/> son
    /// coincidentes (longitud nula).
    /// </exception>
    public static EjesLocalesCSI Calcular(
        Vector3 nodoI, Vector3 nodoJ, double anguloRollGrados = 0.0)
    {
        var delta = nodoJ - nodoI;
        float longitud = delta.Length();
        if (longitud <= float.Epsilon)
            throw new ArgumentException(
                "Los nodos i y j son coincidentes: longitud del miembro nula.",
                nameof(nodoJ));

        Vector3 e1 = delta / longitud;

        // ¿Miembro vertical? Test contra ambos sentidos del eje Z global.
        Vector3 e2;
        if (1f - Math.Abs(e1.Z) < ToleranciaVertical)
        {
            // Frame vertical: eje2 = +X global; eje3 = +Y global.
            e2 = new Vector3(1f, 0f, 0f);
        }
        else
        {
            // Frame inclinado u horizontal: eje2 = proyección normalizada
            // del +Z global sobre el plano normal al eje1.
            Vector3 zGlobal = new(0f, 0f, 1f);
            Vector3 e2NoNorm = zGlobal - (Vector3.Dot(zGlobal, e1) * e1);
            float e2Long = e2NoNorm.Length();
            // Por construcción e2Long > 0 (e1 no es paralelo a +Z aquí).
            e2 = e2NoNorm / e2Long;
        }

        Vector3 e3 = Vector3.Cross(e1, e2);
        // Renormalizar e3 por estabilidad numérica (eje1 y eje2 ya son
        // ortonormales por construcción; el módulo de e3 debe ser ≈ 1).
        e3 = Vector3.Normalize(e3);

        // Ángulo roll opcional: rota e2 y e3 alrededor de e1.
        if (Math.Abs(anguloRollGrados) > 1e-9)
        {
            double radianes = anguloRollGrados * Math.PI / 180.0;
            float cos = (float)Math.Cos(radianes);
            float sin = (float)Math.Sin(radianes);
            Vector3 e2Rotado = cos * e2 + sin * e3;
            Vector3 e3Rotado = -sin * e2 + cos * e3;
            e2 = e2Rotado;
            e3 = e3Rotado;
        }

        return new EjesLocalesCSI(e1, e2, e3);
    }
}
