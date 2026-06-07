"""Test de integración end-to-end: análisis → diseño → sismo en un solo flujo.

Demuestra que las tres capas componen: el resultado del análisis (reacción de
momento en la base) alimenta el diseño a flexión, y el mismo modelo alimenta el
cortante basal sísmico; además se ejercitan columna, zapata, losa y combos.
"""
import math

from modelos_ref import L, voladizo
from motor_fea import sismo
from motor_fea.core.modelo import CargaNodal
from motor_fea.core.solver import resolver
from motor_fea.normativa import aci318, combinaciones as cb
from motor_fea.normativa.r001 import ZonaSismica

P = 1000.0   # N


def test_analisis_alimenta_diseno_de_viga():
    # 1) Análisis: voladizo con carga en punta → reacción de momento en la base.
    modelo = voladizo()
    modelo.cargas.append(CargaNodal(2, fz=P))
    res = resolver(modelo)
    my_base = abs(res.reacciones[1][4])          # N·m
    assert abs(my_base - P * L) / (P * L) < 1e-9  # equilibrio: My = P·L

    # 2) Diseño: ese momento (N·m → N·mm) dimensiona una viga a flexión.
    mu = my_base * 1000.0
    d = aci318.verificar_viga_flexion(mu, b=300, d=540, fc=28, fy=420, as_provisto=1500.0)
    assert not d.seccion_insuficiente
    assert d.as_requerido > 0
    assert d.cumple                              # 1500 mm² cubre el momento de servicio


def test_sismo_desde_el_mismo_modelo():
    r = sismo.cortante_basal_sismico(voladizo(), masas={2: 1000.0},
                                     zona=ZonaSismica.ZONA_I, fa=1.0, fv=1.5, rd=5.5)
    assert r.periodo > 0 and r.cortante_basal > 0
    assert math.isclose(r.cortante_basal, r.cb * r.peso, rel_tol=1e-9)


def test_suite_de_diseno_completa_compone():
    # Viga (flexión + cortante), columna (P-M), zapata (punzonamiento), losa.
    viga_f = aci318.verificar_viga_flexion(200e6, 300, 540, 28, 420, as_provisto=1200.0)
    viga_v = aci318.verificar_viga_cortante(150e3, 300, 540, 28, av=142, fyt=420, s=150)
    col = aci318.punto_interaccion(200, 400, 400, 28, 420, [(60, 1020), (340, 1020)])
    zap = aci318.cortante_punzonamiento(1.5e6, 400, 400, 440, 28, "interior")
    losa = aci318.diseno_losa_franja(50e6, 1000, 200, 170, 21, 420)

    # Todos producen resultados coherentes (no NaN donde no corresponde).
    assert viga_f.ratio is not None and viga_f.ratio > 0
    assert viga_v.phi_vn > 0
    assert col.phi <= 0.90 and col.phi >= 0.65
    assert zap.bo > 0 and zap.phi_vc > 0
    assert losa.armadura.cumple


def test_envolvente_de_combinaciones_para_diseno():
    # La envolvente de combos entrega el Mu de diseño (máx) y revisa uplift (mín).
    env = cb.envolvente(D=30.0, L=20.0, Lr=5.0, W=15.0, E=25.0)
    assert env.maximo > 0
    assert env.maximo >= env.minimo
    # El máximo gobierna el diseño a flexión positiva.
    mu = env.maximo * 1e6      # kN·m → N·mm (escala ilustrativa)
    d = aci318.as_requerido_flexion(mu, 300, 540, 28, 420)
    assert d[0] > 0 and not d[1]
