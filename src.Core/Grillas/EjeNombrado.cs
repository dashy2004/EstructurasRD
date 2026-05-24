namespace LosasPlus.Grillas;

/// <summary>
/// Un eje estructural nombrado del plano horizontal de un edificio
/// (ej. <c>A</c>, <c>B</c>, <c>C</c> para ejes X, o <c>1</c>, <c>2</c>,
/// <c>3</c> para ejes Y). Es un punto a lo largo del eje perpendicular
/// del lienzo, con su etiqueta visible en planta.
///
/// <para>
/// Tipo <b>puro de dominio</b> — sin dependencias de WPF/Helix. Vive en
/// <c>src.Core/Grillas/</c> y se serializa/deserializa por nombre +
/// posición vía System.Text.Json.
/// </para>
///
/// <para>
/// <c>readonly record struct</c>: igualdad por valor (<c>Nombre</c> +
/// <c>PosicionMetros</c>), cero allocations, inmutabilidad — idónea
/// como item de listas serializables del Edificio.
/// </para>
/// </summary>
/// <param name="Nombre">Etiqueta visible del eje (típicamente 1-2 caracteres).</param>
/// <param name="PosicionMetros">Coordenada del eje en su dirección, en metros desde el origen del lienzo.</param>
public readonly record struct EjeNombrado(string Nombre, double PosicionMetros);
