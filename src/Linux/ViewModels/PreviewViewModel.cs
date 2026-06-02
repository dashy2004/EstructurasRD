using System.Collections.Generic;
using System.Collections.ObjectModel;
using LosasPlus.Calculo;
using LosasPlus.Models;

namespace LosasPlus.Linux.ViewModels;

/// <summary>
/// ViewModel de la cabeza de puente Avalonia. Construye el mismo proyecto de
/// ejemplo (Torre Residencial Ensanche Piantini) que usa MemoriaPlus, corre el
/// <see cref="CalculoEngine"/> real de <c>src.Core</c> sobre él, y expone las
/// losas calculadas aplanadas para mostrarlas en un DataGrid. Sirve para
/// validar que el motor de cálculo funciona idéntico en Linux y que toda la
/// pila Avalonia + tema + DataGrid renderiza correctamente.
/// </summary>
public sealed class PreviewViewModel
{
    public Proyecto Proyecto { get; }

    public ObservableCollection<LosaRow> Losas { get; } = new();

    public string Titulo { get; }
    public string Subtitulo { get; }
    public string MotorInfo { get; }

    public PreviewViewModel()
    {
        Proyecto = SeedProyectoEjemplo();

        // ---- Motor de cálculo REAL de src.Core (idéntico al de Windows) ----
        CalculoEngine.RecalcularProyecto(Proyecto);

        foreach (var sistema in Proyecto.Sistemas)
            foreach (var losa in sistema.Losas)
                Losas.Add(new LosaRow(sistema, losa));

        Titulo = $"{Proyecto.Nombre}";
        Subtitulo = $"{Proyecto.Ciudad} · {Proyecto.SistemaEstructural} · " +
                    $"f'c={Proyecto.FcKgCm2} kg/cm² · fy={Proyecto.FyKgCm2} kg/cm²";
        MotorInfo = $"Motor src.Core (net8.0) ejecutándose en Linux — " +
                    $"{Proyecto.Sistemas.Count} niveles, {Losas.Count} losas calculadas en vivo.";
    }

    /// <summary>
    /// Réplica del proyecto semilla de MemoriaPlus (MainViewModel.SeedProyectoEjemplo),
    /// para mostrar el engine rellenando HCalc/Qd/Qu/αm al arrancar.
    /// </summary>
    private static Proyecto SeedProyectoEjemplo()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Nombre = "Torre Residencial Ensanche Piantini";
        p.Ciudad = "Santo Domingo";
        p.Uso = "Residencial";
        p.SistemaEstructural = "Aporticado";
        p.FcKgCm2 = 280;
        p.FyKgCm2 = 4200;

        var e1 = new Sistema { Nombre = "E1", Uso = SistemaUso.Entrepiso, CotaMetros = 2.80 };
        e1.Losas.Add(new Losa { Id = 1, Tipo = 10, Lx = 6.45, Ly = 5.40, MampO = 1.78 });
        e1.Losas.Add(new Losa { Id = 2, Tipo = 33, Lx = 4.90, Ly = 4.45, MampO = 7.67 });
        e1.Losas.Add(new Losa { Id = 3, Tipo = 33, Lx = 4.90, Ly = 4.40 });
        e1.Losas.Add(new Losa { Id = 4, Tipo = 22, Lx = 3.80, Ly = 1.50 });
        e1.Losas.Add(new Losa { Id = 5, Tipo = 22, Lx = 1.20, Ly = 4.65 });

        var e2 = new Sistema { Nombre = "E2", Uso = SistemaUso.Entrepiso, CotaMetros = 5.60 };
        e2.Losas.Add(new Losa { Id = 1, Tipo = 10, Lx = 6.45, Ly = 5.40, MampO = 1.78 });
        e2.Losas.Add(new Losa { Id = 2, Tipo = 33, Lx = 4.90, Ly = 4.45 });
        e2.Losas.Add(new Losa { Id = 3, Tipo = 33, Lx = 4.90, Ly = 4.40 });

        var techo = new Sistema { Nombre = "Techo", Uso = SistemaUso.Techo, CotaMetros = 8.40 };
        techo.Losas.Add(new Losa { Id = 1, Tipo = 10, Lx = 6.45, Ly = 5.40 });
        techo.Losas.Add(new Losa { Id = 2, Tipo = 33, Lx = 4.90, Ly = 4.45 });

        p.Sistemas.Add(e1);
        p.Sistemas.Add(e2);
        p.Sistemas.Add(techo);

        return p;
    }
}

/// <summary>
/// Fila de presentación para el DataGrid: combina el nivel (Sistema) con la
/// losa y formatea las salidas del engine para lectura tabular.
/// </summary>
public sealed class LosaRow
{
    private readonly Losa _l;

    public LosaRow(Sistema sistema, Losa losa)
    {
        Nivel = sistema.Nombre;
        _l = losa;
    }

    public string Nivel { get; }
    public string Losa => $"L{_l.Id}";
    public int Tipo => _l.Tipo;
    public string Lx => _l.Lx.ToString("0.00");
    public string Ly => _l.Ly.ToString("0.00");
    public string HCalc => _l.HCalc?.ToString("0.00") ?? "—";
    public string HEq => _l.HEq?.ToString("0.00") ?? "—";
    public string Qd => _l.Qd?.ToString("0.000") ?? "—";
    public string Qu => _l.Qu?.ToString("0.000") ?? "—";
    public string AlphaM => _l.AlphaM?.ToString("0.00") ?? "—";
    public string Estado => _l.EstadoAlphaFm ?? "—";
}
