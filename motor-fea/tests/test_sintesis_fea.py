"""Tests de la síntesis FEA (Rebanada B0: columnas → malla)."""
import math

import pytest


def test_material_H210_a_modulo_elastico():
    from motor_fea.edificio.sintesis import material_a_E_pa

    # H210: f'c = 210 kg/cm²; E = 15100·√210 kg/cm² → Pa
    esperado = 15100.0 * math.sqrt(210.0) * 98066.5
    assert material_a_E_pa("H210") == pytest.approx(esperado, rel=1e-9)
    assert material_a_E_pa("h210") == pytest.approx(esperado, rel=1e-9)   # case-insensitive
    assert material_a_E_pa("H210") == pytest.approx(2.146e10, rel=1e-3)


def test_material_invalido_lanza_valueerror():
    from motor_fea.edificio.sintesis import material_a_E_pa

    with pytest.raises(ValueError, match="no reconocido"):
        material_a_E_pa("madera")
    with pytest.raises(ValueError, match="no reconocido"):
        material_a_E_pa("HXY")
    with pytest.raises(ValueError, match="positivo"):
        material_a_E_pa("H0")
