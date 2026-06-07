"""Validación del análisis modal contra la forma cerrada voladizo + masa en punta.

Para un voladizo de masa despreciable con masa ``m`` en la punta, el primer modo
es lateral con rigidez de punta ``k = 3EI/L³`` (Euler-Bernoulli), así que
``ω = √(k/m)`` y ``T = 2π/ω``. La condensación de Guyan del GDL rotacional
reproduce ``3EI/L³`` exactamente → error ~precisión de máquina.
"""
import math

from modelos_ref import KAXIAL, L, voladizo, cadena_2gdl
from modelos_ref import E, I
from modelos_ref import M_PUNTA as M
from modelos_ref import M_MASA as MM
from motor_fea.core.modal import modos, participacion_modal, periodo_fundamental


def test_periodo_fundamental_voladizo_masa_punta():
    r = periodo_fundamental(voladizo(), masas={2: M})
    k = 3 * E * I / L**3                 # rigidez de punta lateral
    omega_ref = math.sqrt(k / M)
    assert abs(r.omega - omega_ref) / omega_ref < 1e-6
    assert abs(r.periodo - 2 * math.pi / omega_ref) / (2 * math.pi / omega_ref) < 1e-6
    assert abs(r.frecuencia - r.omega / (2 * math.pi)) < 1e-9


def test_periodo_crece_con_la_masa():
    r1 = periodo_fundamental(voladizo(), masas={2: M})
    r4 = periodo_fundamental(voladizo(), masas={2: 4 * M})
    # T ∝ √m → cuadruplicar la masa duplica el período.
    assert abs(r4.periodo / r1.periodo - 2.0) < 1e-6


def test_sin_masa_lanza_error():
    try:
        periodo_fundamental(voladizo(), masas={2: 0.0})
    except ValueError:
        return
    raise AssertionError("Debió lanzar ValueError sin masas positivas.")


def test_forma_modal_tiene_el_nodo_con_masa():
    r = periodo_fundamental(voladizo(), masas={2: M})
    assert 2 in r.forma
    # El modo lateral mueve uy o uz (no el axial ux).
    ux, uy, uz = r.forma[2]
    assert max(abs(uy), abs(uz)) > abs(ux)


# ---- Multi-modo: cadena de 2 masas-resorte (modos cerrados) ----
def test_dos_modos_cadena_coinciden_con_cerrado():
    ms = modos(cadena_2gdl(), masas={1: MM, 2: MM}, n_modos=2)
    assert len(ms) == 2
    kr = KAXIAL / MM
    w1 = math.sqrt(kr * (3 - math.sqrt(5)) / 2)   # ω² = (k/m)(3∓√5)/2
    w2 = math.sqrt(kr * (3 + math.sqrt(5)) / 2)
    assert abs(ms[0].omega - w1) / w1 < 1e-6
    assert abs(ms[1].omega - w2) / w2 < 1e-6
    assert ms[0].omega < ms[1].omega              # ordenados ascendentes


def test_participacion_modal_suma_la_masa_total():
    part = participacion_modal(cadena_2gdl(), masas={1: MM, 2: MM}, direccion="x", n_modos=2)
    suma_meff = sum(p.masa_efectiva for _, p in part)
    suma_frac = sum(p.participacion for _, p in part)
    assert abs(suma_meff - 2 * MM) / (2 * MM) < 1e-6      # captura el 100% de la masa
    assert abs(suma_frac - 1.0) < 1e-6
    # El primer modo domina (≈95% para masas/resortes iguales).
    assert part[0][1].participacion > part[1][1].participacion
    assert part[0][1].participacion > 0.9
