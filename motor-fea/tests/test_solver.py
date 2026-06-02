"""Validación del solver de rigidez directa contra soluciones cerradas exactas.

El elemento frame de 12 GDL (Hermitiano) reproduce exactamente la solución de
Euler-Bernoulli para cargas nodales, así que el error frente a las fórmulas
cerradas es de orden de la precisión de máquina (toleramos 1e-6 relativo).

Modelo base: voladizo de longitud L empotrado en el nodo 1, libre en el 2,
orientado a lo largo de X global con sección cuadrada 0.30×0.30.
"""
import math

from motor_fea.core.modelo import (
    Apoyo,
    CargaNodal,
    ElementoFrame,
    Material,
    ModeloEstructural,
    Nodo,
    Seccion,
)
from motor_fea.core.solver import resolver, rigidez_local

# Propiedades de referencia (SI).
E = 2.0e10           # Pa
NU = 0.2
G = E / (2 * (1 + NU))
B = 0.30
A = B * B            # 0.09 m²
I = B**4 / 12        # 6.75e-4 m⁴ (Iy = Iz, sección cuadrada)
J = 0.1406 * B**4    # ~constante de torsión de sección cuadrada
L = 3.0              # m
P = 1000.0           # N


def _voladizo(carga: CargaNodal) -> ModeloEstructural:
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0.0, 0.0, 0.0), Nodo(2, L, 0.0, 0.0)]
    m.materiales.append(Material(1, E=E, nu=NU))
    m.secciones.append(Seccion(1, area=A, inercia_y=I, inercia_z=I, constante_torsion=J))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas.append(carga)
    return m


def _rel(a: float, b: float) -> float:
    return abs(a - b) / abs(b)


def test_axial_PL_sobre_AE():
    r = resolver(_voladizo(CargaNodal(2, fx=P)))
    ux = r.desplazamientos[2][0]
    assert _rel(ux, P * L / (A * E)) < 1e-9


def test_voladizo_carga_Z_usa_Iy():
    r = resolver(_voladizo(CargaNodal(2, fz=P)))
    uz = r.desplazamientos[2][2]
    esperado = P * L**3 / (3 * E * I)            # δ = PL³/3EI
    assert _rel(uz, esperado) < 1e-9


def test_voladizo_carga_Y_usa_Iz():
    r = resolver(_voladizo(CargaNodal(2, fy=P)))
    uy = r.desplazamientos[2][1]
    assert _rel(uy, P * L**3 / (3 * E * I)) < 1e-9


def test_torsion_TL_sobre_GJ():
    r = resolver(_voladizo(CargaNodal(2, mx=P)))
    rx = r.desplazamientos[2][3]
    assert _rel(rx, P * L / (G * J)) < 1e-9


def test_giro_por_momento_extremo_My():
    r = resolver(_voladizo(CargaNodal(2, my=P)))
    ry = r.desplazamientos[2][4]
    assert _rel(ry, P * L / (E * I)) < 1e-9       # θ = ML/EI


def test_reaccion_equilibra_la_carga():
    r = resolver(_voladizo(CargaNodal(2, fz=P)))
    # La reacción vertical en el empotramiento equilibra la carga aplicada.
    rz_apoyo = r.reacciones[1][2]
    assert _rel(rz_apoyo, -P) < 1e-9
    # Momento de reacción My = -P·L (equilibra el momento de vuelco).
    my_apoyo = r.reacciones[1][4]
    assert _rel(my_apoyo, P * L) < 1e-6


def test_rigidez_local_es_simetrica():
    k = rigidez_local(E, G, A, I, I, J, L)
    for i in range(12):
        for j in range(12):
            assert math.isclose(k[i][j], k[j][i], rel_tol=1e-12, abs_tol=1e-9)


def test_dos_elementos_equivalen_a_uno():
    # Dividir el voladizo en 2 elementos debe dar la misma punta que 1 elemento.
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, L / 2, 0, 0), Nodo(3, L, 0, 0)]
    m.materiales.append(Material(1, E=E, nu=NU))
    m.secciones.append(Seccion(1, area=A, inercia_y=I, inercia_z=I, constante_torsion=J))
    m.elementos += [ElementoFrame(1, 1, 2, 1, 1), ElementoFrame(2, 2, 3, 1, 1)]
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas.append(CargaNodal(3, fz=P))
    r = resolver(m)
    assert _rel(r.desplazamientos[3][2], P * L**3 / (3 * E * I)) < 1e-9
