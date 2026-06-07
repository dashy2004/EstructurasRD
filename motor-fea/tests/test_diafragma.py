"""Validación del diafragma rígido en plano (constraint master-slave).

Modelo: dos columnas idénticas en voladizo (empotradas en la base, verticales
a lo largo de Z), con los topes ligados por un diafragma rígido. Una carga
lateral en el maestro debe (a) mover ambos topes lo mismo en X (cinemática
rígida, dy=0) y (b) repartirse entre las dos columnas → la mitad de la deflexión
que tendría una sola columna cargada.
"""
import math

from motor_fea.core.diafragma import Diafragma, resolver_con_diafragma
from motor_fea.core.modelo import (
    Apoyo,
    CargaNodal,
    ElementoFrame,
    Material,
    ModeloEstructural,
    Nodo,
    Seccion,
)
from motor_fea.core.solver import resolver

E, NU = 2.0e10, 0.2
A = 0.09
I = 0.30**4 / 12
J = 0.1406 * 0.30**4
H = 3.0
P = 10000.0


def _dos_columnas() -> ModeloEstructural:
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, H), Nodo(3, 4, 0, 0), Nodo(4, 4, 0, H)]
    m.materiales.append(Material(1, E=E, nu=NU))
    m.secciones.append(Seccion(1, area=A, inercia_y=I, inercia_z=I, constante_torsion=J))
    m.elementos += [ElementoFrame(1, 1, 2, 1, 1), ElementoFrame(2, 3, 4, 1, 1)]   # columnas verticales
    m.apoyos += [Apoyo.empotrado(1), Apoyo.empotrado(3)]
    m.cargas.append(CargaNodal(2, fx=P))                                          # carga lateral en el maestro
    return m


def test_diafragma_liga_los_topes_en_x():
    r = resolver_con_diafragma(_dos_columnas(), [Diafragma(maestro=2, esclavos=(4,))])
    ux2 = r.desplazamientos[2][0]
    ux4 = r.desplazamientos[4][0]
    assert ux2 > 0
    assert abs(ux2 - ux4) / ux2 < 1e-9          # cinemática rígida: mismos ux (dy=0)


def test_diafragma_reparte_la_carga_y_equilibra():
    r = resolver_con_diafragma(_dos_columnas(), [Diafragma(maestro=2, esclavos=(4,))])
    # Equilibrio: las reacciones X en las dos bases suman −P.
    rx = r.reacciones[1][0] + r.reacciones[3][0]
    assert abs(rx + P) / P < 1e-6
    # Columnas idénticas → cada base toma la mitad.
    assert abs(r.reacciones[1][0] - r.reacciones[3][0]) / abs(r.reacciones[1][0]) < 1e-6


def test_diafragma_reduce_la_deflexion_a_la_mitad():
    # Sin diafragma, la carga en el nodo 2 sólo mueve su columna.
    solo = resolver(_dos_columnas())
    ux2_solo = solo.desplazamientos[2][0]
    con = resolver_con_diafragma(_dos_columnas(), [Diafragma(maestro=2, esclavos=(4,))])
    ux2_diaf = con.desplazamientos[2][0]
    # Con diafragma, dos columnas idénticas comparten la carga → la mitad de ux.
    assert abs(ux2_diaf - 0.5 * ux2_solo) / (0.5 * ux2_solo) < 1e-6
