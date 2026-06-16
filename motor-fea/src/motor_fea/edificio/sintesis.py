"""Síntesis FEA: modelo de autoría (Edificio) → malla estructural (ModeloEstructural).

Rebanada B0 — solo columnas: nodos compartidos por coordenada, barras entre
quiebres, zapata→apoyo, material/sección desde autoría, losas como geometría
inerte para el visor. Sin I/O, sin NumPy. Asume un edificio válido; garantiza
una salida que pasa ``ModeloEstructural.validar()``.
"""
from __future__ import annotations

import math

from motor_fea.core.modelo import (
    Apoyo,
    CargaNodal,
    ElementoFrame,
    LosaViz,
    Material,
    ModeloEstructural,
    Nodo,
    Seccion,
)
from motor_fea.edificio.cargas import repartir_losa
from motor_fea.edificio.modelo import Columna, Edificio

_TOL = 6  # decimales de cuantización de coordenadas (≈ mm)
_KN_A_N = 1000.0  # CargasLosa/reparto en kN; el core FEA es SI en newtons (E en Pa)

FACTOR_E_ACI = 15100.0     # E[kg/cm²] = 15100·√(f'c[kg/cm²])  (ACI 318, concreto)
KGF_CM2_A_PA = 98066.5     # 1 kgf/cm² en pascales


def material_a_E_pa(material: str) -> float:
    """Convierte un material de obra ``'H{n}'`` (f'c en kg/cm²) a E en pascales (ACI)."""
    s = material.strip().upper()
    fc = None
    if s.startswith("H"):
        try:
            fc = float(s[1:])
        except ValueError:
            fc = None
    if fc is None:
        raise ValueError(f"Material no reconocido: {material!r} (se espera 'H<f'c en kg/cm²>').")
    if fc <= 0:
        raise ValueError(f"Material {material!r}: f'c debe ser positivo.")
    return FACTOR_E_ACI * math.sqrt(fc) * KGF_CM2_A_PA


def _torsion_rectangular(base: float, peralte: float) -> float:
    """Constante de torsión J de una sección rectangular (β≈0.1406 para cuadrada)."""
    a = max(base, peralte)
    t = min(base, peralte)
    return a * t**3 * (1.0 / 3.0 - 0.21 * (t / a) * (1.0 - t**4 / (12.0 * a**4)))


def _propiedades_seccion(col: "Columna") -> tuple:
    """(area, inercia_y, inercia_z, J) de una columna rectangular base×peralte."""
    b, h = col.base, col.peralte
    return (b * h, h * b**3 / 12.0, b * h**3 / 12.0, _torsion_rectangular(b, h))


def _quiebres(col: "Columna", cotas_nivel: list) -> list:
    """Cotas Z donde la columna necesita un nodo: extremos + niveles intermedios."""
    qs = {round(col.cota_base, _TOL), round(col.cota_tope, _TOL)}
    for c in cotas_nivel:
        if col.cota_base < c < col.cota_tope:
            qs.add(round(c, _TOL))
    return sorted(qs)


def sintetizar(edificio: Edificio) -> ModeloEstructural:
    """Traduce un edificio autorado a una malla FEA (Rebanada B0: solo columnas)."""
    modelo = ModeloEstructural()
    cotas_nivel = [n.cota for n in edificio.niveles_ordenados()]

    nodos_por_coord: dict[tuple, int] = {}
    material_por_str: dict[str, int] = {}
    seccion_por_dim: dict[tuple, int] = {}
    apoyos_nodos: set[int] = set()

    def _nodo(x: float, y: float, z: float) -> int:
        key = (round(x, _TOL), round(y, _TOL), round(z, _TOL))
        if key not in nodos_por_coord:
            nid = len(nodos_por_coord) + 1
            nodos_por_coord[key] = nid
            modelo.nodos.append(Nodo(nid, key[0], key[1], key[2]))
        return nodos_por_coord[key]

    def _material(s: str) -> int:
        if s not in material_por_str:
            mid = len(material_por_str) + 1
            material_por_str[s] = mid
            modelo.materiales.append(Material(mid, E=material_a_E_pa(s)))
        return material_por_str[s]

    def _seccion(col: Columna) -> int:
        key = (round(col.base, _TOL), round(col.peralte, _TOL))
        if key not in seccion_por_dim:
            sid = len(seccion_por_dim) + 1
            seccion_por_dim[key] = sid
            area, iy, iz, j = _propiedades_seccion(col)
            modelo.secciones.append(
                Seccion(sid, area=area, inercia_y=iy, inercia_z=iz, constante_torsion=j))
        return seccion_por_dim[key]

    for col in edificio.elementos_verticales:
        if not isinstance(col, Columna):
            continue  # muros fuera de alcance (B0)
        x, y = col.posicion
        mat_id = _material(col.material)
        sec_id = _seccion(col)
        nodos_col = [_nodo(x, y, z) for z in _quiebres(col, cotas_nivel)]
        for ni, nj in zip(nodos_col, nodos_col[1:]):
            eid = len(modelo.elementos) + 1
            modelo.elementos.append(ElementoFrame(eid, ni, nj, mat_id, sec_id))
        if col.zapata is not None:
            base_nid = nodos_col[0]   # quiebre más bajo = cota_base
            if base_nid not in apoyos_nodos:
                apoyos_nodos.add(base_nid)
                modelo.apoyos.append(Apoyo.empotrado(base_nid))

    for nivel in edificio.niveles_ordenados():
        for losa in nivel.losas:
            vid = len(modelo.losas) + 1
            modelo.losas.append(LosaViz(vid, nivel.puntos_losa_3d(losa)))

    return modelo


def cargas_de_losas(edificio: Edificio, modelo: ModeloEstructural) -> list[CargaNodal]:
    """Cargas nodales equivalentes del peso de las losas sobre la malla (Rebanada C2).

    Reparte cada paño a sus bordes (``repartir_losa``) y convierte cada borde en
    fuerzas nodales sobre sus dos nodos extremo: las distribuciones triangular/
    trapezoidal/uniforme son **simétricas respecto al centro del borde**, así que
    su resultante cae en el punto medio → mitad de ``fuerza_total`` a cada extremo.
    Gravedad → ``fz < 0``; ``kN → N`` (×1000, el core es SI en newtons). Muerta →
    caso ``"D"``, viva → caso ``"L"``.

    No muta ``modelo``; devuelve la lista para ``modelo.cargas.extend(...)``. Una
    esquina de losa sin nodo de columna a su cota → ``ValueError`` (en B0, sin
    vigas, esa carga no tiene cómo bajar).
    """
    nodo_por_coord = {
        (round(n.x, _TOL), round(n.y, _TOL), round(n.z, _TOL)): n.id
        for n in modelo.nodos
    }

    def _nid(x: float, y: float, z: float, losa_id: int) -> int:
        key = (round(x, _TOL), round(y, _TOL), round(z, _TOL))
        if key not in nodo_por_coord:
            raise ValueError(
                f"losa {losa_id}: la esquina ({x}, {y}) no tiene columna a la cota "
                f"{z}; no hay nodo donde aplicar su carga."
            )
        return nodo_por_coord[key]

    acum: dict[tuple[int, str], float] = {}
    for nivel in edificio.niveles_ordenados():
        for losa in nivel.losas:
            rep = repartir_losa(losa)
            puntos = losa.puntos
            n = len(puntos)
            for caso, direccion in (("D", rep.muerta), ("L", rep.viva)):
                for borde in direccion.bordes:
                    i = borde.indice_borde
                    media = borde.fuerza_total / 2.0 * _KN_A_N
                    for (x, y) in (puntos[i], puntos[(i + 1) % n]):
                        nid = _nid(x, y, nivel.cota, losa.id)
                        acum[(nid, caso)] = acum.get((nid, caso), 0.0) - media

    return [
        CargaNodal(nodo_id=nid, fz=fz, caso=caso)
        for (nid, caso), fz in sorted(acum.items())
    ]
