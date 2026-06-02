"""Combinaciones de carga LRFD — ACI 318-19 §5.3.1 / ASCE 7-16 §2.3.

Capa 2 (code-checking), pura. Aplica las combinaciones de resistencia a un
conjunto de efectos por tipo de carga y devuelve el efecto último por combo y la
envolvente (máximo y mínimo — el mínimo captura levantamiento/volcamiento).

Tipos de carga: D (muerta), L (viva), Lr (viva de techo), S (nieve), R (lluvia),
W (viento), E (sismo). W y E son **reversibles** (±). Las ranuras "(Lr o S o R)"
se resuelven con el mayor de los tres (la que gobierna).
"""
from __future__ import annotations

from dataclasses import dataclass

# Cada combinación es una lista de términos (factor, tipo). 'roof' = max(Lr,S,R).
_COMBOS: list[tuple[str, list[tuple[float, str]]]] = [
    ("1", [(1.4, "D")]),
    ("2", [(1.2, "D"), (1.6, "L"), (0.5, "roof")]),
    ("3a", [(1.2, "D"), (1.6, "roof"), (1.0, "L")]),
    ("3b", [(1.2, "D"), (1.6, "roof"), (0.5, "W")]),
    ("4", [(1.2, "D"), (1.0, "W"), (1.0, "L"), (0.5, "roof")]),
    ("5", [(1.2, "D"), (1.0, "E"), (1.0, "L"), (0.2, "S")]),
    ("6", [(0.9, "D"), (1.0, "W")]),
    ("7", [(0.9, "D"), (1.0, "E")]),
]

_REVERSIBLES = {"W", "E"}


@dataclass(frozen=True)
class Envolvente:
    maximo: float
    minimo: float
    combo_max: str
    combo_min: str


def combinaciones_resistencia(D: float = 0.0, L: float = 0.0, Lr: float = 0.0,
                              S: float = 0.0, R: float = 0.0, W: float = 0.0,
                              E: float = 0.0) -> dict[str, float]:
    """Efecto último U por cada combinación (ACI 318-19 §5.3.1).

    Los argumentos son los efectos (fuerza, momento, …) de cada tipo de carga,
    en unidades consistentes. Las combos con W o E se expanden a ±.
    """
    vals = {"D": D, "L": L, "S": S, "roof": max(Lr, S, R), "W": W, "E": E}
    salida: dict[str, float] = {}
    for nombre, terminos in _COMBOS:
        reversibles = [t for _, t in terminos if t in _REVERSIBLES]
        signos = [1.0, -1.0] if reversibles else [1.0]
        for signo in signos:
            u = 0.0
            for factor, tipo in terminos:
                v = vals.get(tipo, 0.0)
                if tipo in _REVERSIBLES:
                    v = v * signo
                u += factor * v
            sufijo = "" if not reversibles else ("(+)" if signo > 0 else "(-)")
            salida[nombre + sufijo] = u
    return salida


def envolvente(D: float = 0.0, L: float = 0.0, Lr: float = 0.0, S: float = 0.0,
               R: float = 0.0, W: float = 0.0, E: float = 0.0) -> Envolvente:
    """Envolvente (máximo y mínimo) de los efectos últimos sobre todas las combinaciones."""
    combos = combinaciones_resistencia(D, L, Lr, S, R, W, E)
    nmax = max(combos, key=lambda k: combos[k])
    nmin = min(combos, key=lambda k: combos[k])
    return Envolvente(maximo=combos[nmax], minimo=combos[nmin],
                      combo_max=nmax, combo_min=nmin)
