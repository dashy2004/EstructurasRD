"""Análisis por caso de carga (capa core).

Separa las cargas del modelo por su ``caso`` (D/L/W/E…), corre un análisis lineal
independiente por caso y devuelve los esfuerzos por elemento de cada uno. La
combinación LRFD es posterior (a nivel de esfuerzos, en la capa de diseño).
"""
from __future__ import annotations

from dataclasses import replace

from motor_fea.core.modelo import ModeloEstructural
from motor_fea.core.solver import EsfuerzosElemento, esfuerzos_elementos, resolver


def esfuerzos_por_caso(modelo: ModeloEstructural) -> dict[str, dict[int, EsfuerzosElemento]]:
    """{caso: {elem_id: EsfuerzosElemento}} — un análisis lineal por caso de carga distinto."""
    casos = sorted({c.caso for c in modelo.cargas})
    salida: dict[str, dict[int, EsfuerzosElemento]] = {}
    for caso in casos:
        sub = replace(modelo, cargas=[c for c in modelo.cargas if c.caso == caso])
        salida[caso] = esfuerzos_elementos(sub, resolver(sub))
    return salida
