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

    /// <summary>Lista cerrada de placeholders conocidos (para validacion).</summary>
    public static readonly string[] Todos = new[]
    {
        NombreProyecto, CiudadUbicacion, MesAno, UbicacionCompleta, Uso,
        CantidadNiveles, SistemaEstructural, NombreIngeniero, Codia,
        TelFijo, TelCelular, NombreDisenadorArq, TipoFundaciones,
        EsfuerzoAdmisible, ProfundidadDesplante, OtrosParametros, FechaDdMmAaaa,
    };
}
