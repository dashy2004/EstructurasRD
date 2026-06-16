"""Tests de C2: cargas de losa → cargas nodales equivalentes en la malla FEA.

Convierte el reparto losa→bordes (Rebanada C) en `CargaNodal` colgadas de los
nodos de columna a la cota del nivel. 50/50 por borde, gravedad fz<0, kN→N (×1000),
casos D (muerta)/L (viva).
"""
import pytest

from motor_fea.edificio.modelo import (
    CargasLosa, Columna, Edificio, Losa, Nivel, Zapata,
)
from motor_fea.edificio.sintesis import cargas_de_losas, sintetizar


def _edificio_losa(puntos, muerta=0.0, viva=0.0, columnas_en=None):
    """Edificio de 1 vano: losa en N2 (cota 3) sobre columnas 0→3 (con zapata, para
    que el modelo quede apoyado y analizable) en `columnas_en` (por defecto, una en
    cada esquina de la losa)."""
    cols = list(columnas_en) if columnas_en is not None else list(puntos)
    losa = Losa(id=1, tipo="maciza", espesor=0.20, puntos=tuple(puntos),
                cargas=CargasLosa(muerta=muerta, viva=viva))
    verticales = [
        Columna(id=i + 1, posicion=(x, y), base=0.30, peralte=0.30,
                cota_base=0.0, cota_tope=3.0, material="H210",
                zapata=Zapata(1.2, 1.2, 0.4))
        for i, (x, y) in enumerate(cols)
    ]
    return Edificio(
        id=1, nombre="T",
        niveles=[Nivel(1, "N1", 0.0), Nivel(2, "N2", 3.0, (losa,))],
        elementos_verticales=verticales,
    )


def test_pano_cuadrado_carga_cada_esquina_por_igual():
    edi = _edificio_losa(((0, 0), (5, 0), (5, 5), (0, 5)), muerta=10)
    m = sintetizar(edi)
    cargas = cargas_de_losas(edi, m)
    d = [c for c in cargas if c.caso == "D"]
    assert len(d) == 4
    for c in d:
        assert c.fz == pytest.approx(-62500.0)   # 2 bordes × 62.5/2 kN × 1000
    assert sum(c.fz for c in d) == pytest.approx(-250000.0)


def test_muerta_y_viva_en_casos_separados():
    edi = _edificio_losa(((0, 0), (5, 0), (5, 5), (0, 5)), muerta=10, viva=4)
    m = sintetizar(edi)
    cargas = cargas_de_losas(edi, m)
    sD = sum(c.fz for c in cargas if c.caso == "D")
    sL = sum(c.fz for c in cargas if c.caso == "L")
    assert sD == pytest.approx(-250000.0)        # 10·25·1000
    assert sL == pytest.approx(-100000.0)        # 4·25·1000


def test_conservacion_rectangular():
    edi = _edificio_losa(((0, 0), (4, 0), (4, 8), (0, 8)), muerta=10)
    m = sintetizar(edi)
    cargas = cargas_de_losas(edi, m)
    d = [c for c in cargas if c.caso == "D"]
    assert sum(c.fz for c in d) == pytest.approx(-320000.0)   # q·A·1000
    for c in d:
        assert c.fz == pytest.approx(-80000.0)   # corto 40/2 + largo 120/2, ×1000


def test_esquina_sin_columna_falla_fuerte():
    # Losa 5×5 pero solo 2 columnas → esquinas (5,5)/(0,5) sin nodo.
    edi = _edificio_losa(((0, 0), (5, 0), (5, 5), (0, 5)), muerta=10,
                         columnas_en=[(0, 0), (5, 0)])
    m = sintetizar(edi)
    with pytest.raises(ValueError):
        cargas_de_losas(edi, m)


def test_determinismo():
    edi = _edificio_losa(((0, 0), (5, 0), (5, 5), (0, 5)), muerta=10, viva=4)
    m = sintetizar(edi)
    a = [(c.nodo_id, c.fz, c.caso) for c in cargas_de_losas(edi, m)]
    b = [(c.nodo_id, c.fz, c.caso) for c in cargas_de_losas(edi, m)]
    assert a == b


def test_integracion_modelo_cargado_valido_y_analizable():
    from motor_fea.core.casos import esfuerzos_por_caso

    edi = _edificio_losa(((0, 0), (5, 0), (5, 5), (0, 5)), muerta=10, viva=4)
    m = sintetizar(edi)
    m.cargas.extend(cargas_de_losas(edi, m))
    assert m.validar() == []
    por_caso = esfuerzos_por_caso(m)
    assert set(por_caso.keys()) == {"D", "L"}
