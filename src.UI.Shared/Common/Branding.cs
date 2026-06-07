namespace MemoriaPlus.Common;

/// <summary>
/// Constantes de marca centralizadas (Fase C — branding).
/// Todo texto de <em>display</em> (títulos de ventana, About, status, versión)
/// debe usar <see cref="Producto"/> en vez de literales sueltos para que un
/// futuro rebranding sea un solo cambio.
///
/// <para>
/// IMPORTANTE: esto es SOLO para texto visible al usuario. Identificadores,
/// namespaces, nombres de ensamblado, rutas <c>%AppData%</c>, claves de
/// registro, URIs <c>avares://</c>, extensiones <c>.lpx.json</c> y nombres de
/// <c>.exe</c> NO deben usar esta constante: siguen siendo identificadores
/// técnicos estables.
/// </para>
/// </summary>
public static class Branding
{
    /// <summary>Nombre comercial del producto mostrado al usuario.</summary>
    public const string Producto = "EstructurasRD";
}
