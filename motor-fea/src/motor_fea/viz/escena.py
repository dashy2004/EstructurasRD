"""Exportador de geometría del modelo a un SceneDTO render-agnóstico (capa frontera).

Función pura (stdlib, sin NumPy ni web): un :class:`ModeloEstructural` se traduce
a un dict JSON-able que cualquier visor 3D consume. No conoce three.js ni HTTP.
"""
from __future__ import annotations

import math

from motor_fea.core.modelo import ModeloEstructural

B_VIS = 0.20          # grosor visual por defecto (m) para secciones no rectangulares
H_VIS = 0.20
RELACION_MAX = 50.0   # tope b:h aceptable antes de caer al grosor por defecto


def _clasificar(ni, nj) -> str:
    """columna si la componente vertical (Δz) domina; si no, viga."""
    dx, dy, dz = abs(nj.x - ni.x), abs(nj.y - ni.y), abs(nj.z - ni.z)
    return "columna" if dz > dx and dz > dy else "viga"


def _dimensiones(sec) -> tuple[float, float]:
    """Deriva (b, h) de una sección rectangular desde A e Iz; si no es físico, default."""
    a, iz = sec.area, sec.inercia_z
    if a <= 0.0 or iz <= 0.0:
        return B_VIS, H_VIS
    h = math.sqrt(12.0 * iz / a)
    b = a / h
    if b <= 0.0 or h <= 0.0 or max(b, h) / min(b, h) > RELACION_MAX:
        return B_VIS, H_VIS
    return b, h


def exportar_escena(modelo: ModeloEstructural) -> dict:
    """Traduce el modelo a un SceneDTO (dict). Lanza ValueError si el modelo es inválido."""
    errores = modelo.validar()
    if errores:
        raise ValueError("Modelo inválido: " + "; ".join(errores))

    nodos = {n.id: n for n in modelo.nodos}
    secs = {s.id: s for s in modelo.secciones}

    barras = []
    for e in modelo.elementos:
        ni, nj = nodos[e.nodo_i], nodos[e.nodo_j]
        b, h = _dimensiones(secs[e.seccion_id])
        barras.append({"id": e.id, "i": e.nodo_i, "j": e.nodo_j,
                       "tipo": _clasificar(ni, nj), "b": b, "h": h})

    if modelo.nodos:
        xs = [n.x for n in modelo.nodos]
        ys = [n.y for n in modelo.nodos]
        zs = [n.z for n in modelo.nodos]
        bbox = {"min": [min(xs), min(ys), min(zs)], "max": [max(xs), max(ys), max(zs)]}
    else:
        bbox = {"min": [0.0, 0.0, 0.0], "max": [0.0, 0.0, 0.0]}

    return {
        "unidades": "m",
        "bbox": bbox,
        "nodos": [{"id": n.id, "p": [n.x, n.y, n.z]} for n in modelo.nodos],
        "barras": barras,
        "losas": [],
    }
