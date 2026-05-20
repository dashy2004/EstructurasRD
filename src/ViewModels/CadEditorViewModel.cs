using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Services;

namespace LosasPlus.ViewModels;

/// <summary>
/// ViewModel del modo <b>Plano CAD</b> (Fase 1.B del <c>PLAN_CAD_V1.md</c>).
///
/// <para>
/// Coordina la importación de planos <c>.DXF</c> y expone el
/// <see cref="PlanoReferencia"/> resultante para que el <c>CadCanvasHost</c>
/// lo dibuje. Las losas siguen viniendo del <b>SSOT</b> (<see cref="Sistema.Losas"/>
/// del sistema activo) — el CAD es una vista más sobre la misma colección, no
/// un estado paralelo.
/// </para>
/// </summary>
public sealed class CadEditorViewModel : INotifyPropertyChanged
{
    private readonly IPlanoImporter _importer;
    private readonly Func<Sistema> _getSistemaActivo;
    private readonly Action _pushUndoSnapshot;

    public CadEditorViewModel(Func<Sistema> getSistemaActivo, Action pushUndoSnapshot)
        : this(getSistemaActivo, pushUndoSnapshot, new DxfImportService())
    {
    }

    /// <summary>Constructor con inyección del importador — útil para tests.</summary>
    public CadEditorViewModel(Func<Sistema> getSistemaActivo, Action pushUndoSnapshot, IPlanoImporter importer)
    {
        _getSistemaActivo = getSistemaActivo ?? throw new ArgumentNullException(nameof(getSistemaActivo));
        _pushUndoSnapshot = pushUndoSnapshot ?? throw new ArgumentNullException(nameof(pushUndoSnapshot));
        _importer = importer ?? throw new ArgumentNullException(nameof(importer));
        ImportarDxfCommand = new RelayCommand(_ => ImportarDxf());
        ImportarPdfCommand = new RelayCommand(_ => _ = ImportarPdfAsync());
        MapearPoligonoCommand = new RelayCommand(p => MapearPoligono(p as PolilineaCad));
        ActualizarLosaCommand = new RelayCommand(p => ActualizarLosa(p as ActualizacionLosaArgs));
        CrearBordeAdicCommand = new RelayCommand(p => CrearBordeAdic(p as AdyacenciaCandidata?));
        EncuadrarPlanoCommand = new RelayCommand(_ => EncuadrarPlano());
        EncuadrarPdfCommand = new RelayCommand(_ => EncuadrarPdf());
        CrearLosaCommand = new RelayCommand(p => CrearLosa(p as CrearLosaArgs));
        MoverGrupoCommand = new RelayCommand(p => MoverGrupo(p as MovimientoGrupoArgs));
    }

    /// <summary>
    /// Catálogo de los 23 tipos de losa permitidos, ordenado por código —
    /// fuente del <c>ComboBox</c> del editor in-canvas (Iteración 2 v1.2).
    /// </summary>
    public IReadOnlyList<TipoLosa> CatalogoTipos { get; } =
        TipoLosa.Catalogo.Values.OrderBy(t => t.Codigo).ToList();

    // ---- Plano DXF importado ----

    private PlanoReferencia? _plano;
    /// <summary>Plano de referencia importado del .DXF. Null hasta que se importe uno.</summary>
    public PlanoReferencia? Plano
    {
        get => _plano;
        private set
        {
            _plano = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TienePlano));
            OnPropertyChanged(nameof(EscalaPlano));
            OnPropertyChanged(nameof(OffsetXPlano));
            OnPropertyChanged(nameof(OffsetYPlano));
        }
    }

    /// <summary>True si hay un plano DXF cargado.</summary>
    public bool TienePlano => _plano is { EstaVacio: false };

    // ---- Ajuste espacial del plano DXF (escala / offset / encuadre) — It. 2 ----

    private int _revisionPlano;
    /// <summary>
    /// Token de revisión: se incrementa al cambiar escala/offset para que el
    /// <c>CadCanvasHost</c> redibuje la Capa 1 (el plano es un objeto mutable;
    /// cambiar una de sus propiedades no notifica al lienzo por sí solo).
    /// </summary>
    public int RevisionPlano
    {
        get => _revisionPlano;
        private set { _revisionPlano = value; OnPropertyChanged(); }
    }

    private int _solicitudEncuadre;
    /// <summary>Token de encuadre: se incrementa para pedir un «zoom to fit» del plano.</summary>
    public int SolicitudEncuadre
    {
        get => _solicitudEncuadre;
        private set { _solicitudEncuadre = value; OnPropertyChanged(); }
    }

    /// <summary>Escala uniforme del bloque DXF — proxy bindable de <see cref="PlanoReferencia.Escala"/>.</summary>
    public double EscalaPlano
    {
        get => _plano?.Escala ?? 1.0;
        set
        {
            if (_plano is null || Math.Abs(_plano.Escala - value) < 1e-9) return;
            _plano.Escala = value;
            OnPropertyChanged();
            RevisionPlano++;
        }
    }

    /// <summary>Desplazamiento X del bloque DXF en metros — proxy bindable.</summary>
    public double OffsetXPlano
    {
        get => _plano?.OffsetX ?? 0.0;
        set
        {
            if (_plano is null || Math.Abs(_plano.OffsetX - value) < 1e-9) return;
            _plano.OffsetX = value;
            OnPropertyChanged();
            RevisionPlano++;
        }
    }

    /// <summary>Desplazamiento Y del bloque DXF en metros — proxy bindable.</summary>
    public double OffsetYPlano
    {
        get => _plano?.OffsetY ?? 0.0;
        set
        {
            if (_plano is null || Math.Abs(_plano.OffsetY - value) < 1e-9) return;
            _plano.OffsetY = value;
            OnPropertyChanged();
            RevisionPlano++;
        }
    }

    /// <summary>Encuadra el plano DXF en el viewport del lienzo (zoom to fit).</summary>
    public ICommand EncuadrarPlanoCommand { get; }

    private void EncuadrarPlano()
    {
        if (TienePlano) SolicitudEncuadre++;
    }

    // ---- PDF Underlay importado (Iteración 4 Epic v1.2) ----

    private PdfReferencia? _pdf;
    /// <summary>
    /// Metadata del PDF importado (nombre + dimensiones físicas en metros).
    /// Null hasta que el usuario importe un PDF.
    /// </summary>
    public PdfReferencia? Pdf
    {
        get => _pdf;
        private set
        {
            _pdf = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TienePdf));
            OnPropertyChanged(nameof(EscalaPdf));
            OnPropertyChanged(nameof(OffsetXPdf));
            OnPropertyChanged(nameof(OffsetYPdf));
        }
    }

    private BitmapSource? _fondoPdf;
    /// <summary>
    /// Bitmap rasterizado de la primera página del PDF, ya <c>.Freeze()</c>-eado
    /// para poder cruzarse entre el hilo de background y el UI. Null hasta que
    /// el usuario importe un PDF (o si la importación falló).
    /// </summary>
    public BitmapSource? FondoPdf
    {
        get => _fondoPdf;
        private set
        {
            _fondoPdf = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TienePdf));
        }
    }

    /// <summary>True si hay un PDF con metadata Y bitmap rasterizado disponibles.</summary>
    public bool TienePdf => _pdf is { EstaVacio: false } && _fondoPdf is not null;

    private int _revisionPdf;
    /// <summary>
    /// Token de revisión análogo a <see cref="RevisionPlano"/> pero para el
    /// PDF: se incrementa al cambiar <see cref="EscalaPdf"/>/<see cref="OffsetXPdf"/>/
    /// <see cref="OffsetYPdf"/> para que el <c>CadCanvasHost</c> redibuje la Capa 1.
    /// </summary>
    public int RevisionPdf
    {
        get => _revisionPdf;
        private set { _revisionPdf = value; OnPropertyChanged(); }
    }

    private int _solicitudEncuadrePdf;
    /// <summary>Token de encuadre: se incrementa para pedir un «zoom to fit» del PDF.</summary>
    public int SolicitudEncuadrePdf
    {
        get => _solicitudEncuadrePdf;
        private set { _solicitudEncuadrePdf = value; OnPropertyChanged(); }
    }

    /// <summary>Escala uniforme del bloque PDF — proxy bindable de <see cref="PdfReferencia.Escala"/>.</summary>
    public double EscalaPdf
    {
        get => _pdf?.Escala ?? 1.0;
        set
        {
            if (_pdf is null || Math.Abs(_pdf.Escala - value) < 1e-9) return;
            _pdf.Escala = value;
            OnPropertyChanged();
            RevisionPdf++;
        }
    }

    /// <summary>Desplazamiento X del bloque PDF en metros — proxy bindable.</summary>
    public double OffsetXPdf
    {
        get => _pdf?.OffsetX ?? 0.0;
        set
        {
            if (_pdf is null || Math.Abs(_pdf.OffsetX - value) < 1e-9) return;
            _pdf.OffsetX = value;
            OnPropertyChanged();
            RevisionPdf++;
        }
    }

    /// <summary>Desplazamiento Y del bloque PDF en metros — proxy bindable.</summary>
    public double OffsetYPdf
    {
        get => _pdf?.OffsetY ?? 0.0;
        set
        {
            if (_pdf is null || Math.Abs(_pdf.OffsetY - value) < 1e-9) return;
            _pdf.OffsetY = value;
            OnPropertyChanged();
            RevisionPdf++;
        }
    }

    /// <summary>Encuadra el PDF en el viewport del lienzo (zoom to fit).</summary>
    public ICommand EncuadrarPdfCommand { get; }

    private void EncuadrarPdf()
    {
        if (TienePdf) SolicitudEncuadrePdf++;
    }

    // ---- Herramienta de interacción del lienzo (Iteración 3) ----

    private ModoInteraccionCad _modoInteraccion = ModoInteraccionCad.Puntero;
    /// <summary>
    /// Herramienta activa del lienzo CAD: <see cref="ModoInteraccionCad.Puntero"/>
    /// (selección/arrastre) o <see cref="ModoInteraccionCad.DibujarLosa"/>
    /// (click-drag para crear losas). La cambia la toolbar flotante de <c>CadView</c>.
    /// </summary>
    public ModoInteraccionCad ModoInteraccion
    {
        get => _modoInteraccion;
        set { if (_modoInteraccion == value) return; _modoInteraccion = value; OnPropertyChanged(); }
    }

    private bool _snapActivo = true;
    /// <summary>
    /// Encendido del <see cref="SnappingEngine"/> al mover/redimensionar (toggle
    /// de la toolbar — Iteración 3 Epic v1.2).
    /// </summary>
    public bool SnapActivo
    {
        get => _snapActivo;
        set { if (_snapActivo == value) return; _snapActivo = value; OnPropertyChanged(); }
    }

    private bool _moverConectadas;
    /// <summary>
    /// Si está activo, al arrastrar una losa se arrastran rígidamente todas las
    /// losas conectadas por adyacencia (BFS sobre <c>BordesX</c>/<c>BordesY</c>).
    /// </summary>
    public bool MoverConectadas
    {
        get => _moverConectadas;
        set { if (_moverConectadas == value) return; _moverConectadas = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Crea una <see cref="Losa"/> nueva a partir del rectángulo que el usuario
    /// dibujó en el lienzo (lo dispara <c>CadCanvasHost</c> al soltar el mouse en
    /// modo <see cref="ModoInteraccionCad.DibujarLosa"/>). El parámetro es un
    /// <see cref="CrearLosaArgs"/> con la geometría ya en metros.
    /// </summary>
    public ICommand CrearLosaCommand { get; }

    private void CrearLosa(CrearLosaArgs? args)
    {
        if (args is null) return;

        var sistema = SistemaActivo;
        int nuevoId = sistema.Losas.Count > 0 ? sistema.Losas.Max(l => l.Id) + 1 : 1;

        var losa = new Losa
        {
            Id = nuevoId,
            Tipo = 10,                  // 4 bordes simplemente apoyados (default editable)
            Lx = args.Lx,
            Ly = args.Ly,
            PosX = args.PosX,
            PosY = args.PosY,
            // Carga / Espesor / Rec quedan en sus defaults del modelo Losa.
        };

        // CRÍTICO: snapshot ANTES de mutar el SSOT — preserva el Undo/Redo.
        _pushUndoSnapshot();
        sistema.Losas.Add(losa);

        EstadoImportacion =
            $"✓ Losa {nuevoId} dibujada — {args.Lx:0.00} × {args.Ly:0.00} m. " +
            $"Editá tipo y cargas en el modo Editor.";
        OnPropertyChanged(nameof(Losas));
    }

    // ---- Acceso al SSOT (sistema activo + sus losas) ----

    /// <summary>Sistema activo — fuente de las losas a dibujar en el lienzo.</summary>
    public Sistema SistemaActivo => _getSistemaActivo();

    /// <summary>Colección de losas del sistema activo (el SSOT, no una copia).</summary>
    public ObservableCollection<Losa> Losas => SistemaActivo.Losas;

    // ---- Estado de la última importación (mensaje para la UI) ----

    private string _estadoImportacion = "Sin plano DXF cargado. Usá «Importar DXF…» para abrir uno.";
    public string EstadoImportacion
    {
        get => _estadoImportacion;
        private set { _estadoImportacion = value; OnPropertyChanged(); }
    }

    // ---- Comando: importar un .DXF ----

    /// <summary>Abre un <c>OpenFileDialog</c> filtrado a .dxf e importa el plano.</summary>
    public ICommand ImportarDxfCommand { get; }

    private void ImportarDxf()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Importar plano DXF",
            Filter = "Planos AutoCAD DXF (*.dxf)|*.dxf|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var plano = _importer.Importar(dlg.FileName);
            Plano = plano;
            EstadoImportacion =
                $"✓ {plano.NombreArchivo} — {plano.CantidadEntidades} entidad(es), " +
                $"{plano.Ancho:0.0}×{plano.Alto:0.0} m (unidad origen: {plano.UnidadOriginal}).";
        }
        catch (FileNotFoundException ex)
        {
            EstadoImportacion = $"✕ Archivo no encontrado: {ex.Message}";
        }
        catch (ArgumentException ex)
        {
            EstadoImportacion = $"✕ Ruta inválida: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            EstadoImportacion = $"✕ No se pudo importar el DXF: {ex.Message}";
        }
        catch (Exception ex)
        {
            // Red de seguridad — cualquier fallo inesperado se reporta sin crashear.
            EstadoImportacion = $"✕ Error inesperado al importar: {ex.Message}";
        }
    }

    // ---- Comando: importar un .PDF (Iteración 4 Epic v1.2) ----

    /// <summary>
    /// Abre un <c>OpenFileDialog</c> filtrado a .pdf y rasteriza la primera
    /// página vía <see cref="PdfImportador"/> en un hilo de background, sin
    /// bloquear el UI. Al terminar, asigna <see cref="Pdf"/> + <see cref="FondoPdf"/>
    /// y dispara un encuadre automático.
    /// </summary>
    public ICommand ImportarPdfCommand { get; }

    private async Task ImportarPdfAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Importar PDF como capa de referencia",
            Filter = "Documentos PDF (*.pdf)|*.pdf|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;

        EstadoImportacion = "Rasterizando PDF…";

        var resultado = await PdfImportador.RasterizarPrimeraPaginaAsync(dlg.FileName);

        if (!resultado.EsExito)
        {
            // Mensaje amigable — no propagamos la excepción para no crashear la app.
            MessageBox.Show(
                resultado.Error ?? "Error desconocido al leer el PDF.",
                "Importar PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            EstadoImportacion = "✕ Error al importar PDF.";
            return;
        }

        var meta = resultado.Meta!;
        Pdf = meta;
        FondoPdf = resultado.Imagen;
        EstadoImportacion =
            $"✓ {meta.NombreArchivo} — {meta.Ancho:0.00} × {meta.Alto:0.00} m " +
            $"(rasterizado).";

        // Auto-encuadrar al recién importar para que el PDF caiga centrado.
        SolicitudEncuadrePdf++;
    }

    // ---- Comando: mapear un polígono del plano a una nueva Losa (Fase 2) ----

    /// <summary>
    /// Convierte un polígono rectangular del plano DXF en una <see cref="Losa"/>
    /// nueva. El parámetro es la <see cref="PolilineaCad"/> sobre la que el
    /// usuario hizo clic en el lienzo (lo dispara <c>CadCanvasHost</c>).
    /// </summary>
    public ICommand MapearPoligonoCommand { get; }

    private void MapearPoligono(PolilineaCad? poli)
    {
        if (poli is null) return;

        // Restricción geométrica estricta: solo rectángulos ortogonales.
        if (!PoligonoLosaMapper.TryMapearRectangulo(poli, out var rect))
        {
            EstadoImportacion =
                "✕ El polígono no es un rectángulo ortogonal — no se puede mapear a una losa. " +
                "Solo se aceptan contornos rectangulares de 4 lados a 90°.";
            return;
        }

        var sistema = SistemaActivo;
        int nuevoId = sistema.Losas.Count > 0 ? sistema.Losas.Max(l => l.Id) + 1 : 1;

        // Conversión a coordenadas del lienzo (Y descendente): la esquina
        // superior-izquierda en pantalla corresponde al Y máximo del DXF.
        double posX = rect.MinX;
        double posY = (Plano?.MaxY ?? rect.MaxY) - rect.MaxY;

        var losa = new Losa
        {
            Id = nuevoId,
            Tipo = 10,                  // 4 bordes simplemente apoyados (default editable)
            Lx = rect.Ancho,
            Ly = rect.Alto,
            PosX = posX,
            PosY = posY,
            // Carga / Espesor / Rec quedan en sus defaults del modelo Losa.
        };

        // CRÍTICO: snapshot ANTES de mutar el SSOT — preserva el Undo/Redo.
        _pushUndoSnapshot();
        sistema.Losas.Add(losa);

        EstadoImportacion =
            $"✓ Losa {nuevoId} creada desde el polígono — " +
            $"{rect.Ancho:0.00} × {rect.Alto:0.00} m. Editá tipo y cargas en el modo Editor.";
        OnPropertyChanged(nameof(Losas));
    }

    // ---- Comando: actualizar una losa movida o redimensionada (Fase 3) ----

    /// <summary>
    /// Persiste en el SSOT el resultado de un gesto de <b>mover</b> o
    /// <b>redimensionar</b> una losa en el lienzo CAD. El parámetro es un
    /// <see cref="ActualizacionLosaArgs"/> que el <c>CadCanvasHost</c> construye
    /// al soltar el mouse — el ViewModel nunca ve eventos de <c>System.Windows.Input</c>.
    /// </summary>
    public ICommand ActualizarLosaCommand { get; }

    private void ActualizarLosa(ActualizacionLosaArgs? args)
    {
        if (args is null) return;

        // CRÍTICO: un ÚNICO snapshot ANTES de mutar — todo el arrastre (mover
        // o redimensionar) se revierte con un solo Ctrl+Z.
        _pushUndoSnapshot();

        var losa = args.Losa;
        losa.PosX = args.PosX;
        losa.PosY = args.PosY;
        losa.Lx   = args.Lx;
        losa.Ly   = args.Ly;
        losa.Tipo = args.Tipo;

        EstadoImportacion =
            $"✓ Losa {losa.Id} actualizada — {args.Lx:0.00} × {args.Ly:0.00} m, " +
            $"tipo {args.Tipo} @ ({args.PosX:0.00}, {args.PosY:0.00}).";
        OnPropertyChanged(nameof(Losas));
    }

    // ---- Comando: mover un bloque conectado de losas (Iteración 3 v1.2) ----

    /// <summary>
    /// Aplica las nuevas posiciones a todas las losas del bloque conectado que
    /// se arrastró rígidamente (con <see cref="MoverConectadas"/> activo). El
    /// <c>CadCanvasHost</c> arma el DTO con la componente conexa y las nuevas
    /// <c>PosX/PosY</c>; el ViewModel toma <b>un único</b> snapshot de Undo y
    /// muta el SSOT (Ctrl+Z revierte el bloque entero de una vez).
    /// </summary>
    public ICommand MoverGrupoCommand { get; }

    private void MoverGrupo(MovimientoGrupoArgs? args)
    {
        if (args is null || args.Movimientos.Count == 0) return;

        // CRÍTICO: UN solo snapshot para todo el bloque conectado.
        _pushUndoSnapshot();

        int n = 0;
        var losas = SistemaActivo.Losas;
        foreach (var m in args.Movimientos)
        {
            var losa = losas.FirstOrDefault(l => l.Id == m.LosaId);
            if (losa is null) continue;
            losa.PosX = m.PosX;
            losa.PosY = m.PosY;
            n++;
        }

        EstadoImportacion = $"✓ Bloque conectado movido — {n} losa(s) actualizadas.";
        OnPropertyChanged(nameof(Losas));
    }

    // ---- Comando: crear una adyacencia (BordeAdic) desde un chip del lienzo (Fase 4) ----

    /// <summary>
    /// Crea un <see cref="BordeAdic"/> a partir de un chip de adyacencia del
    /// lienzo CAD. El parámetro es la <see cref="AdyacenciaCandidata"/> detectada
    /// por <see cref="AdyacenciaDetector"/> — el ViewModel toma el snapshot de
    /// Undo y agrega el borde a <c>BordesX</c> o <c>BordesY</c> según el sentido.
    /// </summary>
    public ICommand CrearBordeAdicCommand { get; }

    private void CrearBordeAdic(AdyacenciaCandidata? c)
    {
        if (c is null) return;
        var cand = c.Value;

        // CRÍTICO: snapshot ANTES de mutar el SSOT — preserva el Undo/Redo.
        _pushUndoSnapshot();

        var borde = new BordeAdic { BI = cand.BI, BJ = cand.BJ, Balanceo = "S" };
        (cand.EsBordeX ? SistemaActivo.BordesX : SistemaActivo.BordesY).Add(borde);

        EstadoImportacion =
            $"✓ Adyacencia {(cand.EsBordeX ? "X" : "Y")} creada entre las losas " +
            $"{cand.BI} y {cand.BJ}.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
