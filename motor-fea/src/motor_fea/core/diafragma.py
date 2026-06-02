"""Diafragma rígido en su plano — constraint master-slave (capa 1, análisis).

Modela un piso infinitamente rígido en su plano (estándar en edificios): los GDL
horizontales (ux, uy, rz) de los nodos del diafragma se ligan a un **nodo maestro**
por cinemática de cuerpo rígido::

    ux_s = UX − (y_s − y_m)·RZ
    uy_s = UY + (x_s − x_m)·RZ
    rz_s = RZ

Los GDL fuera de plano (uz, rx, ry) quedan libres. Se resuelve por el **método de
transformación**: ``K_red = Tᵀ K T``, ``F_red = Tᵀ F``, se aplican los apoyos sobre
los GDL independientes, se resuelve y se expande ``u = T·u_red``.

Python puro; reusa el ensamblaje de :mod:`motor_fea.core.solver`.
"""
from __future__ import annotations

from dataclasses import dataclass

from motor_fea.core.modelo import GDL_POR_NODO, ModeloEstructural
from motor_fea.core.solver import _matmul, _matvec, _transpuesta, ensamblar_global, resolver_lineal


@dataclass(frozen=True)
class Diafragma:
    """Un diafragma rígido: nodo maestro + nodos esclavos (todos a la misma cota)."""
    maestro: int
    esclavos: tuple[int, ...]


@dataclass
class ResultadoDiafragma:
    desplazamientos: dict[int, tuple[float, ...]]   # nodo_id → 6 GDL
    reacciones: dict[int, tuple[float, ...]]


def resolver_con_diafragma(modelo: ModeloEstructural, diafragmas: list[Diafragma]) -> ResultadoDiafragma:
    """Análisis estático lineal con diafragmas rígidos en plano (método de transformación)."""
    errores = modelo.validar()
    if errores:
        raise ValueError("Modelo inválido: " + "; ".join(errores))

    idx = modelo.indice_nodos()
    nodos = {n.id: n for n in modelo.nodos}
    n = modelo.n_gdl
    K = ensamblar_global(modelo)
    F = [0.0] * n
    for c in modelo.cargas:
        base = idx[c.nodo_id] * GDL_POR_NODO
        for d, v in enumerate(c.componentes()):
            F[base + d] += v

    # GDL esclavizados (ux,uy,rz de cada esclavo) → dependientes del maestro.
    esclavo_de: dict[int, Diafragma] = {}
    for dia in diafragmas:
        for s in dia.esclavos:
            esclavo_de[s] = dia

    gdl_dependientes: set[int] = set()
    for s, dia in esclavo_de.items():
        base = idx[s] * GDL_POR_NODO
        for d in (0, 1, 5):                 # ux, uy, rz
            gdl_dependientes.add(base + d)

    independientes = [g for g in range(n) if g not in gdl_dependientes]
    pos_ind = {g: i for i, g in enumerate(independientes)}
    n_ind = len(independientes)

    # T (n × n_ind): identidad en los independientes; filas de los esclavos por cinemática.
    T = [[0.0] * n_ind for _ in range(n)]
    for g in independientes:
        T[g][pos_ind[g]] = 1.0
    for s, dia in esclavo_de.items():
        m = dia.maestro
        ns, nm = nodos[s], nodos[m]
        bm = idx[m] * GDL_POR_NODO
        ux_m, uy_m, rz_m = pos_ind[bm + 0], pos_ind[bm + 1], pos_ind[bm + 5]
        bs = idx[s] * GDL_POR_NODO
        dx, dy = ns.x - nm.x, ns.y - nm.y
        # ux_s = UX − dy·RZ ; uy_s = UY + dx·RZ ; rz_s = RZ
        T[bs + 0][ux_m] = 1.0; T[bs + 0][rz_m] = -dy
        T[bs + 1][uy_m] = 1.0; T[bs + 1][rz_m] = dx
        T[bs + 5][rz_m] = 1.0

    Tt = _transpuesta(T)
    Kred = _matmul(_matmul(Tt, K), T)
    Fred = _matvec(Tt, F)

    # Apoyos: GDL fijos mapeados al espacio independiente (no se restringe un esclavo).
    fijos_ind: set[int] = set()
    for ap in modelo.apoyos:
        base = idx[ap.nodo_id] * GDL_POR_NODO
        for d, restr in enumerate(ap.restricciones()):
            if restr and (base + d) in pos_ind:
                fijos_ind.add(pos_ind[base + d])
    libres = [i for i in range(n_ind) if i not in fijos_ind]

    Kff = [[Kred[i][j] for j in libres] for i in libres]
    Ff = [Fred[i] for i in libres]
    uf = resolver_lineal(Kff, Ff) if libres else []
    ured = [0.0] * n_ind
    for pos, i in enumerate(libres):
        ured[i] = uf[pos]

    u = _matvec(T, ured)                    # expandir a GDL completos
    Ku = _matvec(K, u)
    reac_full = [Ku[i] - F[i] for i in range(n)]

    desplazamientos = {
        nd.id: tuple(u[idx[nd.id] * GDL_POR_NODO + d] for d in range(6)) for nd in modelo.nodos
    }
    reacciones = {}
    for ap in modelo.apoyos:
        base = idx[ap.nodo_id] * GDL_POR_NODO
        reacciones[ap.nodo_id] = tuple(reac_full[base + d] for d in range(6))
    return ResultadoDiafragma(desplazamientos, reacciones)
