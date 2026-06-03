using System.ComponentModel;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de la elevación del sistema (WS3): cada sistema lleva su cota de
/// elevación («cada sistema es un nivel de elevación»), de forma aditiva y
/// observable, sin cambiar la jerarquía Nivel→Sistema.
/// </summary>
public class SistemaElevacionTests
{
    [Fact]
    public void Elevacion_por_defecto_es_cero_y_se_puede_asignar()
    {
        var s = new Sistema();
        Assert.Equal(0.0, s.Elevacion, 6);

        s.Elevacion = 3.5;
        Assert.Equal(3.5, s.Elevacion, 6);
    }

    [Fact]
    public void Elevacion_notifica_PropertyChanged()
    {
        var s = new Sistema();
        var cambiadas = new System.Collections.Generic.List<string?>();
        ((INotifyPropertyChanged)s).PropertyChanged += (_, e) => cambiadas.Add(e.PropertyName);

        s.Elevacion = 6.0;

        // Alias de CotaMetros: notifica ambos nombres para mantener binding consistente.
        Assert.Contains(nameof(Sistema.Elevacion), cambiadas);
        Assert.Contains(nameof(Sistema.CotaMetros), cambiadas);
    }
}
