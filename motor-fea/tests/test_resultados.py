"""Tests puros del cálculo de resultados del visor (peso propio, deformada, modos)."""
import math

import pytest

import modelos_ref
from motor_fea.core.modelo import Apoyo, ElementoFrame, ModeloEstructural
from motor_fea.viz import resultados


def test_peso_propio_reparte_masa_mitad_a_cada_nodo():
    # voladizo: densidad 2400 · A 0.09 · L 3 = 648 kg → 324 kg en cada extremo.
    masa, cargas = resultados._peso_propio(modelos_ref.voladizo())
    assert masa[1] == pytest.approx(324.0)
    assert masa[2] == pytest.approx(324.0)
    # carga gravitatoria fz = -m·g/2 en cada nodo (signo negativo = hacia abajo).
    assert all(c.fz < 0 for c in cargas)


def test_deformada_punta_baja_por_peso_propio():
    res = resultados.calcular_resultados(modelos_ref.voladizo())
    uz_punta = res["deformada"]["desplazamientos"]["2"][2]
    assert uz_punta < 0.0


def test_modos_periodo_positivo_y_omega_ascendente():
    res = resultados.calcular_resultados(modelos_ref.voladizo())
    modos = res["modos"]
    assert len(modos) >= 1
    assert all(m["periodo"] > 0.0 for m in modos)
    omegas = [m["omega"] for m in modos]
    assert omegas == sorted(omegas)          # modal devuelve ω ascendente (modo 1 = fundamental)


def test_factor_sugerido_finito_y_positivo():
    res = resultados.calcular_resultados(modelos_ref.voladizo())
    fs_def = res["deformada"]["factor_sugerido"]
    assert fs_def > 0.0 and math.isfinite(fs_def)
    for m in res["modos"]:
        assert m["factor_sugerido"] > 0.0 and math.isfinite(m["factor_sugerido"])


def test_todo_empotrado_sin_modos_pero_con_deformada():
    m = modelos_ref.voladizo()
    m.apoyos.append(Apoyo.empotrado(2))      # ahora ambos nodos fijos
    res = resultados.calcular_resultados(m)
    assert res["modos"] == []
    assert "desplazamientos" in res["deformada"]


def test_modelo_invalido_lanza_valueerror():
    m = ModeloEstructural(elementos=[ElementoFrame(1, 1, 2, 1, 1)])  # refs inexistentes
    with pytest.raises(ValueError):
        resultados.calcular_resultados(m)


def test_masa_sismica_de_carga_vertical_se_suma():
    from motor_fea.core.modelo import CargaNodal
    m = modelos_ref.voladizo()
    m.cargas.append(CargaNodal(2, fz=-9810.0))   # |fz|/g = 1000 kg extra en el nodo 2
    masa, _ = resultados._peso_propio(m)
    resultados._agregar_masa_sismica(m, masa)
    # 324 (peso propio) + 1000 (sísmica) = 1324 kg en el nodo 2.
    assert masa[2] == pytest.approx(1324.0)
