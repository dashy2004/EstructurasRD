"""Capstone: flujo de edificio completo en un solo análisis.

Pórtico 3D (2 columnas + diafragma rígido) → estático lateral → modal → cortante
basal modal-espectral (R-001) → diseño de losa por FEM. Demuestra que todas las
capas del motor componen en un análisis de edificio coherente.
"""
import math

from motor_fea.core.diafragma import Diafragma, resolver_con_diafragma
from motor_fea.core.modal import modos
from motor_fea.core.modelo import (
    Apoyo, CargaNodal, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea import sismo
from motor_fea.diseno_losa import disenar_losa
from motor_fea.normativa.r001 import ZonaSismica

E, NU, A, I, J, H = 2.0e10, 0.2, 0.09, 0.30**4 / 12, 0.1406 * 0.30**4, 3.0


def _portico() -> ModeloEstructural:
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, H), Nodo(3, 4, 0, 0), Nodo(4, 4, 0, H)]
    m.materiales.append(Material(1, E=E, nu=NU))
    m.secciones.append(Seccion(1, area=A, inercia_y=I, inercia_z=I, constante_torsion=J))
    m.elementos += [ElementoFrame(1, 1, 2, 1, 1), ElementoFrame(2, 3, 4, 1, 1)]
    m.apoyos += [Apoyo.empotrado(1), Apoyo.empotrado(3)]
    return m


def test_flujo_edificio_estatico_modal_sismo_diseno():
    # 1) Estático lateral con diafragma rígido (los topes se mueven juntos).
    mod = _portico()
    mod.cargas.append(CargaNodal(2, fx=20000.0))
    est = resolver_con_diafragma(mod, [Diafragma(maestro=2, esclavos=(4,))])
    assert abs(est.desplazamientos[2][0] - est.desplazamientos[4][0]) / est.desplazamientos[2][0] < 1e-9

    # 2) Modal: masas en los topes → período fundamental.
    masas = {2: 5000.0, 4: 5000.0}
    ms = modos(_portico(), masas, n_modos=2)
    assert ms[0].periodo > 0 and ms[0].omega <= ms[1].omega

    # 3) Sismo: cortante basal modal-espectral (R-001 Zona I).
    sis = sismo.cortante_basal_modal_espectral(
        _portico(), masas, ZonaSismica.ZONA_I, fa=1.0, fv=1.5, rd=5.5, direccion="x", n_modos=2)
    assert sis.cortante_basal > 0
    assert sis.masa_participativa > 0

    # 4) Diseño: una losa del piso por FEM → acero (vano + apoyo).
    losa = disenar_losa(5.0, 5.0, 8, 8, E, NU, 0.20, 10000.0, fc_mpa=21.0, fy_mpa=420.0)
    assert losa.franja_x.armadura.cumple
    assert losa.franja_apoyo.as_diseno >= 0

    # Las cuatro capas produjeron resultados coherentes en un solo flujo.
    assert all(v > 0 for v in (est.desplazamientos[2][0], ms[0].periodo,
                               sis.cortante_basal, losa.franja_x.as_diseno))
