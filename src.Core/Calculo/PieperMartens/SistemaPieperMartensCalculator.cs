using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LosasPlus.Calculo;
using LosasPlus.Models;

namespace LosasPlus.Calculo.PieperMartens;

/// <summary>
/// Orquestador del motor nativo Pieper-Martens: toma un <see cref="Sistema"/>
/// (modelo del .DL) y produce un <see cref="SalidaPerdomo"/> con momentos,
/// balanceo de apoyos y armaduras de vano y apoyo. Reemplaza a <c>Losas.exe</c>
/// como fuente del <see cref="SalidaPerdomo"/> que consumen la Memoria y la UI.
/// Reusa <see cref="MomentosCalculator"/>, <see cref="BalanceoMomentos"/> y
/// <see cref="AcerosLosaDesigner"/>.
/// </summary>
public sealed class SistemaPieperMartensCalculator
{
    private readonly MomentosCalculator _momentos;
    private readonly BalanceoMomentos _balanceo;

    public SistemaPieperMartensCalculator(TablaPieperMartens tabla)
    {
        _momentos = new MomentosCalculator(tabla);
        _balanceo = new BalanceoMomentos();
    }

    /// <summary>Crea un calculador con las tablas embebidas cargadas.</summary>
    public static SistemaPieperMartensCalculator Crear() => new(TablaPieperMartens.Cargar());

    /// <summary>Calcula el sistema completo y ensambla el <see cref="SalidaPerdomo"/>.</summary>
    public SalidaPerdomo Calcular(Sistema sistema)
    {
        if (sistema is null) throw new ArgumentNullException(nameof(sistema));

        double fc = sistema.Fc, fy = sistema.Fy;
        var salida = new SalidaPerdomo { VersionMotor = "Pieper-Martens nativo" };

        // 1. Momentos por losa → filas MomentoLosa.
        var momentosPorLosa = new Dictionary<int, MomentosLosa>();
        var filaPorLosa = new Dictionary<int, MomentoLosa>();
        foreach (var losa in sistema.Losas)
        {
            MomentosLosa m;
            try { m = _momentos.Calcular(losa); }
            catch (Exception ex)
            {
                // Tipo sin mapear u otro error de la losa: registrar y continuar —
                // una losa no aborta el sistema (mismo patrón por-losa que
                // MotorFeaService.CalcularSistemaConMotorAsync).
                System.Diagnostics.Debug.WriteLine(
                    $"[PieperMartens] Losa {losa.Id} (tipo {losa.Tipo}) omitida: {ex.Message}");
                salida.LosasNoParseadas.Add(losa.Id);
                continue;
            }
            momentosPorLosa[losa.Id] = m;
            var fila = new MomentoLosa(losa.Id, losa.Tipo, losa.Carga, losa.Espesor,
                losa.Lx, losa.Ly, m.Mfx, m.Mfy, m.Msx, m.Msy);
            filaPorLosa[losa.Id] = fila;
            salida.Momentos.Add(fila);
        }

        // 2. Armaduras de vano (X/Y centro), reusando AcerosLosaDesigner.
        // En losas de voladizo (una dirección), la dirección del vuelo no lleva
        // acero de vano (solo el de apoyo); la dirección soportada lleva el de
        // distribución. Se anula el centro de la dirección que vuela.
        foreach (var losa in sistema.Losas)
        {
            if (!filaPorLosa.TryGetValue(losa.Id, out var fila)) continue; // losa omitida en el paso 1
            var d = AcerosLosaDesigner.DisenarLosa(fila, fc, fy, losa.Rec * 100.0);
            bool voladizo = MomentosCalculator.EsVoladizo(losa.Tipo, out bool vuelaSegunY);

            salida.ArmadurasXCentro.Add(voladizo && !vuelaSegunY
                ? SinArmadura(losa.Id, d.XCentro.DCm)
                : AArmadura(losa.Id, d.XCentro));
            salida.ArmadurasYCentro.Add(voladizo && vuelaSegunY
                ? SinArmadura(losa.Id, d.YCentro.DCm)
                : AArmadura(losa.Id, d.YCentro));
        }

        // 3. Armaduras sobre apoyos (X/Y), con balanceo de bordes compartidos.
        AgregarApoyos(sistema, sistema.BordesX, DireccionApoyo.X, momentosPorLosa, salida.ArmadurasXApoyos, fc, fy);
        AgregarApoyos(sistema, sistema.BordesY, DireccionApoyo.Y, momentosPorLosa, salida.ArmadurasYApoyos, fc, fy);

        return salida;
    }

    /// <summary>
    /// Calcula el sistema y <b>aplica</b> el resultado al modelo: asigna
    /// <see cref="Sistema.SalidaPerdomo"/> y refleja los momentos en cada
    /// <see cref="Losa"/> (<c>Mfx/Mfy/MSx/MSy</c>), de modo que las vistas
    /// existentes (pestaña Aceros, Memoria) muestren el resultado igual que al
    /// importar el <c>.TXT</c> de <c>Losas.exe</c>.
    /// </summary>
    public SalidaPerdomo CalcularYAplicar(Sistema sistema)
    {
        if (sistema is null) throw new ArgumentNullException(nameof(sistema));

        var salida = Calcular(sistema);
        sistema.SalidaPerdomo = salida;

        var porId = new Dictionary<int, Losa>();
        foreach (var l in sistema.Losas) porId[l.Id] = l;
        foreach (var m in salida.Momentos)
        {
            if (!porId.TryGetValue(m.LosaId, out var losa)) continue;
            losa.Mfx = m.Mfx;
            losa.Mfy = m.Mfy;
            losa.MSx = m.NMSx;
            losa.MSy = m.NMSy;
        }
        return salida;
    }

    private void AgregarApoyos(
        Sistema sistema, IEnumerable<BordeAdic> bordes, DireccionApoyo direccion,
        IReadOnlyDictionary<int, MomentosLosa> momentos,
        ObservableCollection<ArmaduraApoyo> destino, double fc, double fy)
    {
        foreach (var b in bordes)
        {
            if (!momentos.ContainsKey(b.BI) || !momentos.ContainsKey(b.BJ)) continue; // borde de losa omitida
            var ap = _balanceo.Balancear(b.BI, b.BJ, direccion, b.Balanceo, momentos);

            var losaI = BuscarLosa(sistema, b.BI);
            double hCm = losaI.Espesor * 100.0;
            double db = AcerosLosaDesigner.DiametroBarraCm(4);
            int capa = direccion == DireccionApoyo.X ? 0 : 1;   // Y apila sobre X
            double dCm = AcerosLosaDesigner.CantoUtil(hCm, losaI.Rec * 100.0, db, capa);

            var fr = AcerosLosaDesigner.DisenarFranja($"{direccion} apoyo", ap.MuIJ, hCm, dCm, fc, fy);
            destino.Add(new ArmaduraApoyo(
                b.BI, b.BJ, ap.MuI, ap.MuJ, ap.MuIJ,
                dCm / 100.0, fr.AsDisenoCm2M, 0.0, 0.0, fr.AsDisenoCm2M, fr.Disponer));
        }
    }

    private static Losa BuscarLosa(Sistema sistema, int id)
    {
        foreach (var l in sistema.Losas)
            if (l.Id == id) return l;
        throw new ArgumentException($"El borde referencia la losa {id}, que no existe en el sistema.", nameof(sistema));
    }

    private static ArmaduraLosa AArmadura(int losaId, DisenoAceroFranja f)
        => new(losaId, f.DCm / 100.0, f.MuTonM, f.AsDisenoCm2M, f.Disponer, f.AsProvistaCm2M);

    /// <summary>Fila de vano sin acero (dirección del voladizo): As = 0, sin barra.</summary>
    private static ArmaduraLosa SinArmadura(int losaId, double dCm)
        => new(losaId, dCm / 100.0, 0.0, 0.0, "", 0.0);
}
