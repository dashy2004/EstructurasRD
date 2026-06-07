using System.Threading.Tasks;
using LosasPlus.Models;
using LosasPlus.ViewModels;
using MemoriaPlus.Services;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Fase A (pérdida de datos): cobertura a nivel ViewModel del flag
/// <c>IsDirty</c> y de la mutación segura de <c>EliminarSistema</c>
/// (snapshot de undo + confirmación). El handler <c>Closing</c> de la ventana
/// es UI y no se cubre aquí; sí su contrato de VM (IsDirty + GuardarAsync).
/// </summary>
public class IsDirtyPersistenciaTests : System.IDisposable
{
    private readonly IMessageBoxService _msgBoxOriginal = AppServices.MessageBox;

    /// <summary>Restaura el servicio de diálogos real tras cada test (el stub es estático).</summary>
    public void Dispose() => AppServices.MessageBox = _msgBoxOriginal;


    /// <summary>
    /// Stub de <see cref="IMessageBoxService"/> con respuesta fija para
    /// confirmaciones, evitando el diálogo Avalonia real (que requiere un
    /// TopLevel inexistente en los tests).
    /// </summary>
    private sealed class StubMessageBox : IMessageBoxService
    {
        private readonly bool _confirma;
        public int VecesPreguntado { get; private set; }

        /// <summary>
        /// Respuesta fija de la confirmación de descarte de 3 estados. Default
        /// <see cref="ResultadoDescarte.Cancelar"/> = quedarse (el valor seguro al
        /// descartar el diálogo: Escape / X de la ventana).
        /// </summary>
        public ResultadoDescarte RespuestaDescarte { get; set; } = ResultadoDescarte.Cancelar;
        public int VecesPreguntadoDescarte { get; private set; }

        public StubMessageBox(bool confirma) => _confirma = confirma;
        public Task<bool> ConfirmYesNoAsync(string title, string message)
        {
            VecesPreguntado++;
            return Task.FromResult(_confirma);
        }
        public Task InfoAsync(string title, string message) => Task.CompletedTask;

        public Task<ResultadoDescarte> ConfirmarGuardarDescartarCancelarAsync(string titulo, string mensaje)
        {
            VecesPreguntadoDescarte++;
            return Task.FromResult(RespuestaDescarte);
        }
    }

    private static StubMessageBox InstalarMessageBox(bool confirma)
    {
        var stub = new StubMessageBox(confirma);
        AppServices.MessageBox = stub;
        return stub;
    }

    /// <summary>Instala el stub con una respuesta fija de descarte de 3 estados.</summary>
    private static StubMessageBox InstalarMessageBox(ResultadoDescarte respuesta)
    {
        var stub = new StubMessageBox(confirma: false) { RespuestaDescarte = respuesta };
        AppServices.MessageBox = stub;
        return stub;
    }

    // ---- IsDirty se marca tras una mutación -----------------------------

    [Fact]
    public void Agregar_losa_marca_IsDirty()
    {
        var vm = new MainViewModel();
        Assert.False(vm.IsDirty);

        vm.AgregarLosa();

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Agregar_nivel_marca_IsDirty()
    {
        var vm = new MainViewModel();
        Assert.False(vm.IsDirty);

        vm.AgregarNivel("Nivel 2", 3.0);

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Agregar_sistema_marca_IsDirty()
    {
        var vm = new MainViewModel();
        Assert.False(vm.IsDirty);

        vm.AgregarSistema();

        Assert.True(vm.IsDirty);
    }

    // ---- IsDirty se limpia tras Nuevo / Abrir / Guardar -----------------

    [Fact]
    public void Nuevo_proyecto_limpia_IsDirty()
    {
        var vm = new MainViewModel();
        vm.AgregarLosa();
        Assert.True(vm.IsDirty);

        vm.NuevoProyectoLpx();

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task Guardar_exitoso_limpia_IsDirty()
    {
        var vm = new MainViewModel();
        vm.AgregarLosa();
        Assert.True(vm.IsDirty);

        var ruta = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"losasplus_isdirty_{System.Guid.NewGuid():N}.lpx.json");
        // Set del archivo destino vía un GuardarComo simulado: usamos la API de
        // guardado directo asignando el path y guardando.
        vm.Proyecto.Archivo = ruta;
        var ok = await vm.GuardarAsync();

        Assert.True(ok);
        Assert.False(vm.IsDirty);
        Assert.True(System.IO.File.Exists(ruta));
        System.IO.File.Delete(ruta);
    }

    [Fact]
    public async Task Abrir_proyecto_limpia_IsDirty()
    {
        // 1) Crear y guardar un proyecto a disco.
        var origen = new MainViewModel();
        var ruta = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"losasplus_open_{System.Guid.NewGuid():N}.lpx.json");
        origen.Proyecto.Archivo = ruta;
        Assert.True(await origen.GuardarAsync());

        // 2) En otra VM, ensuciar y luego abrir el archivo → IsDirty debe quedar limpio.
        var vm = new MainViewModel();
        vm.AgregarLosa();
        Assert.True(vm.IsDirty);

        vm.AbrirProyectoLpxPorPath(ruta);

        Assert.False(vm.IsDirty);
        System.IO.File.Delete(ruta);
    }

    // ---- EliminarSistema: snapshot de undo + confirmación ----------------

    [Fact]
    public async Task EliminarSistema_confirmado_toma_snapshot_y_undo_lo_restaura()
    {
        InstalarMessageBox(confirma: true);
        var vm = new MainViewModel();

        // Dos sistemas: el demo inicial + uno agregado con marca identificable.
        var extra = vm.AgregarSistema("Sistema a borrar");
        int countAntes = vm.Proyecto.Sistemas.Count;
        Assert.Contains(vm.Proyecto.Sistemas, s => s.Nombre == "Sistema a borrar");

        await vm.EliminarSistema(extra);

        Assert.Equal(countAntes - 1, vm.Proyecto.Sistemas.Count);
        Assert.DoesNotContain(vm.Proyecto.Sistemas, s => s.Nombre == "Sistema a borrar");

        // El borrado tomó snapshot: Undo debe restaurar el sistema.
        Assert.True(vm.PuedeUndo);
        Assert.True(vm.UndoCommand!.CanExecute(null));
        vm.UndoCommand!.Execute(null);

        Assert.Equal(countAntes, vm.Proyecto.Sistemas.Count);
        Assert.Contains(vm.Proyecto.Sistemas, s => s.Nombre == "Sistema a borrar");
    }

    [Fact]
    public async Task EliminarSistema_cancelado_no_borra_nada()
    {
        var stub = InstalarMessageBox(confirma: false);
        var vm = new MainViewModel();
        var extra = vm.AgregarSistema("No borrar");
        int countAntes = vm.Proyecto.Sistemas.Count;

        await vm.EliminarSistema(extra);

        Assert.Equal(1, stub.VecesPreguntado);
        Assert.Equal(countAntes, vm.Proyecto.Sistemas.Count);
        Assert.Contains(vm.Proyecto.Sistemas, s => s.Nombre == "No borrar");
    }

    // ---- MemoriaPlus: IsDirty se marca y se limpia -----------------------

    [Fact]
    public void Memoria_agregar_sistema_marca_IsDirty()
    {
        var vm = new MemoriaPlus.ViewModels.MainViewModel();
        Assert.False(vm.IsDirty);

        vm.AgregarSistemaCommand.Execute(null);

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task Memoria_nuevo_proyecto_confirmado_limpia_IsDirty()
    {
        InstalarMessageBox(ResultadoDescarte.Descartar);  // descartar y continuar
        var vm = new MemoriaPlus.ViewModels.MainViewModel();
        vm.AgregarSistemaCommand.Execute(null);
        Assert.True(vm.IsDirty);

        await vm.NuevoProyectoConfirmadoAsync();

        Assert.False(vm.IsDirty);
    }

    // ---- Guardia de descarte de 3 estados: dismiss = Cancelar = quedarse ----

    /// <summary>
    /// CRÍTICO (Fase A): el descarte del diálogo (Escape / X) ⇒ Cancelar, que
    /// ABORTA el Nuevo: el proyecto NO se reemplaza y sigue sucio (no se pierde
    /// el trabajo en silencio). El stub devuelve Cancelar por defecto (= dismiss).
    /// </summary>
    [Fact]
    public async Task Nuevo_proyecto_cancelado_no_reemplaza_proyecto()
    {
        InstalarMessageBox(ResultadoDescarte.Cancelar);  // = dismiss (Escape / X)
        var vm = new MainViewModel();
        vm.AgregarLosa();
        Assert.True(vm.IsDirty);
        int countAntes = vm.SistemaActivo.Losas.Count;
        var sistemaAntes = vm.SistemaActivo;

        await vm.NuevoProyectoLpxConfirmadoAsync();

        // Proyecto intacto: el sistema y sus losas siguen ahí y sigue sucio.
        Assert.True(vm.IsDirty);
        Assert.Same(sistemaAntes, vm.SistemaActivo);
        Assert.Equal(countAntes, vm.SistemaActivo.Losas.Count);
    }

    /// <summary>Descartar ⇒ procede el Nuevo: proyecto reemplazado, IsDirty limpio.</summary>
    [Fact]
    public async Task Nuevo_proyecto_descartar_reemplaza_proyecto()
    {
        InstalarMessageBox(ResultadoDescarte.Descartar);
        var vm = new MainViewModel();
        vm.AgregarLosa();
        Assert.True(vm.IsDirty);

        await vm.NuevoProyectoLpxConfirmadoAsync();

        Assert.False(vm.IsDirty);
    }

    /// <summary>Memoria: Cancelar (= dismiss) aborta el Nuevo y deja el proyecto sucio.</summary>
    [Fact]
    public async Task Memoria_nuevo_proyecto_cancelado_no_reemplaza_proyecto()
    {
        InstalarMessageBox(ResultadoDescarte.Cancelar);
        var vm = new MemoriaPlus.ViewModels.MainViewModel();
        vm.AgregarSistemaCommand.Execute(null);
        Assert.True(vm.IsDirty);
        var proyectoAntes = vm.ProyectoActivo;

        await vm.NuevoProyectoConfirmadoAsync();

        // Proyecto intacto y sigue sucio: no se descartó nada en silencio.
        Assert.True(vm.IsDirty);
        Assert.Same(proyectoAntes, vm.ProyectoActivo);
    }

    // ---- IsDirty en importar .TXT y calcular nativo (Fase A) -------------

    /// <summary>Importar un .TXT muta el modelo ⇒ debe marcar IsDirty.</summary>
    [Fact]
    public async Task ImportarTxt_marca_IsDirty()
    {
        var vm = new MainViewModel();
        // Estado limpio de partida (Nuevo limpia IsDirty).
        vm.NuevoProyectoLpx();
        Assert.False(vm.IsDirty);

        // .TXT mínimo con un bloque de resultados para una losa existente.
        var ruta = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"losasplus_txt_{System.Guid.NewGuid():N}.TXT");
        var idLosa = vm.SistemaActivo.Losas.Count > 0 ? vm.SistemaActivo.Losas[0].Id : 1;
        var contenido = $"LOSA {idLosa}\r\nMXV = 1.23\r\nMYV = 2.34\r\n";
        await System.IO.File.WriteAllTextAsync(ruta, contenido,
            System.Text.Encoding.GetEncoding(1252));

        await vm.ImportarTxtAsync(ruta);

        Assert.True(vm.IsDirty);
        System.IO.File.Delete(ruta);
    }

    /// <summary>Calcular con el motor nativo muta el modelo ⇒ debe marcar IsDirty.</summary>
    [Fact]
    public void CalcularNativo_marca_IsDirty()
    {
        var vm = new MainViewModel();
        vm.NuevoProyectoLpx();
        Assert.False(vm.IsDirty);

        vm.CalcularNativo();

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task Memoria_guardar_exitoso_limpia_IsDirty()
    {
        var vm = new MemoriaPlus.ViewModels.MainViewModel();
        vm.AgregarSistemaCommand.Execute(null);
        Assert.True(vm.IsDirty);

        var ruta = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"memoria_isdirty_{System.Guid.NewGuid():N}.lpx.json");
        vm.ProyectoActivo.Archivo = ruta;
        var ok = await vm.GuardarAsync();

        Assert.True(ok);
        Assert.False(vm.IsDirty);
        Assert.True(System.IO.File.Exists(ruta));
        System.IO.File.Delete(ruta);
    }
}
