"""Validación del análisis modal contra la forma cerrada voladizo + masa en punta.

Para un voladizo de masa despreciable con masa ``m`` en la punta, el primer modo
es lateral con rigidez de punta ``k = 3EI/L³`` (Euler-Bernoulli), así que
``ω = √(k/m)`` y ``T = 2π/ω``. La condensación de Guyan del GDL rotacional
reproduce ``3EI/L³`` exactamente → error ~precisión de máquina.
"""
import math

from motor_fea.core.modal import periodo_fundamental
from motor_fea.core.modelo import (
    Apoyo,
    ElementoFrame,
    Material,
    ModeloEstructural,
    Nodo,
    Seccion,
)

E = 2.0e10
NU = 0.2
I = 0.30**4 / 12        # Iy = Iz
A = 0.09
J = 0.1406 * 0.30**4
L = 3.0
M = 1000.0              # kg en la punta


def _voladizo() -> ModeloEstructural:
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, L, 0, 0)]
    m.materiales.append(Material(1, E=E, nu=NU))
    m.secciones.append(Seccion(1, area=A, inercia_y=I, inercia_z=I, constante_torsion=J))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    return m


def test_periodo_fundamental_voladizo_masa_punta():
    r = periodo_fundamental(_voladizo(), masas={2: M})
    k = 3 * E * I / L**3                 # rigidez de punta lateral
    omega_ref = math.sqrt(k / M)
    assert abs(r.omega - omega_ref) / omega_ref < 1e-6
    assert abs(r.periodo - 2 * math.pi / omega_ref) / (2 * math.pi / omega_ref) < 1e-6
    assert abs(r.frecuencia - r.omega / (2 * math.pi)) < 1e-9


def test_periodo_crece_con_la_masa():
    r1 = periodo_fundamental(_voladizo(), masas={2: M})
    r4 = periodo_fundamental(_voladizo(), masas={2: 4 * M})
    # T ∝ √m → cuadruplicar la masa duplica el período.
    assert abs(r4.periodo / r1.periodo - 2.0) < 1e-6


def test_sin_masa_lanza_error():
    try:
        periodo_fundamental(_voladizo(), masas={2: 0.0})
    except ValueError:
        return
    raise AssertionError("Debió lanzar ValueError sin masas positivas.")


def test_forma_modal_tiene_el_nodo_con_masa():
    r = periodo_fundamental(_voladizo(), masas={2: M})
    assert 2 in r.forma
    # El modo lateral mueve uy o uz (no el axial ux).
    ux, uy, uz = r.forma[2]
    assert max(abs(uy), abs(uz)) > abs(ux)
