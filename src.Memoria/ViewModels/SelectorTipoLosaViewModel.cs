using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LosasPlus.Models;
using MemoriaPlus.Common;

namespace MemoriaPlus.ViewModels;

/// <summary>
/// Categorías visibles en los pills del selector de tipo de losa. Mapean al
/// catálogo Pieper-Martens contando bordes continuos (empotrados) y separando
/// los voladizos puros como categoría propia.
/// </summary>
public enum TipoLosaGrupo
{
    Todos,
    ApoyoSimple,
    UnBordeContinuo,
    DosBordes,
    TresBordes,
    CuatroBordes,
    Voladizos,
}

/// <summary>
/// VM del modal "Seleccionar tipo de losa". Expone el catálogo completo, los
/// filtros por categoría visual y por texto de búsqueda, la selección actual,
/// y los commands Confirmar / Cancelar.
///
/// <para>
/// Es un VM efímero — se crea fresco cada vez que el usuario abre el modal
/// desde la columna TIPO del DataGrid de Niveles. Al confirmar, devuelve el
/// código numérico vía <see cref="TipoConfirmado"/> (asignado por el dialog
/// host). Cancelar deja <see cref="TipoConfirmado"/> en null.
/// </para>
/// </summary>
public sealed class SelectorTipoLosaViewModel : INotifyPropertyChanged
{
    private TipoLosaGrupo _grupoActivo = TipoLosaGrupo.Todos;
    private string _busquedaTexto = "";
    private TipoLosa? _tipoSeleccionado;

    /// <summary>
    /// Construye el VM con <paramref name="tipoInicial"/> ya seleccionado si
    /// existe en el catálogo (ej. el tipo actual de la losa que se está
    /// editando). Null o inválido → sin selección inicial.
    /// </summary>
    public SelectorTipoLosaViewModel(int? tipoInicial = null)
    {
        Todos = TipoLosa.Catalogo.Values
            .OrderBy(t => t.Codigo)
            .ToList();
        TiposFiltrados = new ObservableCollection<TipoLosa>(Todos);

        if (tipoInicial.HasValue && TipoLosa.Catalogo.TryGetValue(tipoInicial.Value, out var t))
            _tipoSeleccionado = t;

        FiltrarPorGrupoCommand = new RelayCommand<string>(SetGrupoDesdeString);
        SeleccionarCommand    = new RelayCommand<TipoLosa>(t => TipoSeleccionado = t);
        ConfirmarCommand      = new RelayCommand(Confirmar, () => _tipoSeleccionado != null);
        CancelarCommand       = new RelayCommand(Cancelar);
    }

    /// <summary>Catálogo completo, sin filtrar. Inmutable durante la vida del VM.</summary>
    public IReadOnlyList<TipoLosa> Todos { get; }

    /// <summary>Vista filtrada por grupo + búsqueda. Re-puebla en cada cambio.</summary>
    public ObservableCollection<TipoLosa> TiposFiltrados { get; }

    public TipoLosa? TipoSeleccionado
    {
        get => _tipoSeleccionado;
        set
        {
            _tipoSeleccionado = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EtiquetaTipoActivo));
            ConfirmarCommand.RaiseCanExecuteChanged();
        }
    }

    public TipoLosaGrupo GrupoActivo
    {
        get => _grupoActivo;
        set
        {
            if (_grupoActivo == value) return;
            _grupoActivo = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EsGrupoTodos));
            OnPropertyChanged(nameof(EsGrupoApoyoSimple));
            OnPropertyChanged(nameof(EsGrupoUnBorde));
            OnPropertyChanged(nameof(EsGrupoDosBordes));
            OnPropertyChanged(nameof(EsGrupoTresBordes));
            OnPropertyChanged(nameof(EsGrupoCuatroBordes));
            OnPropertyChanged(nameof(EsGrupoVoladizos));
            ActualizarFiltrados();
        }
    }

    public bool EsGrupoTodos         => _grupoActivo == TipoLosaGrupo.Todos;
    public bool EsGrupoApoyoSimple   => _grupoActivo == TipoLosaGrupo.ApoyoSimple;
    public bool EsGrupoUnBorde       => _grupoActivo == TipoLosaGrupo.UnBordeContinuo;
    public bool EsGrupoDosBordes     => _grupoActivo == TipoLosaGrupo.DosBordes;
    public bool EsGrupoTresBordes    => _grupoActivo == TipoLosaGrupo.TresBordes;
    public bool EsGrupoCuatroBordes  => _grupoActivo == TipoLosaGrupo.CuatroBordes;
    public bool EsGrupoVoladizos     => _grupoActivo == TipoLosaGrupo.Voladizos;

    public string BusquedaTexto
    {
        get => _busquedaTexto;
        set
        {
            if (_busquedaTexto == (value ?? "")) return;
            _busquedaTexto = value ?? "";
            OnPropertyChanged();
            ActualizarFiltrados();
        }
    }

    // ----- Conteos por grupo (para los pills de la UI) ------------------

    public int ConteoTotal        => Todos.Count;
    public int ConteoApoyoSimple  => Todos.Count(t => Grupo(t) == TipoLosaGrupo.ApoyoSimple);
    public int ConteoUnBorde      => Todos.Count(t => Grupo(t) == TipoLosaGrupo.UnBordeContinuo);
    public int ConteoDosBordes    => Todos.Count(t => Grupo(t) == TipoLosaGrupo.DosBordes);
    public int ConteoTresBordes   => Todos.Count(t => Grupo(t) == TipoLosaGrupo.TresBordes);
    public int ConteoCuatroBordes => Todos.Count(t => Grupo(t) == TipoLosaGrupo.CuatroBordes);
    public int ConteoVoladizos    => Todos.Count(t => Grupo(t) == TipoLosaGrupo.Voladizos);

    /// <summary>
    /// Etiqueta del footer "Tipo activo: X — descripción". Vacía cuando no hay
    /// selección.
    /// </summary>
    public string EtiquetaTipoActivo =>
        _tipoSeleccionado is null
            ? "(ningún tipo seleccionado)"
            : $"Tipo activo: {_tipoSeleccionado.Codigo} — {_tipoSeleccionado.Descripcion}";

    // ----- Output -------------------------------------------------------

    /// <summary>
    /// Código del tipo confirmado por el usuario. Null mientras el modal
    /// está abierto o si se cancela.
    /// </summary>
    public int? TipoConfirmado { get; private set; }

    /// <summary>
    /// Disparado al ejecutar Confirmar (después de set TipoConfirmado) o
    /// Cancelar (con TipoConfirmado=null). El dialog host se suscribe para
    /// cerrar la ventana modal.
    /// </summary>
    public event EventHandler? Cerrar;

    public RelayCommand<string> FiltrarPorGrupoCommand { get; }
    public RelayCommand<TipoLosa> SeleccionarCommand   { get; }
    public RelayCommand ConfirmarCommand               { get; }
    public RelayCommand CancelarCommand                { get; }

    // ----- Internos -----------------------------------------------------

    /// <summary>
    /// Clasifica un tipo del catálogo en uno de los grupos del selector.
    /// Voladizos puros (≥3 bordes con vuelo) tienen prioridad sobre el conteo
    /// de empotramientos para no aparecer en "1 borde continuo".
    /// </summary>
    public static TipoLosaGrupo Grupo(TipoLosa t)
    {
        if (t.BordesVuelo >= 3) return TipoLosaGrupo.Voladizos;
        return t.BordesContinuos switch
        {
            0 => TipoLosaGrupo.ApoyoSimple,
            1 => TipoLosaGrupo.UnBordeContinuo,
            2 => TipoLosaGrupo.DosBordes,
            3 => TipoLosaGrupo.TresBordes,
            4 => TipoLosaGrupo.CuatroBordes,
            _ => TipoLosaGrupo.Todos,
        };
    }

    private void ActualizarFiltrados()
    {
        IEnumerable<TipoLosa> q = Todos;
        if (_grupoActivo != TipoLosaGrupo.Todos)
            q = q.Where(t => Grupo(t) == _grupoActivo);

        var texto = _busquedaTexto.Trim();
        if (!string.IsNullOrEmpty(texto))
        {
            q = q.Where(t =>
                t.Codigo.ToString().Contains(texto, StringComparison.OrdinalIgnoreCase)
                || t.Descripcion.Contains(texto, StringComparison.OrdinalIgnoreCase));
        }

        TiposFiltrados.Clear();
        foreach (var t in q) TiposFiltrados.Add(t);
    }

    private void SetGrupoDesdeString(string? clave)
    {
        GrupoActivo = clave switch
        {
            "ApoyoSimple"     => TipoLosaGrupo.ApoyoSimple,
            "UnBordeContinuo" => TipoLosaGrupo.UnBordeContinuo,
            "DosBordes"       => TipoLosaGrupo.DosBordes,
            "TresBordes"      => TipoLosaGrupo.TresBordes,
            "CuatroBordes"    => TipoLosaGrupo.CuatroBordes,
            "Voladizos"       => TipoLosaGrupo.Voladizos,
            _                 => TipoLosaGrupo.Todos,
        };
    }

    private void Confirmar()
    {
        TipoConfirmado = _tipoSeleccionado?.Codigo;
        Cerrar?.Invoke(this, EventArgs.Empty);
    }

    private void Cancelar()
    {
        TipoConfirmado = null;
        Cerrar?.Invoke(this, EventArgs.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
