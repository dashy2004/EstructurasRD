"""Análisis modal de marcos 3D con masas nodales concentradas (capa 1, FEA puro).

Pipeline (Python puro):

1. Ensambla K (reusa el solver estático) y toma los GDL libres.
2. Separa los GDL **con masa** (traslacionales de nodos con masa) de los **sin
   masa**, y condensa estáticamente (Guyan) estos últimos:
   ``K_red = K_mm − K_ms · K_ss⁻¹ · K_sm``.
3. Resuelve ``K_red φ = ω² M_red φ`` por iteración de potencia inversa para el
   menor ``ω²``; para varios modos, deflación M-ortogonal contra los hallados.

La condensación de los GDL rotacionales es exacta para masas traslacionales
(reproduce, p.ej., ``3EI/L³`` de un voladizo). NumPy/SciPy (``eigsh``) entran
para muchos modos a escala (B5).
"""
from __future__ import annotations

import math
from dataclasses import dataclass

from motor_fea.core.modelo import GDL_POR_NODO, ModeloEstructural
from motor_fea.core.solver import _matvec, ensamblar_global, resolver_lineal


@dataclass
class ResultadoModal:
    """Un modo: ω (rad/s), frecuencia (Hz), período (s) y forma modal por nodo."""
    omega: float
    frecuencia: float
    periodo: float
    forma: dict[int, tuple[float, float, float]]   # nodo_id → (ux, uy, uz)


def _submatriz(K, filas, cols):
    return [[K[i][j] for j in cols] for i in filas]


def _reducir(modelo: ModeloEstructural, masas: dict[int, float]):
    """Reduce el modelo a (Kred, masa_de, con_masa): sistema condensado de GDL con masa."""
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

    con_masa: list[int] = []
    masa_de: list[float] = []
    for nodo_id, m in masas.items():
        if nodo_id not in idx:
            raise ValueError(f"Masa en nodo inexistente {nodo_id}.")
        base = idx[nodo_id] * GDL_POR_NODO
        for d in range(3):                       # traslaciones ux, uy, uz
            g = base + d
            if g in libres_set:
                con_masa.append(g)
                masa_de.append(m)
    if not con_masa:
        raise ValueError("Las masas no caen sobre GDL libres (¿nodo totalmente apoyado?).")

    sin_masa = [g for g in libres if g not in set(con_masa)]
    Kmm = _submatriz(K, con_masa, con_masa)
    if not sin_masa:
        return Kmm, masa_de, con_masa

    Kms = _submatriz(K, con_masa, sin_masa)
    Ksm = _submatriz(K, sin_masa, con_masa)
    Kss = _submatriz(K, sin_masa, sin_masa)
    ncol = len(con_masa)
    Y = [[0.0] * ncol for _ in range(len(sin_masa))]
    for c in range(ncol):
        col = resolver_lineal(Kss, [Ksm[r][c] for r in range(len(sin_masa))])
        for r in range(len(sin_masa)):
            Y[r][c] = col[r]
    Kred = [[Kmm[i][j] - sum(Kms[i][p] * Y[p][j] for p in range(len(sin_masa)))
             for j in range(ncol)] for i in range(ncol)]
    return Kred, masa_de, con_masa


def _forma_por_nodo(modelo, con_masa, vec):
    forma: dict[int, tuple[float, float, float]] = {}
    for pos, g in enumerate(con_masa):
        nodo_id = modelo.nodos[g // GDL_POR_NODO].id
        comp = g % GDL_POR_NODO
        prev = forma.get(nodo_id, (0.0, 0.0, 0.0))
        forma[nodo_id] = tuple(vec[pos] if k == comp else prev[k] for k in range(3))  # type: ignore[assignment]
    return forma


def _inversa_deflactada(Kred, masa_de, hallados, max_iter, tol):
    """Iteración de potencia inversa con deflación M-ortogonal contra ``hallados`` (M-normalizados)."""
    nm = len(masa_de)

    def m_ortogonalizar(v):
        for phi in hallados:
            coef = sum(masa_de[i] * phi[i] * v[i] for i in range(nm))
            v = [v[i] - coef * phi[i] for i in range(nm)]
        return v

    x = [1.0 + 0.01 * i for i in range(nm)]      # arranque determinista, rompe simetría
    x = m_ortogonalizar(x)
    omega2 = 0.0
    for _ in range(max_iter):
        b = [masa_de[i] * x[i] for i in range(nm)]
        y = resolver_lineal(Kred, b)
        y = m_ortogonalizar(y)
        mn = math.sqrt(sum(masa_de[i] * y[i] * y[i] for i in range(nm)))
        if mn < 1e-300:
            raise ValueError("Iteración modal degenerada (deflación agotó el subespacio).")
        y = [v / mn for v in y]                   # M-normalizar: yᵀ M y = 1
        ky = _matvec(Kred, y)
        nuevo = sum(y[i] * ky[i] for i in range(nm))   # ω² = yᵀ K y
        if abs(nuevo - omega2) <= tol * max(1.0, nuevo):
            omega2, x = nuevo, y
            break
        omega2, x = nuevo, y
    return math.sqrt(omega2), x


def modos(modelo: ModeloEstructural, masas: dict[int, float], n_modos: int = 3,
          max_iter: int = 1000, tol: float = 1e-12) -> list[ResultadoModal]:
    """Los ``n_modos`` modos más bajos (ascendente en ω), por deflación M-ortogonal."""
    Kred, masa_de, con_masa = _reducir(modelo, masas)
    n_modos = min(n_modos, len(con_masa))
    hallados: list[list[float]] = []
    salida: list[ResultadoModal] = []
    for _ in range(n_modos):
        omega, vec = _inversa_deflactada(Kred, masa_de, hallados, max_iter, tol)
        hallados.append(vec)
        salida.append(ResultadoModal(
            omega=omega,
            frecuencia=omega / (2 * math.pi),
            periodo=2 * math.pi / omega,
            forma=_forma_por_nodo(modelo, con_masa, vec),
        ))
    return salida


def periodo_fundamental(modelo: ModeloEstructural, masas: dict[int, float],
                        max_iter: int = 500, tol: float = 1e-12) -> ResultadoModal:
    """Período del primer modo (atajo de :func:`modos` con n=1)."""
    return modos(modelo, masas, n_modos=1, max_iter=max_iter, tol=tol)[0]


@dataclass
class ParticipacionModal:
    """Masa modal efectiva y % de participación de un modo en una dirección."""
    masa_efectiva: float
    participacion: float       # fracción [0..1] de la masa total en esa dirección


def participacion_modal(modelo: ModeloEstructural, masas: dict[int, float],
                        direccion: str = "x", n_modos: int = 3) -> list[tuple[ResultadoModal, ParticipacionModal]]:
    """Para cada modo, masa modal efectiva y % de participación en ``direccion`` ∈ {x,y,z}.

    Influencia: vector unitario en los GDL traslacionales de la dirección dada.
    Con φ M-normalizado: M_eff = (φᵀ M r)²; total = rᵀ M r = masa total en la dirección.
    """
    comp = {"x": 0, "y": 1, "z": 2}[direccion]
    Kred, masa_de, con_masa = _reducir(modelo, masas)
    r = [1.0 if (g % GDL_POR_NODO) == comp else 0.0 for g in con_masa]
    total = sum(masa_de[i] * r[i] * r[i] for i in range(len(con_masa)))

    hallados: list[list[float]] = []
    res = []
    for m in modos(modelo, masas, n_modos):
        # Reconstruir el vector M-normalizado en el espacio reducido desde la forma.
        vec = []
        for g in con_masa:
            nodo_id = modelo.nodos[g // GDL_POR_NODO].id
            vec.append(m.forma[nodo_id][g % GDL_POR_NODO])
        l = sum(masa_de[i] * vec[i] * r[i] for i in range(len(con_masa)))
        meff = l * l
        res.append((m, ParticipacionModal(meff, meff / total if total > 0 else 0.0)))
        hallados.append(vec)
    return res
