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


def _columna_3niveles(con_zapata=False):
    from motor_fea.edificio.modelo import Columna, Edificio, Nivel, Zapata
    col = Columna(id=1, posicion=(0.0, 0.0), base=0.30, peralte=0.30,
                  cota_base=0.0, cota_tope=6.0, material="H210",
                  zapata=Zapata(1.2, 1.2, 0.4) if con_zapata else None)
    edi = Edificio(id=1, nombre="Bloque A",
                   niveles=[Nivel(1, "N1", 0.0), Nivel(2, "N2", 3.0), Nivel(3, "N3", 6.0)],
                   elementos_verticales=[col])
    return edi


def test_columna_continua_genera_nodos_compartidos_y_barras():
    from motor_fea.edificio.sintesis import sintetizar

    m = sintetizar(_columna_3niveles())

    # 3 nodos en (0,0,z) para z = 0, 3, 6
    assert sorted(n.z for n in m.nodos) == [0.0, 3.0, 6.0]
    assert all((n.x, n.y) == (0.0, 0.0) for n in m.nodos)
    # 2 barras consecutivas que comparten el nodo intermedio (z=3)
    assert len(m.elementos) == 2
    z = {n.id: n.z for n in m.nodos}
    e1, e2 = m.elementos
    assert z[e1.nodo_j] == 3.0 and z[e2.nodo_i] == 3.0      # nodo z=3 compartido
    assert e1.nodo_j == e2.nodo_i
    # sin zapata → sin apoyos
    assert m.apoyos == []


def test_seccion_cuadrada_propiedades():
    from motor_fea.edificio.sintesis import sintetizar

    m = sintetizar(_columna_3niveles())
    assert len(m.secciones) == 1
    s = m.secciones[0]
    assert s.area == pytest.approx(0.30 * 0.30)
    assert s.inercia_y == pytest.approx(0.30**4 / 12)
    assert s.inercia_z == pytest.approx(0.30**4 / 12)
    assert s.constante_torsion == pytest.approx(0.1406 * 0.30**4, rel=1e-2)
    # un solo material, con el E de H210
    assert len(m.materiales) == 1
    assert m.materiales[0].E == pytest.approx(15100.0 * math.sqrt(210.0) * 98066.5)


def test_zapata_genera_empotramiento_en_base():
    from motor_fea.edificio.sintesis import sintetizar

    m = sintetizar(_columna_3niveles(con_zapata=True))
    assert len(m.apoyos) == 1
    z = {n.id: n.z for n in m.nodos}
    apoyo = m.apoyos[0]
    assert z[apoyo.nodo_id] == 0.0                       # apoyo en la base (cota_base)
    assert apoyo.restricciones() == (True,) * 6          # empotrado


def test_columnas_que_comparten_base_deduplican_nodo_y_apoyo():
    from motor_fea.edificio.modelo import Columna, Edificio, Nivel, Zapata
    from motor_fea.edificio.sintesis import sintetizar

    # dos columnas distintas con MISMA posición de base (apiladas en niveles distintos)
    inf = Columna(id=1, posicion=(2.0, 2.0), base=0.30, peralte=0.30,
                  cota_base=0.0, cota_tope=3.0, material="H210",
                  zapata=Zapata(1.0, 1.0, 0.4))
    sup = Columna(id=2, posicion=(2.0, 2.0), base=0.30, peralte=0.30,
                  cota_base=3.0, cota_tope=6.0, material="H210")
    edi = Edificio(id=1, nombre="A",
                   niveles=[Nivel(1, "N1", 0.0), Nivel(2, "N2", 3.0), Nivel(3, "N3", 6.0)],
                   elementos_verticales=[inf, sup])
    m = sintetizar(edi)

    # nodos en z=0,3,6 todos en (2,2): el nodo z=3 lo comparten ambas columnas
    assert sorted(n.z for n in m.nodos) == [0.0, 3.0, 6.0]
    assert len(m.elementos) == 2
    assert len(m.apoyos) == 1                            # solo la columna inferior tiene zapata


def test_losas_se_transportan_como_geometria_a_su_cota():
    from motor_fea.edificio.modelo import Columna, Edificio, Losa, Nivel
    from motor_fea.edificio.sintesis import sintetizar

    losa = Losa(id=1, tipo="maciza", espesor=0.20,
                puntos=((0, 0), (5, 0), (5, 5), (0, 5)))
    edi = Edificio(id=1, nombre="A",
                   niveles=[Nivel(1, "N1", 0.0),
                            Nivel(2, "N2", 3.0, (losa,))],   # losa en el nivel z=3
                   elementos_verticales=[
                       Columna(id=1, posicion=(0, 0), base=0.3, peralte=0.3,
                               cota_base=0.0, cota_tope=3.0, material="H210")])
    m = sintetizar(edi)

    assert len(m.losas) == 1
    pts = m.losas[0].puntos
    assert all(z == 3.0 for (_x, _y, z) in pts)          # elevada a la cota del nivel
    assert pts[0] == [0, 0, 3.0]
