"""Cálculo de resultados estructurales para el visor (capa frontera).

Deriva, reusando los motores de ``core`` (solver y modal), los estados que el
visor anima: la **deformada** estática bajo peso propio + cargas del modelo y
las **formas modales** 1..n. Función pura: solo usa ``core``; no toca HTTP ni
three.js, así que se prueba con asserts normales.

Convención de unidades: SI (metros, newtons, kilogramos). El peso propio de un
elemento es ``densidad·area·L``; se reparte mitad a cada nodo, como masa (para
el análisis modal) y como carga gravitatoria ``fz = −m·g/2`` (para la deformada).
"""
from __future__ import annotations

import math
from dataclasses import replace

from motor_fea.core import modal, solver
from motor_fea.core.modelo import CargaNodal, ModeloEstructural, Nodo

G = 9.81  # aceleración de la gravedad (m/s²)


def _longitud(ni: Nodo, nj: Nodo) -> float:
    return math.sqrt((nj.x - ni.x) ** 2 + (nj.y - ni.y) ** 2 + (nj.z - ni.z) ** 2)


def _peso_propio(modelo: ModeloEstructural) -> tuple[dict[int, float], list[CargaNodal]]:
    """Masa nodal {id: kg} y cargas gravitatorias por peso propio (mitad a cada nodo)."""
    nodos = {n.id: n for n in modelo.nodos}
    mats = {m.id: m for m in modelo.materiales}
    secs = {s.id: s for s in modelo.secciones}
    masa: dict[int, float] = {}
    fz: dict[int, float] = {}
    for e in modelo.elementos:
        ni, nj = nodos[e.nodo_i], nodos[e.nodo_j]
        m_el = mats[e.material_id].densidad * secs[e.seccion_id].area * _longitud(ni, nj)
        for nid in (e.nodo_i, e.nodo_j):
            masa[nid] = masa.get(nid, 0.0) + m_el / 2.0
            fz[nid] = fz.get(nid, 0.0) - m_el * G / 2.0
    cargas = [CargaNodal(nid, fz=v) for nid, v in fz.items()]
    return masa, cargas


def _agregar_masa_sismica(modelo: ModeloEstructural, masa: dict[int, float]) -> None:
    """Suma la masa sísmica de las cargas verticales del modelo: |fz|/g."""
    for c in modelo.cargas:
        if c.fz != 0.0:
            masa[c.nodo_id] = masa.get(c.nodo_id, 0.0) + abs(c.fz) / G


def _diagonal_bbox(modelo: ModeloEstructural) -> float:
    if not modelo.nodos:
        return 1.0
    xs = [n.x for n in modelo.nodos]
    ys = [n.y for n in modelo.nodos]
    zs = [n.z for n in modelo.nodos]
    d = math.sqrt((max(xs) - min(xs)) ** 2 + (max(ys) - min(ys)) ** 2 + (max(zs) - min(zs)) ** 2)
    return d if d > 0.0 else 1.0


def _factor_sugerido(despl: dict[int, tuple[float, ...]], diag: float) -> float:
    """0.08·diagonal / max|desplazamiento|; 1.0 si el máximo es 0 (no divide por cero)."""
    maxd = 0.0
    for v in despl.values():
        mag = math.sqrt(v[0] ** 2 + v[1] ** 2 + v[2] ** 2)
        if mag > maxd:
            maxd = mag
    return 0.08 * diag / maxd if maxd > 0.0 else 1.0


def calcular_resultados(modelo: ModeloEstructural, n_modos: int = 3) -> dict:
    """ResultadosDTO: deformada (peso propio + cargas) y modos 1..n. ValueError si inválido."""
    errores = modelo.validar()
    if errores:
        raise ValueError("Modelo inválido: " + "; ".join(errores))

    diag = _diagonal_bbox(modelo)
    masa, cargas_pp = _peso_propio(modelo)
    _agregar_masa_sismica(modelo, masa)

    # Deformada: modelo + cargas de peso propio (sin mutar el original).
    modelo_def = replace(modelo, cargas=list(modelo.cargas) + cargas_pp)
    res = solver.resolver(modelo_def)
    despl = {nid: u[:3] for nid, u in res.desplazamientos.items()}
    deformada = {
        "factor_sugerido": _factor_sugerido(despl, diag),
        "desplazamientos": {str(nid): list(u) for nid, u in despl.items()},
    }

    # Modos: si no hay masa en GDL libres, modal lanza ValueError → modos=[].
    masas = {nid: m for nid, m in masa.items() if m > 0.0}
    try:
        modales = modal.modos(modelo, masas, n_modos=n_modos)
    except ValueError:
        modales = []
    # En estructuras con modos degenerados (p.ej. sección con Iy=Iz → flexión
    # equi-rígida), la deflación puede devolver modos de ω idéntica en orden no
    # ascendente; reordenamos para garantizar modo 1 = fundamental.
    modales = sorted(modales, key=lambda rm: rm.omega)
    modos = []
    for i, rm in enumerate(modales, start=1):
        modos.append({
            "indice": i,
            "periodo": rm.periodo,
            "frecuencia": rm.frecuencia,
            "omega": rm.omega,
            "factor_sugerido": _factor_sugerido(rm.forma, diag),
            "forma": {str(nid): list(v) for nid, v in rm.forma.items()},
        })

    return {"deformada": deformada, "modos": modos}
