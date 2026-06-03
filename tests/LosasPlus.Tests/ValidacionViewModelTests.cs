using System.Linq;
using LosasPlus.Models;
using LosasPlus.Validation;
using Shared.UI.ViewModels;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="ValidacionViewModel"/> centrados en el modo detalle,
/// auto-fix "Aplicar mínimo R-001" y el contador per-sistema usado por el
/// banner de NivelesView.
/// </summary>
public class ValidacionViewModelTests
{
    private static Proyecto ProyectoConEspesorBajo()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Uso = "Residencial";
        var sistema = new Sistema { Nombre = "E1", Uso = SistemaUso.Entrepiso };
        sistema.Losas.Add(new Losa { Id = 1, Tipo = 10, Lx = 4.0, Ly = 4.0, Espesor = 0.08 });
        p.Sistemas.Add(sistema);
        return p;
    }

    [Fact]
    public void Default_arranca_en_modo_lista()
    {
        var vm = new ValidacionViewModel();
        Assert.True(vm.EnModoLista);
        Assert.False(vm.EnModoDetalle);
        Assert.Null(vm.IssueSeleccionado);
    }

    [Fact]
    public void SeleccionarIssue_cambia_a_modo_detalle()
    {
        var vm = new ValidacionViewModel();
        vm.RevalidarPara(ProyectoConEspesorBajo());
        var issue = vm.Issues.First();
        vm.SeleccionarIssueCommand.Execute(issue);
        Assert.True(vm.EnModoDetalle);
        Assert.Equal(issue, vm.IssueSeleccionado);
    }

    [Fact]
    public void VolverALaLista_limpia_seleccion()
    {
        var vm = new ValidacionViewModel();
        vm.RevalidarPara(ProyectoConEspesorBajo());
        vm.SeleccionarIssueCommand.Execute(vm.Issues.First());
        vm.VolverALaListaCommand.Execute(null);
        Assert.True(vm.EnModoLista);
        Assert.Null(vm.IssueSeleccionado);
    }

    [Fact]
    public void CerrarPanel_tambien_limpia_seleccion()
    {
        var vm = new ValidacionViewModel();
        vm.RevalidarPara(ProyectoConEspesorBajo());
        vm.PanelAbierto = true;
        vm.SeleccionarIssueCommand.Execute(vm.Issues.First());
        vm.CerrarPanelCommand.Execute(null);
        Assert.False(vm.PanelAbierto);
        Assert.Null(vm.IssueSeleccionado);
    }

    [Fact]
    public void IssueSeleccionadoTieneAutoFix_true_para_espesor_R001()
    {
        var vm = new ValidacionViewModel();
        vm.RevalidarPara(ProyectoConEspesorBajo());
        vm.IssueSeleccionado = vm.Issues.First(i => i.Codigo.StartsWith("R-001-3.2.1-espesor"));
        Assert.True(vm.IssueSeleccionadoTieneAutoFix);
        Assert.True(vm.AplicarMinimoCommand.CanExecute(null));
    }

    [Fact]
    public void IssueSeleccionadoTieneAutoFix_false_para_aspecto()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Uso = "Residencial";
        var s = new Sistema { Nombre = "E1", Uso = SistemaUso.Entrepiso };
        s.Losas.Add(new Losa { Id = 1, Tipo = 10, Lx = 2.0, Ly = 5.0 });  // aspecto fuera de rango
        p.Sistemas.Add(s);
        var vm = new ValidacionViewModel();
        vm.RevalidarPara(p);
        var aspecto = vm.Issues.FirstOrDefault(i => i.Codigo.StartsWith("PM-aspecto"));
        Assert.NotNull(aspecto);
        vm.IssueSeleccionado = aspecto;
        Assert.False(vm.IssueSeleccionadoTieneAutoFix);
        Assert.False(vm.AplicarMinimoCommand.CanExecute(null));
    }

    [Fact]
    public void AplicarMinimo_corrige_espesor_y_elimina_issue()
    {
        var p = ProyectoConEspesorBajo();
        var vm = new ValidacionViewModel();
        vm.RevalidarPara(p);
        var antesErrores = vm.TotalErrores;
        Assert.True(antesErrores >= 1);

        var issue = vm.Issues.First(i => i.Codigo.StartsWith("R-001-3.2.1-espesor"));
        vm.IssueSeleccionado = issue;
        vm.AplicarMinimoCommand.Execute(null);

        Assert.Equal(0.10, p.Sistemas[0].Losas[0].Espesor);
        // Tras aplicar, el issue debe haber desaparecido (residencial pide 0.10 m).
        Assert.DoesNotContain(vm.Issues, i => i.Codigo.StartsWith("R-001-3.2.1-espesor"));
        // El VM vuelve automáticamente al modo lista.
        Assert.Null(vm.IssueSeleccionado);
    }

    [Fact]
    public void IssueSeleccionadoDeficit_devuelve_negativo_cuando_falta()
    {
        var vm = new ValidacionViewModel();
        vm.RevalidarPara(ProyectoConEspesorBajo());
        vm.IssueSeleccionado = vm.Issues.First();
        Assert.NotNull(vm.IssueSeleccionadoDeficit);
        Assert.True(vm.IssueSeleccionadoDeficit < 0);
    }

    [Fact]
    public void ContarIssuesDeSistema_filtra_por_nombre()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Uso = "Residencial";
        var e1 = new Sistema { Nombre = "E1", Uso = SistemaUso.Entrepiso };
        e1.Losas.Add(new Losa { Id = 1, Lx = 4.0, Ly = 4.0, Espesor = 0.08 });  // error
        p.Sistemas.Add(e1);
        var e2 = new Sistema { Nombre = "E2", Uso = SistemaUso.Entrepiso };
        e2.Losas.Add(new Losa { Id = 1, Lx = 4.0, Ly = 4.0, Espesor = 0.15 });  // OK
        p.Sistemas.Add(e2);
        var vm = new ValidacionViewModel();
        vm.RevalidarPara(p);

        Assert.True(vm.ContarIssuesDeSistema("E1") >= 1);
        Assert.Equal(0, vm.ContarIssuesDeSistema("E2"));
        Assert.Equal(0, vm.ContarIssuesDeSistema(null));
        Assert.Equal(0, vm.ContarIssuesDeSistema(""));
    }
}
