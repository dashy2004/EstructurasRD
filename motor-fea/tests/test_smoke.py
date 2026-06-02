"""Smoke-tests de B0 — solo stdlib (no requieren NumPy/SciPy).

Validan que el dominio estructural y las constantes normativas se importan y se
comportan, fijando el contrato sobre el que B1 montará el solver.
"""
from motor_fea import __version__
from motor_fea.core import (
    Apoyo,
    CargaNodal,
    ElementoFrame,
    Material,
    ModeloEstructural,
    Nodo,
    Seccion,
)
from motor_fea.normativa import r001


def _voladizo() -> ModeloEstructural:
    """Una barra empotrada-libre con una carga en punta (modelo mínimo válido)."""
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0.0, 0.0, 0.0), Nodo(2, 3.0, 0.0, 0.0)]
    m.materiales.append(Material(1, E=2.0e10))           # ~hormigón, Pa
    m.secciones.append(Seccion(1, area=0.09, inercia_y=6.75e-4, inercia_z=6.75e-4, constante_torsion=1.0e-3))
    m.elementos.append(ElementoFrame(1, nodo_i=1, nodo_j=2, material_id=1, seccion_id=1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas.append(CargaNodal(2, fz=-1000.0))
    return m


def test_version_expuesta():
    assert __version__ == "0.0.1"


def test_modelo_valido_y_gdl():
    m = _voladizo()
    assert m.es_valido()
    assert m.validar() == []
    assert m.n_gdl == 12                 # 2 nodos × 6 GDL → consistente con frame 3D
    assert m.indice_nodos() == {1: 0, 2: 1}


def test_material_modulo_corte():
    mat = Material(1, E=2.0e10, nu=0.2)
    assert mat.G == 2.0e10 / (2.0 * 1.2)


def test_validacion_detecta_referencias_rotas():
    m = ModeloEstructural()
    m.nodos.append(Nodo(1, 0.0, 0.0))
    m.elementos.append(ElementoFrame(9, nodo_i=1, nodo_j=99, material_id=7, seccion_id=7))
    errores = m.validar()
    assert any("nodo_j=99" in e for e in errores)
    assert any("material_id=7" in e for e in errores)
    assert not m.es_valido()


def test_apoyo_empotrado_vs_articulado():
    assert Apoyo.empotrado(1).restricciones() == (True,) * 6
    assert Apoyo.articulado(1).restricciones() == (True, True, True, False, False, False)


def test_r001_constantes_zonas():
    assert r001.PARAMETROS_ZONA[r001.ZonaSismica.ZONA_I].ss == 1.55
    assert r001.PARAMETROS_ZONA[r001.ZonaSismica.ZONA_I].s1 == 0.75
    assert r001.PARAMETROS_ZONA[r001.ZonaSismica.ZONA_II].ss == 0.95


def test_r001_espectro_y_cortante_basal():
    esp = r001.espectro_diseno(r001.ZonaSismica.ZONA_I, fa=1.0, fv=1.5)
    # SDS = 2/3·1.0·1.55 ; SD1 = 2/3·1.5·0.75
    assert round(esp["SDS"], 4) == round((2 / 3) * 1.55, 4)
    assert round(esp["SD1"], 4) == round((2 / 3) * 1.5 * 0.75, 4)
    # Piso de cortante basal 0.03.
    assert r001.cortante_basal(u=1.0, sa=0.01, rd=5.5) == 0.03
    assert r001.cortante_basal(u=1.5, sa=0.5, rd=5.5) > 0.03
