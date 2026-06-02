"""Análisis modal — período fundamental (capa 1, análisis FEA puro).

Calcula la frecuencia/período del primer modo de un marco 3D con masas nodales
concentradas, en Python puro:

1. Ensambla K (reusa el solver estático) y toma los GDL libres.
2. Separa los GDL **con masa** (traslacionales de nodos con masa) de los **sin
   masa**, y condensa estáticamente (Guyan) estos últimos:
   ``K_red = K_mm − K_ms · K_ss⁻¹ · K_sm``.
3. Itera por potencia inversa el problema generalizado ``K_red φ = ω² M_red φ``
   para el menor ``ω²`` (cociente de Rayleigh), y devuelve ``T = 2π/ω``.

La condensación de los GDL rotacionales es exacta para masas traslacionales:
reproduce, p.ej., la rigidez de punta ``3EI/L³`` de un voladizo. NumPy/SciPy
(eigensolver disperso) entran para muchos modos a escala (B5).
"""
from __future__ import annotations

import math
from dataclasses import dataclass

from motor_fea.core.modelo import GDL_POR_NODO, ModeloEstructural
from motor_fea.core.solver import (
    _matvec,
    ensamblar_global,
    resolver_lineal,
)


@dataclass
class ResultadoModal:
    """Primer modo: ω (rad/s), frecuencia (Hz), período (s) y la forma modal por nodo."""
    omega: float
    frecuencia: float
    periodo: float
    forma: dict[int, tuple[float, float, float]]   # nodo_id → (ux, uy, uz) normalizado


def _submatriz(K, filas, cols):
    return [[K[i][j] for j in cols] for i in filas]


def periodo_fundamental(modelo: ModeloEstructural, masas: dict[int, float],
                        max_iter: int = 500, tol: float = 1e-12) -> ResultadoModal:
    """Período del primer modo para masas nodales traslacionales ``masas`` (nodo_id → kg)."""
    errores = modelo.validar()
    if errores:
        raise ValueError("Modelo inválido: " + "; ".join(errores))
    masas = {k: v for k, v in masas.items() if v > 0}
    if not masas:
        raise ValueError("Se requiere al menos una masa nodal positiva.")

    idx = modelo.indice_nodos()
    n = modelo.n_gdl
    K = ensamblar_global(modelo)

    fijos: set[int] = set()
    for ap in modelo.apoyos:
        base = idx[ap.nodo_id] * GDL_POR_NODO
        for d, restr in enumerate(ap.restricciones()):
            if restr:
                fijos.add(base + d)
    libres = [i for i in range(n) if i not in fijos]
    libres_set = set(libres)

    # GDL con masa = traslaciones (0,1,2) de nodos con masa, que estén libres.
    con_masa: list[int] = []
    masa_de: list[float] = []
    for nodo_id, m in masas.items():
        if nodo_id not in idx:
            raise ValueError(f"Masa en nodo inexistente {nodo_id}.")
        base = idx[nodo_id] * GDL_POR_NODO
        for d in range(3):
            g = base + d
            if g in libres_set:
                con_masa.append(g)
                masa_de.append(m)
    if not con_masa:
        raise ValueError("Las masas no caen sobre GDL libres (¿nodo totalmente apoyado?).")

    sin_masa = [g for g in libres if g not in set(con_masa)]

    Kmm = _submatriz(K, con_masa, con_masa)
    if sin_masa:
        Kms = _submatriz(K, con_masa, sin_masa)
        Kss = _submatriz(K, sin_masa, sin_masa)
        Ksm = _submatriz(K, sin_masa, con_masa)
        # Y = Kss⁻¹ · Ksm  (columna por columna)
        ncol = len(con_masa)
        Y = [[0.0] * ncol for _ in range(len(sin_masa))]
        for c in range(ncol):
            col = resolver_lineal(Kss, [Ksm[r][c] for r in range(len(sin_masa))])
            for r in range(len(sin_masa)):
                Y[r][c] = col[r]
        # Kred = Kmm − Kms · Y
        Kred = [[Kmm[i][j] - sum(Kms[i][p] * Y[p][j] for p in range(len(sin_masa)))
                 for j in range(ncol)] for i in range(ncol)]
    else:
        Kred = Kmm

    nm = len(con_masa)
    # Iteración de potencia inversa para el menor ω².
    x = [1.0] * nm
    omega2 = 0.0
    for _ in range(max_iter):
        b = [masa_de[i] * x[i] for i in range(nm)]      # M·x (M diagonal)
        y = resolver_lineal(Kred, b)                    # y = Kred⁻¹ M x
        norm = math.sqrt(sum(v * v for v in y))
        if norm < 1e-300:
            raise ValueError("Iteración modal degenerada.")
        y = [v / norm for v in y]
        ky = _matvec(Kred, y)
        num = sum(y[i] * ky[i] for i in range(nm))      # yᵀ K y
        den = sum(masa_de[i] * y[i] * y[i] for i in range(nm))  # yᵀ M y
        nuevo = num / den
        if abs(nuevo - omega2) <= tol * max(1.0, nuevo):
            omega2 = nuevo
            x = y
            break
        omega2, x = nuevo, y

    omega = math.sqrt(omega2)
    forma: dict[int, tuple[float, float, float]] = {}
    for pos, g in enumerate(con_masa):
        nodo_id = modelo.nodos[g // GDL_POR_NODO].id
        comp = g % GDL_POR_NODO
        prev = forma.get(nodo_id, (0.0, 0.0, 0.0))
        forma[nodo_id] = tuple(x[pos] if k == comp else prev[k] for k in range(3))  # type: ignore[assignment]

    return ResultadoModal(
        omega=omega,
        frecuencia=omega / (2 * math.pi),
        periodo=2 * math.pi / omega,
        forma=forma,
    )
