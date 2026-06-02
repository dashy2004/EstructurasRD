"""Constructores de modelos de referencia compartidos por los tests.

No es un módulo de test (no empieza con ``test_``): pytest no lo colecta, pero
es importable como ``modelos_ref`` por estar en el directorio de tests. Evita el
anti-patrón de importar un módulo de test desde otro.
"""
from motor_fea.core.modelo import (
    Apoyo,
    ElementoFrame,
    Material,
    ModeloEstructural,
    Nodo,
    Seccion,
)

# Sección cuadrada 0.30×0.30, hormigón ~SI.
E = 2.0e10
NU = 0.2
A = 0.09
I = 0.30**4 / 12        # Iy = Iz
J = 0.1406 * 0.30**4
L = 3.0
M_PUNTA = 1000.0        # kg en la punta del voladizo

# Cadena de 2 masas-resorte.
KAXIAL = 1.0e6          # EA/L por barra (N/m)
M_MASA = 1000.0         # kg por masa


def voladizo() -> ModeloEstructural:
    """Voladizo a lo largo de X (nodo 1 empotrado, nodo 2 libre), sin masa."""
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, L, 0, 0)]
    m.materiales.append(Material(1, E=E, nu=NU))
    m.secciones.append(Seccion(1, area=A, inercia_y=I, inercia_z=I, constante_torsion=J))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    return m


def cadena_2gdl() -> ModeloEstructural:
    """node0 fijo — k — m1 — k — m2 (sólo ux libre en 1 y 2)."""
    mod = ModeloEstructural()
    mod.nodos += [Nodo(0, 0, 0, 0), Nodo(1, 1.0, 0, 0), Nodo(2, 2.0, 0, 0)]
    mod.materiales.append(Material(1, E=2.0e10, nu=0.2))
    a = KAXIAL * 1.0 / 2.0e10                       # EA/L = KAXIAL con L=1
    mod.secciones.append(Seccion(1, area=a, inercia_y=1e-8, inercia_z=1e-8, constante_torsion=1e-8))
    mod.elementos += [ElementoFrame(1, 0, 1, 1, 1), ElementoFrame(2, 1, 2, 1, 1)]
    mod.apoyos.append(Apoyo.empotrado(0))
    mod.apoyos += [Apoyo(1, False, True, True, True, True, True),
                   Apoyo(2, False, True, True, True, True, True)]
    return mod
