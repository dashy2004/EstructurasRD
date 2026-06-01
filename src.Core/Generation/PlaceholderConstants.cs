namespace LosasPlus.Generation;

/// <summary>
/// Nombres de los placeholders <c>{{...}}</c> presentes en la plantilla
/// <c>Memoria_Losas_PLANTILLA.docx</c> del ingeniero. Verificados extrayendo
/// el XML del <c>.docx</c> con un regex permisivo:
///
/// <code>
/// powershell -c &quot;[regex]::Matches((zip extract), '\{\{([^}]+)\}\}')&quot;
/// </code>
///
/// <para>
/// Contiene caracteres con tilde y eñe (<c>{{MES_AÑO}}</c>,
/// <c>{{NOMBRE_DISEÑADOR_ARQ}}</c>) — el matching debe ser literal byte-a-byte
/// usando <see cref="System.StringComparison.Ordinal"/>.
/// </para>
///
/// <para>
/// El placeholder <c>{{DD/MM/AAAA}}</c> es atípico (incluye barras) y se
/// reemplaza por la fecha de cálculo formateada como <c>dd/MM/yyyy</c>
/// — no es una variable del proyecto sino un timestamp del momento de
/// generación.
/// </para>
/// </summary>
public static class PlaceholderConstants
{
    public const string NombreProyecto       = "{{NOMBRE_PROYECTO}}";
    public const string CiudadUbicacion      = "{{CIUDAD_UBICACION}}";
    public const string MesAno               = "{{MES_AÑO}}";
    public const string UbicacionCompleta    = "{{UBICACION_COMPLETA}}";
    public const string Uso                  = "{{USO}}";
    public const string CantidadNiveles      = "{{CANTIDAD_NIVELES}}";
    public const string SistemaEstructural   = "{{SISTEMA_ESTRUCTURAL}}";
    public const string NombreIngeniero      = "{{NOMBRE_INGENIERO}}";
    public const string Codia                = "{{CODIA}}";
    public const string TelFijo              = "{{TEL_FIJO}}";
    public const string TelCelular           = "{{TEL_CELULAR}}";
    public const string NombreDisenadorArq   = "{{NOMBRE_DISEÑADOR_ARQ}}";
    public const string TipoFundaciones      = "{{TIPO_FUNDACIONES}}";
    public const string EsfuerzoAdmisible    = "{{ESFUERZO_ADMISIBLE}}";
    public const string ProfundidadDesplante = "{{PROFUNDIDAD_DESPLANTE}}";
    public const string OtrosParametros      = "{{OTROS_PARAMETROS}}";
    public const string FechaDdMmAaaa        = "{{DD/MM/AAAA}}";

    /// <summary>Lista cerrada de placeholders de portada/descripcion (para validacion).</summary>
    public static readonly string[] Todos = new[]
    {
        NombreProyecto, CiudadUbicacion, MesAno, UbicacionCompleta, Uso,
        CantidadNiveles, SistemaEstructural, NombreIngeniero, Codia,
        TelFijo, TelCelular, NombreDisenadorArq, TipoFundaciones,
        EsfuerzoAdmisible, ProfundidadDesplante, OtrosParametros, FechaDdMmAaaa,
    };

    // =================================================================
    // BLOQUE PLURINIVEL — markers + placeholders de nivel
    // =================================================================

    /// <summary>
    /// Marca el inicio del bloque plantilla que se clona una vez por nivel.
    /// Convencion: el ingeniero coloca <c>{{NIVEL_BLOQUE_INICIO}}</c> en su
    /// propio parrafo, justo arriba del contenido del nivel-template; coloca
    /// <c>{{NIVEL_BLOQUE_FIN}}</c> en su propio parrafo justo debajo. Todo lo
    /// que quede entre los dos markers se replica para cada
    /// <see cref="LosasPlus.Models.Sistema"/> del proyecto.
    /// </summary>
    public const string NivelBloqueInicio = "{{NIVEL_BLOQUE_INICIO}}";

    /// <summary>Marca el fin del bloque plantilla. Ver <see cref="NivelBloqueInicio"/>.</summary>
    public const string NivelBloqueFin = "{{NIVEL_BLOQUE_FIN}}";

    /// <summary>Nombre del nivel: "E1", "E2", "Techo".</summary>
    public const string NivelNombre = "{{NIVEL_NOMBRE}}";

    /// <summary>Indice 1-based del nivel dentro del proyecto.</summary>
    public const string NivelNumero = "{{NIVEL_NUMERO}}";

    /// <summary>Uso del nivel ("Entrepiso", "Techo", "Balcon", ...).</summary>
    public const string NivelUso = "{{NIVEL_USO}}";

    /// <summary>Cota desde el +0.00 formateada como "+2.80 m".</summary>
    public const string NivelCota = "{{NIVEL_COTA}}";

    /// <summary>Cantidad de losas en el nivel.</summary>
    public const string NivelNumeroLosas = "{{NIVEL_NUMERO_LOSAS}}";

    /// <summary>
    /// Placeholder bloque-completo: el parrafo que lo contiene se REEMPLAZA por
    /// una tabla OpenXml generada con una fila por cada <c>Losa</c> del sistema
    /// del nivel actual. Columnas: ID, Lx, Ly, Cond, h_usar, qd, ql, qu.
    ///
    /// <para>
    /// Convencion: poner el placeholder en su propio parrafo dentro del bloque
    /// NIVEL (entre los markers <see cref="NivelBloqueInicio"/> y
    /// <see cref="NivelBloqueFin"/>). El parrafo desaparece y se inserta una
    /// <see cref="DocumentFormat.OpenXml.Wordprocessing.Table"/> en su lugar.
    /// </para>
    /// </summary>
    public const string TablaLosas = "{{TABLA_LOSAS}}";

    /// <summary>
    /// Tabla de momentos flectores parseados del .txt F. Perdomo.
    /// Columnas: LOSA, TIPO, CARGA, H, Lx, Ly, Mfx, Mfy, -Msx, -Msy.
    /// Si no hay <c>SalidaPerdomo</c> asociada al sistema, el parrafo se
    /// reemplaza por una nota informativa.
    /// </summary>
    public const string TablaMomentos = "{{TABLA_MOMENTOS}}";

    /// <summary>
    /// Tabla de armaduras de vano según X (centro de losa).
    /// Columnas: LOSA, d, Mu, As, Disponer, As (provisto).
    /// </summary>
    public const string TablaArmadurasX = "{{TABLA_ARMADURAS_X}}";

    /// <summary>
    /// Tabla de armaduras de vano según Y (centro de losa).
    /// Mismas columnas que <see cref="TablaArmadurasX"/>.
    /// </summary>
    public const string TablaArmadurasY = "{{TABLA_ARMADURAS_Y}}";

    /// <summary>
    /// Tabla de armaduras sobre apoyos (X e Y combinadas, con columna Dir).
    /// Columnas: Dir, I, J, MuI, MuJ, MuI-J, d, As, AsI, AsJ, DAs, Disponer.
    /// </summary>
    public const string TablaApoyos = "{{TABLA_APOYOS}}";

    /// <summary>Lista cerrada de placeholders interiores al bloque NIVEL.</summary>
    public static readonly string[] TodosNivel = new[]
    {
        NivelNombre, NivelNumero, NivelUso, NivelCota, NivelNumeroLosas,
        TablaLosas, TablaMomentos, TablaArmadurasX, TablaArmadurasY, TablaApoyos,
    };
}
