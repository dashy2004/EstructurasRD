using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LosasPlus.Calculo;
using LosasPlus.Models;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace LosasPlus.ViewModels;

/// <summary>
/// ViewModel del editor de columnas (Fase J.10/J.12): agregar, editar y eliminar
/// las <see cref="Columna"/> del <see cref="NivelSeleccionado"/> del edificio
/// activo, que alimentan la vista 3D (I.5/I.6) y el descenso de cargas (J.7).
///
/// <para>
/// Sin dependencias de Avalonia — testeable. Recibe el edificio por un
/// <c>Func</c> y expone los <see cref="Niveles"/> para elegir cuál editar
/// (J.12); por defecto el primero.
/// </para>
/// </summary>
public sealed class ColumnasEditorViewModel : INotifyPropertyChanged
{
    private readonly Func<Edificio?> _getEdificio;

    public ColumnasEditorViewModel(Func<Edificio?> getEdificio)
    {
        _getEdificio = getEdificio ?? throw new ArgumentNullException(nameof(getEdificio));
        Recargar();
    }

    /// <summary>Niveles del edificio activo, para elegir cuál editar.</summary>
    public ObservableCollection<Nivel> Niveles { get; } = new();

    private Nivel? _nivelSeleccionado;
    /// <summary>Nivel cuyas columnas se editan.</summary>
    public Nivel? NivelSeleccionado
    {
        get => _nivelSeleccionado;
        set
        {
            if (ReferenceEquals(_nivelSeleccionado, value)) return;
            _nivelSeleccionado = value;
            Seleccionada = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Columnas));
        }
    }

    /// <summary>Columnas del nivel seleccionado (la colección real — editable).</summary>
    public ObservableCollection<Columna>? Columnas => _nivelSeleccionado?.Columnas;

    private Columna? _seleccionada;
    /// <summary>Columna seleccionada en la tabla (objetivo de «Eliminar»).</summary>
    public Columna? Seleccionada
    {
        get => _seleccionada;
        set { _seleccionada = value; OnPropertyChanged(); RecalcularDiseno(); }
    }

    // ---- Diseño a flexo-compresión de la columna seleccionada (ACI 318-19) ----

    private double _fcMPa = 28.0, _fyMPa = 420.0, _recubrimientoMm = 40.0;
    private int _numeroBarra = 8, _barrasX = 3, _barrasY = 3;
    private double _puKN, _muKNm;

    /// <summary>Resistencia del hormigón f'c (MPa) para el diseño de la columna.</summary>
    public double FcMPa { get => _fcMPa; set { _fcMPa = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Resistencia del acero fy (MPa).</summary>
    public double FyMPa { get => _fyMPa; set { _fyMPa = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Recubrimiento al centro de la barra (mm).</summary>
    public double RecubrimientoMm { get => _recubrimientoMm; set { _recubrimientoMm = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Número de barra longitudinal (#3..#11).</summary>
    public int NumeroBarra { get => _numeroBarra; set { _numeroBarra = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Barras por cara horizontal (≥2, incluye esquinas).</summary>
    public int BarrasX { get => _barrasX; set { _barrasX = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Barras por cara vertical (≥2, incluye esquinas).</summary>
    public int BarrasY { get => _barrasY; set { _barrasY = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Carga axial última de demanda Pu (kN).</summary>
    public double PuKN { get => _puKN; set { _puKN = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Momento último de demanda Mu (kN·m).</summary>
    public double MuKNm { get => _muKNm; set { _muKNm = value; OnPropertyChanged(); RecalcularDiseno(); } }

    /// <summary>
    /// Diseño de la columna seleccionada (cuantía, Po, φPn,max, estribo, diagrama,
    /// chequeo), o <c>null</c> si no hay selección o la geometría/armado es inválido.
    /// </summary>
    public ColumnaDisenador.DisenoColumna? DisenoActual { get; private set; }

    /// <summary>Modelo de OxyPlot que representa el diagrama P-M.</summary>
    public PlotModel? ModeloInteraccion { get; private set; }

    private void RecalcularDiseno()
    {
        var col = _seleccionada;
        if (col is null || col.Base <= 0 || col.Peralte <= 0
            || _barrasX < 2 || _barrasY < 2
            || ColumnaDisenador.DiametroBarraMm(_numeroBarra) <= 0)
        {
            DisenoActual = null;
        }
        else
        {
            double b = col.Base * 1000.0;       // m → mm
            double h = col.Peralte * 1000.0;
            var barras = ColumnaDisenador.LayoutPerimetral(b, h, _recubrimientoMm, _barrasX, _barrasY, _numeroBarra);
            var sec = new ColumnaSeccion(b, h, _fcMPa, _fyMPa, barras);
            DisenoActual = ColumnaDisenador.DisenarColumna(sec, _numeroBarra, _puKN * 1000.0, _muKNm * 1e6);
        }
        ConstruirPlot();   // maneja DisenoActual==null → ModeloInteraccion=null (evita dejar el plot viejo stale)
        OnPropertyChanged(nameof(DisenoActual));
        OnPropertyChanged(nameof(ModeloInteraccion));
    }

    private void ConstruirPlot()
    {
        if (DisenoActual is null)
        {
            ModeloInteraccion = null;
            return;
        }

        var d = DisenoActual;
        var plot = new PlotModel
        {
            Title = "Diagrama de Interacción P-M",
            TitleFontSize = 14,
            TitleFontWeight = OxyPlot.FontWeights.Bold,
            TextColor = OxyColor.Parse("#E2E8F0"),
            PlotAreaBorderColor = OxyColor.Parse("#334155")
        };

        plot.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "φMn (kN·m)",
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = OxyColor.Parse("#1E293B"),
            TicklineColor = OxyColor.Parse("#334155"),
            AxislineColor = OxyColor.Parse("#475569")
        });

        plot.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "φPn (kN)",
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = OxyColor.Parse("#1E293B"),
            TicklineColor = OxyColor.Parse("#334155"),
            AxislineColor = OxyColor.Parse("#475569")
        });

        // Curva del diagrama
        var serieCurva = new LineSeries
        {
            Title = "Límite de diseño (φPn, φMn)",
            Color = OxyColor.Parse("#3B82F6"),
            StrokeThickness = 2
        };
        foreach (var p in d.Diagrama)
        {
            serieCurva.Points.Add(new DataPoint(p.PhiMn / 1e6, p.PhiPn / 1000.0));
        }
        plot.Series.Add(serieCurva);

        // Tope horizontal de compresión máxima
        double phiPnMaxKN = d.PhiPnMaxN / 1000.0;
        var serieTope = new LineSeries
        {
            Title = "Compresión máx. (φPn,max)",
            Color = OxyColor.Parse("#94A3B8"),
            StrokeThickness = 1.5,
            LineStyle = LineStyle.Dash
        };
        // Para que cruce todo el gráfico, tomamos desde 0 hasta el max PhiMn (aproximado)
        double maxMn = d.Diagrama.Count > 0 ? d.Diagrama.Max(p => p.PhiMn) / 1e6 : 100;
        serieTope.Points.Add(new DataPoint(0, phiPnMaxKN));
        serieTope.Points.Add(new DataPoint(maxMn * 1.1, phiPnMaxKN));
        plot.Series.Add(serieTope);

        // Punto de demanda
        var colorDemanda = d.Chequeo.Cumple ? OxyColor.Parse("#10B981") : OxyColor.Parse("#EF4444");
        var serieDemanda = new ScatterSeries
        {
            Title = "Demanda (Mu, Pu)",
            MarkerType = MarkerType.Circle,
            MarkerSize = 5,
            MarkerFill = colorDemanda,
            MarkerStroke = OxyColors.White,
            MarkerStrokeThickness = 1
        };
        serieDemanda.Points.Add(new ScatterPoint(_muKNm, _puKN));
        plot.Series.Add(serieDemanda);

        ModeloInteraccion = plot;
    }

    /// <summary>Agrega una nueva columna al nivel seleccionado, con Id/Nombre correlativos.</summary>
    public Columna? Agregar()
    {
        var nivel = _nivelSeleccionado;
        if (nivel is null) return null;

        int id = nivel.Columnas.Count > 0 ? nivel.Columnas.Max(c => c.Id) + 1 : 1;
        var columna = new Columna { Id = id, Nombre = $"C-{id}" };
        nivel.Columnas.Add(columna);
        Seleccionada = columna;
        return columna;
    }

    /// <summary>Elimina la <see cref="Seleccionada"/> (o la indicada) del nivel seleccionado.</summary>
    public void Eliminar(Columna? columna = null)
    {
        var nivel = _nivelSeleccionado;
        var objetivo = columna ?? _seleccionada;
        if (nivel is null || objetivo is null) return;
        nivel.Columnas.Remove(objetivo);
        if (ReferenceEquals(objetivo, _seleccionada)) Seleccionada = null;
    }

    /// <summary>Repuebla la lista de niveles desde el edificio activo y selecciona el primero.</summary>
    public void Recargar()
    {
        Niveles.Clear();
        var edificio = _getEdificio();
        if (edificio is not null)
            foreach (var nivel in edificio.Niveles) Niveles.Add(nivel);
        NivelSeleccionado = Niveles.FirstOrDefault();
        OnPropertyChanged(nameof(Columnas));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
