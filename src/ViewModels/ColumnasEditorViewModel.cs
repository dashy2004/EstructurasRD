using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LosasPlus.Calculo;
using LosasPlus.Models;
using LosasPlus.Rendering;
using LosasPlus.Transmision;
using MemoriaPlus.Common;
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
    private readonly Func<Nivel?> _getNivel;

    public ColumnasEditorViewModel(Func<Edificio?> getEdificio, Func<Nivel?> getNivel)
    {
        _getEdificio = getEdificio ?? throw new ArgumentNullException(nameof(getEdificio));
        _getNivel = getNivel ?? throw new ArgumentNullException(nameof(getNivel));
        TomarPuDelDescensoCommand = new RelayCommand(_ => TomarPuDelDescenso());
        AgregarCommand = new RelayCommand(_ => Agregar(), _ => _nivelSeleccionado is not null);
        EliminarCommand = new RelayCommand(_ => Eliminar(), _ => _seleccionada is not null);
        // Sincroniza NivelSeleccionado desde NivelActivo y engancha la colección.
        _nivelSeleccionado = _getNivel();
        EngancharColumnas(_nivelSeleccionado);
    }

    /// <summary>Toma el Pu de demanda desde el descenso de cargas del edificio (botón).</summary>
    public ICommand TomarPuDelDescensoCommand { get; }

    /// <summary>
    /// Comando que agrega una columna al <see cref="NivelSeleccionado"/> (D2: reemplaza
    /// el handler de code-behind <c>OnAgregar</c>). Habilitado solo cuando hay un nivel
    /// seleccionado.
    /// </summary>
    public RelayCommand AgregarCommand { get; }

    /// <summary>
    /// Comando que elimina la <see cref="Seleccionada"/> actual.
    /// Solo ejecutable cuando hay una columna seleccionada — se habilita/deshabilita
    /// reactivamente desde el setter de <see cref="Seleccionada"/>.
    /// </summary>
    public RelayCommand EliminarCommand { get; }

    // ---- Selección de nivel (D2: editor autónomo) ----

    /// <summary>
    /// Niveles del edificio activo para el ComboBox del editor (D2). Antes este
    /// binding apuntaba a una propiedad inexistente (solo vivía en los comentarios
    /// XML) y fallaba en silencio con <c>x:CompileBindings=False</c>.
    /// </summary>
    public ObservableCollection<Nivel>? Niveles => _getEdificio()?.Niveles;

    private Nivel? _nivelSeleccionado;
    /// <summary>
    /// Nivel cuyas columnas edita el editor (D2). Es la fuente real del ComboBox y de
    /// <see cref="Columnas"/>. Al cambiarlo se re-evalúa la colección, se limpia la
    /// selección y se reengancha el <c>CollectionChanged</c> del nuevo nivel.
    /// </summary>
    public Nivel? NivelSeleccionado
    {
        get => _nivelSeleccionado;
        set
        {
            if (ReferenceEquals(_nivelSeleccionado, value)) return;
            EngancharColumnas(value);
            OnPropertyChanged();
            Seleccionada = null;                 // la selección anterior pertenece a otro nivel
            OnPropertyChanged(nameof(Columnas)); // el pass-through ahora apunta al nuevo nivel
            AgregarCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Reengancha el <c>CollectionChanged</c> de las columnas: si se agregan/eliminan
    /// columnas fuera del editor (p. ej. al sincronizar vigas→columnas) la tabla lo
    /// refleja. Desuscribe el nivel anterior.
    /// </summary>
    private void EngancharColumnas(Nivel? nivel)
    {
        if (ReferenceEquals(_nivelSeleccionado, nivel) && _columnasEnganchadas is not null) return;
        if (_columnasEnganchadas is not null)
            _columnasEnganchadas.CollectionChanged -= OnColumnasCambiaron;
        _nivelSeleccionado = nivel;
        _columnasEnganchadas = nivel?.Columnas;
        if (_columnasEnganchadas is not null)
            _columnasEnganchadas.CollectionChanged += OnColumnasCambiaron;
    }

    private ObservableCollection<Columna>? _columnasEnganchadas;

    private void OnColumnasCambiaron(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Columnas));
        // Si se quitó la columna seleccionada desde fuera, limpiar la selección.
        if (_seleccionada is not null && _columnasEnganchadas is not null
            && !_columnasEnganchadas.Contains(_seleccionada))
            Seleccionada = null;
    }

    /// <summary>
    /// Cierra el lazo losa → bajada → columna: calcula la carga última que llega a
    /// la base del edificio (<see cref="BajadaCargas.Acumular"/>), la reparte
    /// equitativamente entre <b>todas</b> las columnas del edificio y fija el
    /// resultado (kN) como <see cref="PuKN"/>, evitando teclear la carga a mano.
    /// Sin edificio o sin columnas no hace nada.
    /// </summary>
    public void TomarPuDelDescenso()
    {
        var edificio = _getEdificio();
        var nivel = _nivelSeleccionado;
        if (edificio is null || nivel is null) return;

        // F3: geométrico por área tributaria para la columna seleccionada; si no
        // recibe carga de vigas (o no hay selección), cae al equitativo histórico.
        // Misma aproximación que Planta2DEditorView; reacciones reales → F4.
        if (_seleccionada is not null)
        {
            double puGeo = DescensoColumnas.PuDemandaGeometricoKN(nivel, _seleccionada);
            if (puGeo > 0) { PuKN = puGeo; return; }
        }

        int numColumnas = nivel.Columnas.Count;
        if (numColumnas <= 0) return;

        double cargaEnBase = BajadaCargas.Acumular(edificio).CargaEnBase;
        PuKN = DescensoColumnas.PuDemandaKN(cargaEnBase, numColumnas);
    }

    /// <summary>Columnas del <see cref="NivelSeleccionado"/> (la colección real — editable).</summary>
    public ObservableCollection<Columna>? Columnas => _nivelSeleccionado?.Columnas;

    private Columna? _seleccionada;
    /// <summary>Columna seleccionada en la tabla (objetivo de «Eliminar»).</summary>
    public Columna? Seleccionada
    {
        get => _seleccionada;
        set
        {
            if (ReferenceEquals(_seleccionada, value)) return;
            // Editar Base/Peralte/coordenada de la columna seleccionada debe recalcular
            // el diseño + el diagrama P-M (D2). Reenganchar la suscripción a la nueva.
            if (_seleccionada is not null)
                _seleccionada.PropertyChanged -= OnColumnaSeleccionadaCambiada;
            _seleccionada = value;
            if (_seleccionada is not null)
                _seleccionada.PropertyChanged += OnColumnaSeleccionadaCambiada;
            OnPropertyChanged();
            // Revaluar predicado que lee _seleccionada.
            EliminarCommand.RaiseCanExecuteChanged();
            RecalcularDiseno();
        }
    }

    /// <summary>
    /// Recalcula el diseño cuando cambia una propiedad de la columna seleccionada
    /// (Base/Peralte/coordenada) — la sección y el diagrama P-M siguen a la geometría (D2).
    /// </summary>
    private void OnColumnaSeleccionadaCambiada(object? sender, PropertyChangedEventArgs e)
        => RecalcularDiseno();

    // ---- Diseño a flexo-compresión de la columna seleccionada (ACI 318-19) ----

    private double _fcMPa = 28.0, _fyMPa = 420.0, _recubrimientoMm = 40.0;
    private int _numeroBarra = 8, _barrasX = 3, _barrasY = 3;
    private double _puKN, _muKNm;
    private double _luMm = 3000.0, _factorK = 1.0;

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

    /// <summary>Longitud no arriostrada Lu (mm) para el chequeo de esbeltez.</summary>
    public double LuMm { get => _luMm; set { _luMm = value; OnPropertyChanged(); RecalcularDiseno(); } }
    /// <summary>Factor de longitud efectiva k (ACI 318-19 §6.6.4) para la esbeltez.</summary>
    public double FactorK { get => _factorK; set { _factorK = value; OnPropertyChanged(); RecalcularDiseno(); } }

    /// <summary>
    /// Diseño de la columna seleccionada (cuantía, Po, φPn,max, estribo, diagrama,
    /// chequeo), o <c>null</c> si no hay selección o la geometría/armado es inválido.
    /// </summary>
    public ColumnaDisenador.DisenoColumna? DisenoActual { get; private set; }

    /// <summary>
    /// Resumen de esbeltez de la columna seleccionada (r, k·Lu/r, límite, δ), o
    /// <c>null</c> si no hay diseño válido. M1=0 / M2=Mu (curvatura simple, conservador).
    /// </summary>
    public ColumnaDisenador.ResumenEsbeltezColumna? EsbeltezActual { get; private set; }

    /// <summary>Modelo de OxyPlot que representa el diagrama P-M.</summary>
    public PlotModel? ModeloInteraccion { get; private set; }

    /// <summary>PNG del diagrama P-M para mostrar como imagen (patrón DiagramaPng, F1).</summary>
    public byte[]? InteraccionPng { get; private set; }

    private const int PngAnchoInteraccion = 900;
    private const int PngAltoInteraccion = 600;
    private const int PngAnchoSeccionCol = 600;
    private const int PngAltoSeccionCol = 440;

    /// <summary>Resumen del armado longitudinal: «N #b · As = … cm²», o null si no hay diseño válido.</summary>
    public string? ArmadoLongitudinal { get; private set; }

    /// <summary>Sección transversal de la columna (concreto + estribo + barras reales), o null.</summary>
    public PlotModel? ModeloSeccionColumna { get; private set; }

    /// <summary>PNG de la sección transversal para mostrar como imagen (patrón DiagramaPng, F1).</summary>
    public byte[]? SeccionColumnaPng { get; private set; }

    private void RecalcularDiseno()
    {
        var col = _seleccionada;
        if (col is null || col.Base <= 0 || col.Peralte <= 0
            || _barrasX < 2 || _barrasY < 2
            || ColumnaDisenador.DiametroBarraMm(_numeroBarra) <= 0)
        {
            DisenoActual = null;
            EsbeltezActual = null;
            ArmadoLongitudinal = null;
            ModeloSeccionColumna = null;
        }
        else
        {
            double b = col.Base * 1000.0;       // m → mm
            double h = col.Peralte * 1000.0;
            var barras = ColumnaDisenador.LayoutPerimetral(b, h, _recubrimientoMm, _barrasX, _barrasY, _numeroBarra);
            var sec = new ColumnaSeccion(b, h, _fcMPa, _fyMPa, barras);
            DisenoActual = ColumnaDisenador.DisenarColumna(sec, _numeroBarra, _puKN * 1000.0, _muKNm * 1e6);
            EsbeltezActual = ColumnaDisenador.ResumenEsbeltez(
                sec, _factorK, _luMm, _puKN * 1000.0, m1: 0.0, m2: _muKNm * 1e6, cm: 1.0);
            // Armado longitudinal real (nº de barras y As total, mm² → cm²) y su dibujo de sección.
            ArmadoLongitudinal = $"{barras.Count} #{_numeroBarra}  ·  As = {sec.Ast / 100.0:0.0} cm²";
            ModeloSeccionColumna = ConstruirSeccionColumna(col, b, h, _recubrimientoMm, barras);
        }
        ConstruirPlot();   // maneja DisenoActual==null → ModeloInteraccion=null (evita dejar el plot viejo stale)
        // Render a imagen (Render(null,…) devuelve null: cubre la rama sin selección).
        InteraccionPng = DiagramaPng.Render(ModeloInteraccion, PngAnchoInteraccion, PngAltoInteraccion);
        SeccionColumnaPng = DiagramaPng.Render(ModeloSeccionColumna, PngAnchoSeccionCol, PngAltoSeccionCol);
        OnPropertyChanged(nameof(DisenoActual));
        OnPropertyChanged(nameof(EsbeltezActual));
        OnPropertyChanged(nameof(ModeloInteraccion));
        OnPropertyChanged(nameof(InteraccionPng));
        OnPropertyChanged(nameof(ArmadoLongitudinal));
        OnPropertyChanged(nameof(ModeloSeccionColumna));
        OnPropertyChanged(nameof(SeccionColumnaPng));
    }

    /// <summary>
    /// Dibuja la sección transversal de la columna: concreto B×H, estribo y las
    /// barras longitudinales REALES en sus posiciones (coords respecto al centro,
    /// mm) más las cotas B/H. Solo presentación — la geometría/armado salen del
    /// diseño (<see cref="ColumnaDisenador.LayoutPerimetral"/>).
    /// </summary>
    private static PlotModel ConstruirSeccionColumna(
        Columna col, double bMm, double hMm, double recMm, System.Collections.Generic.IReadOnlyList<BarraLong> barras)
    {
        var m = new PlotModel { Background = OxyColors.Transparent, PlotAreaBorderColor = OxyColors.Transparent };
        m.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Minimum = -bMm * 0.80, Maximum = bMm * 0.80, IsAxisVisible = false });
        m.Axes.Add(new LinearAxis { Position = AxisPosition.Left,   Minimum = -hMm * 0.85, Maximum = hMm * 0.85, IsAxisVisible = false });

        // Concreto B×H (centrado en el origen). BelowSeries: si quedara encima
        // (default AboveSeries), su gris semitransparente lavaría las barras.
        m.Annotations.Add(new RectangleAnnotation
        {
            MinimumX = -bMm / 2, MaximumX = bMm / 2, MinimumY = -hMm / 2, MaximumY = hMm / 2,
            Fill = OxyColor.FromAColor(80, OxyColors.Gray), Stroke = OxyColors.Black, StrokeThickness = 2,
            Layer = AnnotationLayer.BelowSeries,
        });

        // Estribo (recubrimiento al centro de la barra).
        double xi = bMm / 2 - recMm, yi = hMm / 2 - recMm;
        if (xi > 0 && yi > 0)
            m.Annotations.Add(new RectangleAnnotation
            {
                MinimumX = -xi, MaximumX = xi, MinimumY = -yi, MaximumY = yi,
                Fill = OxyColors.Transparent, Stroke = OxyColors.DarkRed, StrokeThickness = 1.5,
                Layer = AnnotationLayer.BelowSeries,
            });

        // Barras longitudinales reales.
        var scatter = new ScatterSeries
        {
            MarkerType = MarkerType.Circle, MarkerSize = 5,
            MarkerFill = OxyColors.DarkBlue, MarkerStroke = OxyColors.White, MarkerStrokeThickness = 1,
        };
        foreach (var br in barras) scatter.Points.Add(new ScatterPoint(br.X, br.Y));
        m.Series.Add(scatter);

        // Cotas B (abajo) y H (al costado).
        var cota = OxyColor.Parse("#94A3B8");
        m.Annotations.Add(TextoCota($"B = {col.Base:0.##} m", 0, -hMm * 0.64, cota, 0));
        m.Annotations.Add(TextoCota($"H = {col.Peralte:0.##} m", -bMm * 0.64, 0, cota, -90));

        m.InvalidatePlot(true);
        return m;
    }

    /// <summary>Anotación de texto para las cotas de la sección.</summary>
    private static TextAnnotation TextoCota(string texto, double x, double y, OxyColor color, double rotation)
        => new()
        {
            Text = texto,
            TextPosition = new DataPoint(x, y),
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
            TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle,
            TextColor = color,
            FontSize = 11,
            TextRotation = rotation,
            StrokeThickness = 0,
        };

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

    /// <summary>Agrega una nueva columna al <see cref="NivelSeleccionado"/>, con Id/Nombre correlativos.</summary>
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

    /// <summary>Elimina la <see cref="Seleccionada"/> (o la indicada) del <see cref="NivelSeleccionado"/>.</summary>
    public void Eliminar(Columna? columna = null)
    {
        var nivel = _nivelSeleccionado;
        var objetivo = columna ?? _seleccionada;
        if (nivel is null || objetivo is null) return;
        nivel.Columnas.Remove(objetivo);
        if (ReferenceEquals(objetivo, _seleccionada)) Seleccionada = null;
    }

    /// <summary>
    /// Lo invoca <c>MainViewModel</c> cuando cambia el <c>NivelActivo</c>: sincroniza
    /// el <see cref="NivelSeleccionado"/> del editor con el nivel activo de la app
    /// (D2: el editor sigue al nivel global) y refresca la colección/selección.
    /// </summary>
    public void Recargar()
    {
        var activo = _getNivel();
        if (ReferenceEquals(_nivelSeleccionado, activo))
        {
            // Mismo nivel (o ambos null): igual revalidar la colección y limpiar selección.
            Seleccionada = null;
            OnPropertyChanged(nameof(Columnas));
            OnPropertyChanged(nameof(Niveles));
            return;
        }
        NivelSeleccionado = activo;          // recorre el setter completo (rehook + limpiar)
        OnPropertyChanged(nameof(Niveles));  // el edificio/niveles pudieron cambiar también
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
