"""Cálculo del armado de ejemplo para el visor (capa frontera).

Por cada elemento del modelo, deriva un armado representativo (barras
longitudinales + estribo) para dibujarlo en 3D dentro de la sección. El motor
Python no calcula esfuerzos por elemento, así que es un *armado de ejemplo*:
la cantidad de acero sale de reglas ACI mínimas (ρ≈1% en columnas, As mínimo a
flexión en vigas) y la barra de la tabla AREAS_BARRA_MM2; las posiciones se
derivan de la geometría de la sección. Función pura: usa solo `viz.escena`
(geometría b,h y clasificación) y `normativa.aci318` (tabla + As mínimo).

Unidades del DTO: metros (posiciones, diámetros, separaciones), como la escena.
"""
from __future__ import annotations

import math

from motor_fea.core.modelo import ModeloEstructural
from motor_fea.normativa.aci318 import AREAS_BARRA_MM2, as_minimo_flexion
from motor_fea.viz.escena import _clasificar, _dimensiones


def _diametro_m(num: int) -> float:
    """Diámetro de una barra #num (octavos de pulgada) en metros."""
    return num * 25.4 / 8.0 / 1000.0


def _perimetro(n: int, ox: float, oy: float) -> list[tuple[float, float]]:
    """n puntos equiespaciados por longitud de arco en el perímetro del rectángulo
    de semi-ejes (ox, oy), empezando en la esquina (-ox, -oy) (antihorario)."""
    esquinas = [(-ox, -oy), (ox, -oy), (ox, oy), (-ox, oy)]
    lados = [2 * ox, 2 * oy, 2 * ox, 2 * oy]
    per = 2 * (2 * ox + 2 * oy)
    if per <= 0:
        return [(0.0, 0.0)] * n
    puntos: list[tuple[float, float]] = []
    for k in range(n):
        s = k * per / n
        for lado in range(4):
            if s <= lados[lado] or lado == 3:
                x0, y0 = esquinas[lado]
                x1, y1 = esquinas[(lado + 1) % 4]
                f = s / lados[lado] if lados[lado] > 0 else 0.0
                puntos.append((x0 + (x1 - x0) * f, y0 + (y1 - y0) * f))
                break
            s -= lados[lado]
    return puntos


def _barra_columna(as_req: float) -> tuple[int, int]:
    """(num, n) para As requerido: n = max(4, ceil(As/area)) a múltiplo de 4; si n>12 sube a #8."""
    for num in (6, 8):
        n = max(4, math.ceil(as_req / AREAS_BARRA_MM2[num]))
        n = ((n + 3) // 4) * 4
        if n <= 12 or num == 8:
            return num, n
    return 8, 12


def _posiciones_columna(b: float, h: float, rec: float, num: int, n: int) -> list[dict]:
    """Posiciones (x,y,d) de n barras #num distribuidas en el perímetro de la sección."""
    d = _diametro_m(num)
    ox, oy = max(0.0, b / 2 - rec - d / 2), max(0.0, h / 2 - rec - d / 2)
    return [{"x": x, "y": y, "d": d} for x, y in _perimetro(n, ox, oy)]


def _armado_columna(b: float, h: float, rec: float) -> tuple[list[dict], int, int, float]:
    as_req = 0.01 * (b * 1000.0) * (h * 1000.0)      # ρ_min = 1% (ACI 10.6.1.1), mm²
    num, n = _barra_columna(as_req)
    return _posiciones_columna(b, h, rec, num, n), num, n, _diametro_m(num)


def _posiciones_viga(b: float, h: float, rec: float, num: int, n_inf: int) -> list[dict]:
    """Posiciones de n_inf barras #num inferiores + 2 superiores en la sección."""
    d = _diametro_m(num)
    ox = max(0.0, b / 2 - rec - d / 2)
    y_inf, y_sup = -(h / 2 - rec - d / 2), (h / 2 - rec - d / 2)
    long: list[dict] = []
    for k in range(n_inf):                            # fila inferior
        f = k / (n_inf - 1) if n_inf > 1 else 0.5    # n_inf siempre ≥2; el 0.5 es defensivo
        long.append({"x": -ox + 2 * ox * f, "y": y_inf, "d": d})
    long += [{"x": -ox, "y": y_sup, "d": d}, {"x": ox, "y": y_sup, "d": d}]   # 2 sup
    return long


def _armado_viga(b: float, h: float, rec: float, fc: float, fy: float) -> tuple[list[dict], int, float]:
    d_util = h - rec
    as_min = as_minimo_flexion(b * 1000.0, d_util * 1000.0, fc, fy)    # mm²
    n_inf = max(2, math.ceil(as_min / AREAS_BARRA_MM2[5]))
    return _posiciones_viga(b, h, rec, 5, n_inf), n_inf, _diametro_m(5)


def calcular_armado(modelo: ModeloEstructural, fc: float = 21.0, fy: float = 420.0,
                    recubrimiento: float = 0.04) -> dict:
    """ArmadoDTO: armado de ejemplo (longitudinal + estribo) por elemento."""
    if fc <= 0 or fy <= 0 or recubrimiento <= 0:
        raise ValueError("fc, fy y recubrimiento deben ser positivos.")
    errores = modelo.validar()
    if errores:
        raise ValueError("Modelo inválido: " + "; ".join(errores))

    nodos = {n.id: n for n in modelo.nodos}
    secs = {s.id: s for s in modelo.secciones}
    d_est = _diametro_m(3)
    elementos: list[dict] = []
    for e in modelo.elementos:
        ni, nj = nodos[e.nodo_i], nodos[e.nodo_j]
        b, h = _dimensiones(secs[e.seccion_id])
        if b - 2 * recubrimiento <= 0 or h - 2 * recubrimiento <= 0:
            raise ValueError(f"Recubrimiento {recubrimiento} incompatible con la sección {b}×{h}.")
        if _clasificar(ni, nj) == "columna":
            long, num, n, d_long = _armado_columna(b, h, recubrimiento)
            # Separación de estribos ACI 25.7.2.1 (min de 16·Ø_long, 48·Ø_estribo, lado);
            # acotada a ≥0.05 m (separación mínima práctica de obra).
            s = max(0.05, min(16 * d_long, 48 * d_est, min(b, h)))
            tipo, desig = "columna", f"{n}#{num} + E#3@{s:.2f}"
        else:
            long, n_inf, d_long = _armado_viga(b, h, recubrimiento, fc, fy)
            # Estribos de viga ~ d/2 (ACI 9.7.6.2.2), acotado a ≥0.05 m.
            s = max(0.05, (h - recubrimiento) / 2)
            tipo, desig = "viga", f"{n_inf}#5 inf + 2#5 sup + E#3@{s:.2f}"
        elementos.append({
            "id": e.id, "i": e.nodo_i, "j": e.nodo_j, "tipo": tipo,
            "long": long,
            "estribo": {"d": d_est, "s": s, "w": b - 2 * recubrimiento, "h": h - 2 * recubrimiento},
            "designacion": desig,
        })
    return {"recubrimiento": recubrimiento, "elementos": elementos}
