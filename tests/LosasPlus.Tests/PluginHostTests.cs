using System.IO;
using System.Linq;
using System.Text;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del sistema de plugins: parser de manifest, trust store con SHA-256,
/// y carga de plugins con gating por confianza.
/// </summary>
public class PluginHostTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"losasplus_plugins_{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    // ---------- Manifest ----------

    [Fact]
    public void Manifest_se_lee_desde_archivo_csx_manifest_json_adyacente()
    {
        var dir = TempDir();
        try
        {
            var csx = Path.Combine(dir, "myplugin.csx");
            File.WriteAllText(csx, "// nada");
            File.WriteAllText(csx + ".manifest.json",
                "{\"name\":\"Mi Plugin\",\"author\":\"Acme\",\"version\":\"1.2.3\"," +
                "\"description\":\"Hace cosas\",\"hooks\":[\"load\",\"post-txt\"]}",
                Encoding.UTF8);

            var m = PluginManifest.LoadFor(csx);
            Assert.NotNull(m);
            Assert.Equal("Mi Plugin", m!.Name);
            Assert.Equal("Acme", m.Author);
            Assert.Equal("1.2.3", m.Version);
            Assert.Contains("load", m.Hooks);
            Assert.Contains("post-txt", m.Hooks);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Manifest_es_null_si_no_existe()
    {
        var dir = TempDir();
        try
        {
            var csx = Path.Combine(dir, "noprops.csx");
            File.WriteAllText(csx, "// solo");
            Assert.Null(PluginManifest.LoadFor(csx));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Manifest_invalido_retorna_null_en_lugar_de_crashear()
    {
        var dir = TempDir();
        try
        {
            var csx = Path.Combine(dir, "bad.csx");
            File.WriteAllText(csx, "// vacío");
            File.WriteAllText(csx + ".manifest.json", "{ esto no es JSON }", Encoding.UTF8);
            Assert.Null(PluginManifest.LoadFor(csx));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---------- TrustStore ----------

    [Fact]
    public void Plugin_no_es_confiable_por_defecto()
    {
        var dir = TempDir();
        try
        {
            var csx = Path.Combine(dir, "p.csx");
            File.WriteAllText(csx, "// nada");
            var store = new TrustStore(dir);
            Assert.False(store.IsTrusted(csx));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Confiar_y_revocar_persisten_en_trusted_json()
    {
        var dir = TempDir();
        try
        {
            var csx = Path.Combine(dir, "p.csx");
            File.WriteAllText(csx, "// v1");

            var s1 = new TrustStore(dir);
            s1.Trust(csx);
            Assert.True(s1.IsTrusted(csx));
            Assert.True(File.Exists(Path.Combine(dir, ".trusted.json")));

            // Nueva instancia, debe reconocer la confianza persistida
            var s2 = new TrustStore(dir);
            Assert.True(s2.IsTrusted(csx));

            s2.Revoke(csx);
            Assert.False(s2.IsTrusted(csx));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Cambiar_contenido_del_csx_invalida_la_confianza()
    {
        var dir = TempDir();
        try
        {
            var csx = Path.Combine(dir, "p.csx");
            File.WriteAllText(csx, "// v1");
            var store = new TrustStore(dir);
            store.Trust(csx);
            Assert.True(store.IsTrusted(csx));

            // Cambia el contenido — el hash deja de coincidir
            File.WriteAllText(csx, "// v2 (modificado)");
            Assert.False(store.IsTrusted(csx));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---------- PluginHost integración ----------

    [Fact]
    public async System.Threading.Tasks.Task LoadAllAsync_carga_plugins_con_y_sin_manifest()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "plain.csx"), "Log(\"hola\");");
            File.WriteAllText(Path.Combine(dir, "withmeta.csx"), "Log(\"hola\");");
            File.WriteAllText(Path.Combine(dir, "withmeta.csx.manifest.json"),
                "{\"name\":\"Con manifest\",\"author\":\"Test\",\"version\":\"0.1\"}",
                Encoding.UTF8);

            var host = new PluginHost(dir);
            await host.LoadAllAsync();

            Assert.Equal(2, host.Loaded.Count);
            var withmeta = host.Loaded.First(p => p.FileName == "withmeta.csx");
            Assert.NotNull(withmeta.Manifest);
            Assert.Equal("Con manifest", withmeta.Name);

            var plain = host.Loaded.First(p => p.FileName == "plain.csx");
            Assert.Null(plain.Manifest);
            Assert.Equal("plain.csx", plain.Name);

            // Ninguno es confiable por default → hooks no corren
            Assert.False(plain.Trusted);
            Assert.False(withmeta.Trusted);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async System.Threading.Tasks.Task RunHookAsync_omite_plugins_no_confiables()
    {
        var dir = TempDir();
        try
        {
            // Plugin que llamaría Log("ejecutado") si corriera
            File.WriteAllText(Path.Combine(dir, "p.csx"), "Log(\"ejecutado\");");
            var host = new PluginHost(dir);
            await host.LoadAllAsync();

            var captured = new System.Collections.Generic.List<string>();
            var ctx = new PluginContext { Sistema = new LosasPlus.Models.Sistema(), HostLog = captured.Add };
            await host.RunHookAsync("load", ctx, captured.Add);

            Assert.Contains(captured, m => m.Contains("omitido", System.StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(captured, m => m.Contains("ejecutado", System.StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async System.Threading.Tasks.Task RunHookAsync_ejecuta_plugins_confiables()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "p.csx"), "Log(\"ejecutado\");");
            var host = new PluginHost(dir);
            host.Trust.Trust(Path.Combine(dir, "p.csx"));
            await host.LoadAllAsync();

            var captured = new System.Collections.Generic.List<string>();
            var ctx = new PluginContext { Sistema = new LosasPlus.Models.Sistema(), HostLog = captured.Add };
            await host.RunHookAsync("load", ctx, captured.Add);

            Assert.Contains(captured, m => m.Contains("ejecutado"));
            Assert.Contains(captured, m => m.Contains("OK"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async System.Threading.Tasks.Task TrustPlugin_actualiza_estado_en_memoria()
    {
        var dir = TempDir();
        try
        {
            var csx = Path.Combine(dir, "p.csx");
            File.WriteAllText(csx, "Log(\"x\");");
            var host = new PluginHost(dir);
            await host.LoadAllAsync();

            Assert.False(host.Loaded[0].Trusted);
            host.TrustPlugin(csx);
            Assert.True(host.Loaded[0].Trusted);
            host.RevokeTrust(csx);
            Assert.False(host.Loaded[0].Trusted);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async System.Threading.Tasks.Task Plugin_con_error_de_compilacion_se_carga_pero_no_corre()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "bad.csx"), "this is not C# code &&&");
            var host = new PluginHost(dir);
            await host.LoadAllAsync();

            Assert.Single(host.Loaded);
            Assert.NotNull(host.Loaded[0].CompileError);
            Assert.Null(host.Loaded[0].Script);
        }
        finally { Directory.Delete(dir, true); }
    }
}
