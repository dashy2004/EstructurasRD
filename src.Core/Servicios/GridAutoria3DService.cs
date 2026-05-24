using System.Linq;
using LosasPlus.Grillas;
using LosasPlus.Models;
using LosasPlus.Topologia;
using LosasPlus.Vigas;

namespace LosasPlus.Servicios;

/// <summary>
/// Implementación default de <see cref="IAutoria3DService"/> (Liga B
/// paso B3). Sin estado — segura para uso concurrente como singleton
/// del shell o instancia transitoria en tests.
///
/// <para>
/// Delega la creación de vigas a
/// <see cref="GridCreationEngine.CrearVigaEntreSnaps"/>. El borrado
/// recorre <c>Proyecto → Edificios → Niveles</c> buscando el elemento
/// por Id+Tipo y lo remueve de la colección correspondiente. Inmutabilidad
/// de muros respetada — <c>TipoElemento.Muro</c> es no-op declarado.
/// </para>
/// </summary>
public sealed class GridAutoria3DService : IAutoria3DService
{
    /// <inheritdoc />
    public Viga? CrearVigaConSnap(
        Nivel nivel, GrillaEstructural grilla,
        double x1, double y1, double x2, double y2)
        => GridCreationEngine.CrearVigaEntreSnaps(nivel, grilla, x1, y1, x2, y2);

    /// <inheritdoc />
    public bool BorrarElementoPorKey(Proyecto proyecto, DomainKey key)
    {
        // Wrapping global try/catch para garantizar Ghost Deletion —
        // ante cualquier excepción inesperada (race, corrupción de
        // colección durante iteración), retornar false como si la
        // entidad no existiera. El consumidor trata false como éxito
        // silencioso.
        try
        {
            foreach (var edif in proyecto.Edificios)
            foreach (var niv  in edif.Niveles)
            {
                switch (key.Tipo)
                {
                    case TipoElemento.Columna:
                    {
                        var col = niv.Columnas.FirstOrDefault(c => c.Id == key.Id);
                        if (col is not null)
                        {
                            niv.Columnas.Remove(col);
                            // Consistencia M2C: si nivel base tiene zapata
                            // pareada por mismo Id, removerla también.
                            var zap = niv.Zapatas.FirstOrDefault(z => z.Id == key.Id);
                            if (zap is not null) niv.Zapatas.Remove(zap);
                            return true;
                        }
                        break;
                    }
                    case TipoElemento.Viga:
                    {
                        var v = niv.Vigas.FirstOrDefault(x => x.Id == key.Id);
                        if (v is not null)
                        {
                            niv.Vigas.Remove(v);
                            return true;
                        }
                        break;
                    }
                    case TipoElemento.Zapata:
                    {
                        var z = niv.Zapatas.FirstOrDefault(x => x.Id == key.Id);
                        if (z is not null)
                        {
                            niv.Zapatas.Remove(z);
                            return true;
                        }
                        break;
                    }
                    case TipoElemento.Muro:
                        // Inmutabilidad de muros — directiva histórica del
                        // usuario. No-op declarado.
                        return false;
                    case TipoElemento.Losa:
                        // Losas no se borran desde el visor 3D en B3 (alcance
                        // futuro). No-op declarado por ahora.
                        return false;
                }
            }
        }
        catch
        {
            // Ghost Deletion: degradación silenciosa.
        }
        return false;
    }
}
