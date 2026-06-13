using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using MemoriaPlus.Services;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Services;

namespace LosasPlus.ViewModels;

/// <summary>
/// Sub-VM de servicios CAD consumido por Planta 2D (post-UI1.6).
///
/// <para>
/// Coordina la importación de planos <c>.DXF</c> y <c>.PDF</c>, la
/// calibración del underlay, el módulo de muros y el auto-alineado. Expone
/// el <see cref="PlanoReferencia"/> y el <see cref="PdfReferencia"/> para
/// que el <c>PlantaCanvas</c> los dibuje. Las losas siguen viniendo del
/// <b>SSOT</b> (<see cref="Sistema.Losas"/> del sistema activo) — el CAD es
/// una vista más sobre la misma colección, no un estado paralelo.
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
        EliminarDxfCommand = new RelayCommand(_ => EliminarDxf());
        EliminarPdfCommand = new RelayCommand(_ => EliminarPdf());
        AutoAlinearSistemasCommand = new RelayCommand(_ => AutoAlinearSistemas());
        MapearPoligonoCommand = new RelayCommand(p => MapearPoligono(p as PolilineaCad));
        AplicarCalibrarPdfCommand = new RelayCommand(p => AplicarCalibrarPdf(p as CalibracionPdfArgs));
        _eliminarMuroCommand = new RelayCommand(_ => EliminarMuro(), _ => _muroSeleccionado is not null);
    }

    // ---- Plano DXF importado ----

    private PlanoReferencia? _plano;
    /// <summary>Plano de referencia importado del .DXF. Null hasta que se importe uno.</summary>
    public PlanoReferencia? Plano
    {
        get => _plano;
        internal set
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

    // ---- Ajuste espacial del plano DXF (escala / offset) ----

    private int _revisionPlano;
    /// <summary>
    /// Token de revisión: se incrementa al cambiar escala/offset para que el
    /// <c>PlantaCanvas</c> redibuje la capa del underlay DXF (el plano es un
    /// objeto mutable; cambiar una de sus propiedades no notifica al lienzo por
    /// sí solo).
    /// </summary>
    public int RevisionPlano
    {
        get => _revisionPlano;
        private set { _revisionPlano = value; OnPropertyChanged(); }
    }

    /// <summary>Escala uniforme del bloque DXF — proxy bindable de <see cref="PlanoReferencia.Escala"/>.</summary>
    public double EscalaPlano
    {
        get => _plano?.Escala ?? 1.0;
        set
        {
            // Clamp UI1.7: el TextBox commitea un «0» transitorio al teclear
            // «0.5» y PlantaAPlano divide por Escala — rechazar sin mutar.
            if (!double.IsFinite(value) || value <= 0) return;
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

    // ---- PDF Underlay importado ----

    private PdfReferencia? _pdf;
    /// <summary>
    /// Metadata del PDF importado (nombre + dimensiones físicas en metros).
    /// Null hasta que el usuario importe un PDF.
    /// </summary>
    public PdfReferencia? Pdf
    {
        get => _pdf;
        internal set
        {
            _pdf = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TienePdf));
            OnPropertyChanged(nameof(EscalaPdf));
            OnPropertyChanged(nameof(OffsetXPdf));
            OnPropertyChanged(nameof(OffsetYPdf));
        }
    }

    private Bitmap?_fondoPdf;
    /// <summary>
    /// Bitmap rasterizado de la primera página del PDF, ya congelado para
    /// poder cruzarse entre el hilo de background y el UI. Null hasta que
    /// el usuario importe un PDF (o si la importación falló).
    /// </summary>
    public Bitmap?FondoPdf
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
    /// <see cref="OffsetYPdf"/> para que el <c>PlantaCanvas</c> redibuje la
    /// capa del underlay PDF.
    /// </summary>
    public int RevisionPdf
    {
        get => _revisionPdf;
        private set { _revisionPdf = value; OnPropertyChanged(); }
    }

    /// <summary>Escala uniforme del bloque PDF — proxy bindable de <see cref="PdfReferencia.Escala"/>.</summary>
    public double EscalaPdf
    {
        get => _pdf?.Escala ?? 1.0;
        set
        {
            // Clamp UI1.7: el TextBox commitea un «0» transitorio al teclear
            // «0.5» y PlantaAPlano divide por Escala — rechazar sin mutar.
            if (!double.IsFinite(value) || value <= 0) return;
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

    private double _opacidadPdf = 0.6;
    /// <summary>
    /// Opacidad del PDF underlay (rango 0.0-1.0, default 0.6).
    /// Bindeada al <c>Slider</c> del panel lateral de Planta 2D; al cambiar
    /// incrementa <see cref="RevisionPdf"/> para forzar el redibujado.
    /// Aplica sólo al PDF — el DXF se dibuja con su propio pincel sólido.
    /// </summary>
    public double OpacidadPdf
    {
        get => _opacidadPdf;
        set
        {
            double v = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_opacidadPdf - v) < 1e-9) return;
            _opacidadPdf = v;
            OnPropertyChanged();
            RevisionPdf++;
        }
    }

    // Modo oscuro (invertir los canales B/G/R del PDF para que el fondo blanco
    // quede negro y las líneas oscuras se vuelvan blancas resplandecientes).
    private string? _lastPdfPath;       // ruta del último PDF importado con éxito
    private bool _invirtiendoEnCurso;   // gate anti-doble-click durante la re-rasterización

    private bool _invertirColorPdf;
    /// <summary>
    /// Modo oscuro del PDF underlay: si <c>true</c>, los canales B/G/R del
    /// bitmap rasterizado se invierten (preservando Alpha). Al cambiar,
    /// dispara una re-rasterización asíncrona que SOLO actualiza
    /// <see cref="FondoPdf"/> — la metadata <see cref="Pdf"/> (incluida la
    /// calibración Escala/Offset) se preserva intacta.
    /// </summary>
    public bool InvertirColorPdf
    {
        get => _invertirColorPdf;
        set
        {
            if (_invertirColorPdf == value) return;
            _invertirColorPdf = value;
            OnPropertyChanged();
            if (_lastPdfPath is not null && _fondoPdf is not null && !_invirtiendoEnCurso)
                _ = ReRasterizarPdfAsync();
        }
    }

    /// <summary>
    /// Re-rasteriza la primera página con la inversión de color vigente
    /// (lo dispara <see cref="InvertirColorPdf"/>). Sólo actualiza
    /// <see cref="FondoPdf"/>; la metadata <see cref="Pdf"/> (incluida la
    /// calibración Escala/Offset) se preserva intacta.
    /// </summary>
    private async Task ReRasterizarPdfAsync()
    {
        if (_lastPdfPath is null) return;
        _invirtiendoEnCurso = true;
        try
        {
            EstadoImportacion = _invertirColorPdf
                ? "Aplicando modo oscuro…"
                : "Restaurando colores normales…";

            var r = await PdfImportador.RasterizarPrimeraPaginaAsync(
                _lastPdfPath, invertColors: _invertirColorPdf);

            if (!r.EsExito)
            {
                _ = AppServices.MessageBox.InfoAsync("Re-rasterizar PDF",
                    r.Error ?? "Error al re-rasterizar el PDF.");
                EstadoImportacion = "✕ No se pudo re-rasterizar el PDF.";
                return;
            }

            // CRÍTICO: sólo actualizamos el bitmap. La metadata Pdf (con su
            // Escala/Offset calibrados por el usuario) se preserva intacta.
            FondoPdf = r.Imagen;
            RevisionPdf++;
            EstadoImportacion = _invertirColorPdf
                ? "✓ Modo oscuro activado — fondo negro con líneas blancas."
                : "✓ Modo claro restaurado.";
        }
        finally { _invirtiendoEnCurso = false; }
    }

    // ---- Eliminación de planos de referencia ----

    /// <summary>Quita el plano DXF del lienzo (vacía la capa del underlay; no toca las losas).</summary>
    public ICommand EliminarDxfCommand { get; }

    private void EliminarDxf()
    {
        if (_plano is null) return;
        Plano = null;
        EstadoImportacion = "✓ Plano DXF eliminado del lienzo.";
    }

    /// <summary>Quita el PDF underlay del lienzo (bitmap + metadata + ruta cacheada).</summary>
    public ICommand EliminarPdfCommand { get; }

    private void EliminarPdf()
    {
        if (_pdf is null && _fondoPdf is null) return;
        Pdf = null;
        FondoPdf = null;
        _lastPdfPath = null;   // libera el bitmap y el estado del PDF
        EstadoImportacion = "✓ PDF eliminado del lienzo.";
    }

    // ---- Auto-alineación y auto-conexión de losas ----

    /// <summary>
    /// Refresca la leyenda «Suma de Colores» cuando los muros mutan desde
    /// Planta 2D (UI1.4): el SSOT de muros es compartido, pero la notificación
    /// de <see cref="ResumenMuros"/> sólo la disparaban los paths del panel CAD.
    /// </summary>
    public void RefrescarResumenMuros() => OnPropertyChanged(nameof(ResumenMuros));

    /// <summary>
    /// Ejecuta el <see cref="MotorGeometriaAnalitica"/> sobre el sistema
    /// activo: alinea las losas vecinas que casi se tocan y genera los
    /// bordes de continuidad de acero entre las que quedan en contacto.
    /// </summary>
    public ICommand AutoAlinearSistemasCommand { get; }

    private void AutoAlinearSistemas()
    {
        var sistema = SistemaActivo;
        if (sistema.Losas.Count < 2)
        {
            EstadoImportacion = "Se necesitan al menos 2 losas para auto-conectar.";
            return;
        }

        // CRÍTICO: un único snapshot de Undo antes de mutar el SSOT — toda la
        // optimización (posiciones + bordes) se revierte con un solo Ctrl+Z.
        _pushUndoSnapshot();

        var r = MotorGeometriaAnalitica.EjecutarAlineacionYConexion(sistema);

        if (r.LosasAlineadasCount > 0 || r.BordesCreadosCount > 0)
        {
            OnPropertyChanged(nameof(Losas));
        }

        EstadoImportacion =
            $"✓ Sistema optimizado: se alinearon {r.LosasAlineadasCount} losas y " +
            $"se generaron {r.BordesCreadosCount} conexiones de aceros adicionales.";
    }

    // ---- Módulo de Muros ----

    private double _espesorMuroNuevo = 0.15;
    /// <summary>Espesor (m) asignado a los muros nuevos que se dibujan en el lienzo.</summary>
    public double EspesorMuroNuevo
    {
        get => _espesorMuroNuevo;
        set
        {
            double v = Math.Max(0.01, value);
            if (Math.Abs(_espesorMuroNuevo - v) < 1e-9) return;
            _espesorMuroNuevo = v;
            OnPropertyChanged();
        }
    }

    private double _alturaMuroNueva = 2.80;
    /// <summary>Altura libre (m) asignada a los muros nuevos.</summary>
    public double AlturaMuroNueva
    {
        get => _alturaMuroNueva;
        set
        {
            double v = Math.Max(0.01, value);
            if (Math.Abs(_alturaMuroNueva - v) < 1e-9) return;
            _alturaMuroNueva = v;
            OnPropertyChanged();
        }
    }

    private Muro? _muroSeleccionado;
    /// <summary>
    /// Muro actualmente seleccionado en el panel «MUROS» de Planta 2D. Sus
    /// parámetros (espesor, altura) se editan desde el panel.
    /// </summary>
    public Muro? MuroSeleccionado
    {
        get => _muroSeleccionado;
        set
        {
            if (ReferenceEquals(_muroSeleccionado, value)) return;
            if (_muroSeleccionado is not null)
                _muroSeleccionado.PropertyChanged -= OnMuroSeleccionadoEditado;
            _muroSeleccionado = value;
            if (_muroSeleccionado is not null)
                _muroSeleccionado.PropertyChanged += OnMuroSeleccionadoEditado;
            OnPropertyChanged();
            // Revaluar predicado que lee _muroSeleccionado.
            _eliminarMuroCommand.RaiseCanExecuteChanged();
        }
    }

    private void OnMuroSeleccionadoEditado(object? sender, PropertyChangedEventArgs e)
    {
        // El usuario editó espesor/altura en el panel → recomputar leyenda.
        OnPropertyChanged(nameof(ResumenMuros));
    }

    /// <summary>
    /// Desglose «Suma de Colores»: longitudes totales de muro por losa y por
    /// espesor. Lo consume la leyenda flotante del lienzo de Planta 2D.
    /// </summary>
    public IReadOnlyList<EntradaResumenMuro> ResumenMuros
        => AnalisisMuros.Resumir(SistemaActivo).Entradas;

    // Tipado como RelayCommand (no ICommand) para poder llamar RaiseCanExecuteChanged()
    // desde el setter de MuroSeleccionado — Avalonia carece de CommandManager.RequerySuggested.
    private readonly RelayCommand _eliminarMuroCommand;
    /// <summary>Elimina el <see cref="MuroSeleccionado"/> del sistema activo.</summary>
    public RelayCommand EliminarMuroCommand => _eliminarMuroCommand;

    private void EliminarMuro()
    {
        if (_muroSeleccionado is null) return;
        var muro = _muroSeleccionado;

        // CRÍTICO: snapshot ANTES de mutar el SSOT.
        _pushUndoSnapshot();

        SistemaActivo.Muros.Remove(muro);
        MuroSeleccionado = null;

        EstadoImportacion = $"✓ Muro {muro.Id} eliminado.";
        OnPropertyChanged(nameof(ResumenMuros));
    }

    // ---- Calibración interactiva del PDF ----

    /// <summary>
    /// Aplica el factor de homotecia al PDF tras recibir los dos puntos y la
    /// distancia real introducida por el usuario. Lo invoca el code-behind de
    /// <c>Planta2DEditorView</c> al confirmar el gesto de calibración (2 puntos
    /// + distancia real) con la herramienta «🎯 Calibrar PDF» del
    /// <c>PlantaCanvas</c>. La homotecia compartida vive en <c>CalibradorPdf</c>
    /// (src.Core): conserva el pivote <c>(PivoteX, PivoteY)</c> fijo en el
    /// lienzo y corrige <see cref="EscalaPdf"/>/<see cref="OffsetXPdf"/>/
    /// <see cref="OffsetYPdf"/> in situ.
    /// </summary>
    public ICommand AplicarCalibrarPdfCommand { get; }

    private void AplicarCalibrarPdf(CalibracionPdfArgs? args)
    {
        if (args is null || _pdf is null) return;

        // Homotecia compartida (UI1.2): CalibradorPdf conserva el pivote P₁
        // fijo en el lienzo y corrige Escala/Offsets in situ. Devuelve null si
        // las distancias son inválidas — en ese caso no muta nada.
        double? factor = CalibradorPdf.Calibrar(_pdf, args);
        if (factor is null)
        {
            EstadoImportacion = "✕ Calibración cancelada: la distancia debe ser mayor a cero.";
            return;
        }

        // Forzar redibujado síncrono de la capa del underlay y notificar a los proxies.
        OnPropertyChanged(nameof(EscalaPdf));
        OnPropertyChanged(nameof(OffsetXPdf));
        OnPropertyChanged(nameof(OffsetYPdf));
        RevisionPdf++;

        EstadoImportacion =
            $"✓ PDF recalibrado — factor ×{factor.Value:0.0000} " +
            $"({args.DistanciaActual:0.000} m → {args.DistanciaReal:0.000} m).";
    }

    // ---- Comando: mapear un polígono del plano a una nueva Losa ----

    /// <summary>
    /// Convierte un polígono rectangular del plano DXF en una <see cref="Losa"/>
    /// nueva. El parámetro es la <see cref="PolilineaCad"/> sobre la que el
    /// usuario hizo clic en el <c>PlantaCanvas</c> (herramienta «▱ Calcar losa»).
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
            CoordenadaX = posX,
            CoordenadaY = posY,
            Anclada = true,             // mapeada del plano: posición explícita
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

    private async void ImportarDxf()
    {
        var ruta = await AppServices.Dialogs.OpenFileAsync("Importar plano DXF",
            new FileFilter("Planos AutoCAD DXF", new[] { "*.dxf" }),
            new FileFilter("Todos los archivos", new[] { "*.*" }));
        if (ruta is null) return;

        try
        {
            var plano = _importer.Importar(ruta);
            Plano = plano;
            EstadoImportacion = plano.CantidadEntidades > 0
                ? $"✓ {plano.NombreArchivo} — {plano.CantidadEntidades} entidad(es), " +
                  $"{plano.Ancho:0.0}×{plano.Alto:0.0} m (unidad origen: {plano.UnidadOriginal})."
                : $"⚠ {plano.NombreArchivo} importado pero sin entidades reconocidas " +
                  $"(tipos no soportados como SPLINE / HATCH / DIMENSION / Polyline3D).";
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

    // ---- Comando: importar un .PDF ----

    /// <summary>
    /// Abre un <c>OpenFileDialog</c> filtrado a .pdf y rasteriza la primera
    /// página vía <see cref="PdfImportador"/> en un hilo de background, sin
    /// bloquear el UI. Al terminar, asigna <see cref="Pdf"/> + <see cref="FondoPdf"/>;
    /// el <c>PlantaCanvas</c> redibuja vía los bindings de <c>Pdf</c>/<c>FondoPdf</c>.
    /// </summary>
    public ICommand ImportarPdfCommand { get; }

    private async Task ImportarPdfAsync()
    {
        var ruta = await AppServices.Dialogs.OpenFileAsync("Importar PDF como capa de referencia",
            new FileFilter("Documentos PDF", new[] { "*.pdf" }),
            new FileFilter("Todos los archivos", new[] { "*.*" }));
        if (ruta is null) return;

        EstadoImportacion = "Rasterizando PDF…";

        var resultado = await PdfImportador.RasterizarPrimeraPaginaAsync(ruta);

        if (!resultado.EsExito)
        {
            // Mensaje amigable — no propagamos la excepción para no crashear la app.
            _ = AppServices.MessageBox.InfoAsync("Importar PDF",
                resultado.Error ?? "Error desconocido al leer el PDF.");
            EstadoImportacion = "✕ Error al importar PDF.";
            return;
        }

        var meta = resultado.Meta!;
        Pdf = meta;
        FondoPdf = resultado.Imagen;
        _lastPdfPath = ruta;   // para re-rasterizar al togglear Modo Oscuro
        EstadoImportacion = resultado.Aviso ??
            $"✓ {meta.NombreArchivo} — {meta.Ancho:0.00} × {meta.Alto:0.00} m " +
            $"(rasterizado).";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
