// Plugin de ejemplo: registra dos tipos personalizados y reporta cantidad de losas.
// Hooks soportados: "load", "pre-dl", "post-txt", "custom-export".
// El contexto disponible es PluginContext: Sistema, Hook, OutputTxt, Log(...), RegisterTipo(...).

if (Hook == "load")
{
    RegisterTipo(80, "Losa con un borde libre y tres apoyados");
    RegisterTipo(90, "Losa sobre apoyos puntuales (placa)");
    Log("ejemplo.csx: tipos personalizados 80 y 90 registrados");
}

if (Hook == "post-txt" && Sistema != null)
{
    Log($"ejemplo.csx: sistema '{Sistema.Nombre}' con {Sistema.Losas.Count} losa(s) procesado");
}
