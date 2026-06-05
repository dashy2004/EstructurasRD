"""Tests puros del empaquetado de resultados de losa para el visor."""
import math

import pytest

from motor_fea.viz import resultados_losa


def _dto():
    return resultados_losa.calcular_resultados_losa(a=4.0, b=4.0, nx=4, ny=4)


def test_campos_y_unidades():
    dto = _dto()
    assert set(dto["campos"]) == {"deflexion", "momento_mx", "momento_my"}
    assert dto["campos"]["deflexion"]["unidad"] == "mm"
    assert dto["campos"]["momento_mx"]["unidad"] == "kN·m/m"
    assert dto["campos"]["momento_my"]["unidad"] == "kN·m/m"


def test_un_valor_por_nodo():
    dto = _dto()
    n = (4 + 1) * (4 + 1)
    for c in dto["campos"].values():
        assert len(c["valores"]) == n


def test_deflexion_central_positiva():
    dto = _dto()
    assert dto["campos"]["deflexion"]["valores"]["2,2"] > 0.0


def test_min_max_finitos_y_factor_sugerido_positivo():
    dto = _dto()
    for c in dto["campos"].values():
        assert math.isfinite(c["min"]) and math.isfinite(c["max"])
        assert c["min"] <= c["max"]
    assert dto["factor_sugerido"] > 0.0 and math.isfinite(dto["factor_sugerido"])


def test_momento_interior_no_nulo():
    dto = _dto()
    assert dto["campos"]["momento_mx"]["valores"]["2,2"] != 0.0
    assert dto["campos"]["momento_my"]["valores"]["2,2"] != 0.0


def test_parametros_invalidos_lanzan_valueerror():
    with pytest.raises(ValueError):
        resultados_losa.calcular_resultados_losa(nx=0)
    with pytest.raises(ValueError):
        resultados_losa.calcular_resultados_losa(t=0.0)
    with pytest.raises(ValueError):
        resultados_losa.calcular_resultados_losa(borde="otro")


def test_factor_sugerido_fallback_sin_flexion():
    # losa 1×1 con un solo elemento: todos los nodos están en el borde (w=0),
    # así que max|w|=0 y el factor cae al fallback 1.0 (sin dividir por cero).
    dto = resultados_losa.calcular_resultados_losa(a=1.0, b=1.0, nx=1, ny=1)
    assert dto["factor_sugerido"] == 1.0
