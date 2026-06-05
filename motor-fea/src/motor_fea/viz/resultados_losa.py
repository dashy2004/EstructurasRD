"""Cálculo de resultados de losa para el visor (capa frontera).

Empaqueta el FEM de losas (``core.losa_fem``) en un LosaDTO render-agnóstico:
malla rectangular + campos escalares por nodo (deflexión, momentos Mx y My) con
unidades, min/max y un factor de exageración sugerido. Función pura: solo usa
``core``; no toca HTTP ni three.js, así que se prueba con asserts normales.

Unidades de presentación: deflexión en mm; momentos en kN·m/m.
"""
from __future__ import annotations

import math

from motor_fea.core.losa_fem import resolver_losa_rectangular


def calcular_resultados_losa(a: float = 5.0, b: float = 5.0, nx: int = 8, ny: int = 8,
                             E: float = 2.0e10, nu: float = 0.2, t: float = 0.2,
                             q: float = 10000.0, borde: str = "simple") -> dict:
    """LosaDTO: malla + campos por nodo (deflexión, Mx, My). ValueError si los parámetros son inválidos."""
    if a <= 0 or b <= 0 or t <= 0 or q <= 0:
        raise ValueError("a, b, t y q deben ser positivos.")
    if nx < 1 or ny < 1:
        raise ValueError("nx y ny deben ser ≥ 1.")
    if borde not in ("simple", "empotrado"):
        raise ValueError(f"borde desconocido: {borde!r} (use 'simple' o 'empotrado').")

    res = resolver_losa_rectangular(a, b, nx, ny, E, nu, t, q, borde)

    deflexion: dict[str, float] = {}
    momento_mx: dict[str, float] = {}
    momento_my: dict[str, float] = {}
    max_w = 0.0
    for i in range(nx + 1):
        for j in range(ny + 1):
            w = res.desplazamientos_w[(i, j)]
            mx, my = res.momentos_nodales[(i, j)]
            clave = f"{i},{j}"
            deflexion[clave] = w * 1000.0          # m → mm
            momento_mx[clave] = mx / 1000.0        # N·m/m → kN·m/m
            momento_my[clave] = my / 1000.0
            max_w = max(max_w, abs(w))

    diag = math.sqrt(a * a + b * b)
    # 0.08·diagonal / w_max: relieve ≈ 8% de la diagonal; 1.0 si la losa no flecta (no divide por cero).
    factor_sugerido = 0.08 * diag / max_w if max_w > 0.0 else 1.0

    def campo(valores: dict[str, float], unidad: str) -> dict:
        vs = list(valores.values())
        return {"unidad": unidad, "min": min(vs), "max": max(vs), "valores": valores}

    return {
        "a": a, "b": b, "nx": nx, "ny": ny,
        "factor_sugerido": factor_sugerido,
        "campos": {
            "deflexion": campo(deflexion, "mm"),
            "momento_mx": campo(momento_mx, "kN·m/m"),
            "momento_my": campo(momento_my, "kN·m/m"),
        },
    }
