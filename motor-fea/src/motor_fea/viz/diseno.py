"""Cálculo del armado DISEÑADO por fuerzas para el visor (capa frontera).

Por cada elemento: resuelve el modelo, extrae los esfuerzos, diseña el refuerzo
(``diseno_elemento``) y empaqueta el armado real + la demanda (Pu/Mu/Vu) + cumple,
reusando la derivación de posiciones de ``viz.armado``. Función pura: usa core
(solver), viz (escena/armado) y diseno_elemento (que envuelve aci318); no toca
HTTP ni three.js.

Unidades del DTO: metros (posiciones, estribo) y N/N·m (demanda), como la escena.
"""
from __future__ import annotations

from motor_fea import diseno_elemento
from motor_fea.core.modelo import ModeloEstructural
from motor_fea.core.solver import EsfuerzosElemento, esfuerzos_elementos, resolver
from motor_fea.viz import armado
from motor_fea.viz.escena import _clasificar, _dimensiones


def _demanda(esf: EsfuerzosElemento) -> dict:
    """Demanda del elemento: pu=|axial|, mu=max|My|,|Mz|, vu=max|Vy|,|Vz| (N, N·m, N)."""
    mu = vu = 0.0
    for _s, _n, vy, vz, _t, my, mz in esf.diagrama(21):
        mu = max(mu, abs(my), abs(mz))
        vu = max(vu, abs(vy), abs(vz))
    return {"pu": abs(esf.axial), "mu": mu, "vu": vu}


def calcular_diseno(modelo: ModeloEstructural, fc: float = 21.0, fy: float = 420.0,
                    recubrimiento: float = 0.04) -> dict:
    """DisenoDTO: armado diseñado por fuerzas + demanda + cumple por elemento."""
    if fc <= 0 or fy <= 0 or recubrimiento <= 0:
        raise ValueError("fc, fy y recubrimiento deben ser positivos.")
    errores = modelo.validar()
    if errores:
        raise ValueError("Modelo inválido: " + "; ".join(errores))

    esfuerzos = esfuerzos_elementos(modelo, resolver(modelo))
    nodos = {n.id: n for n in modelo.nodos}
    secs = {s.id: s for s in modelo.secciones}
    d_est = armado._diametro_m(3)
    elementos: list[dict] = []
    for e in modelo.elementos:
        ni, nj = nodos[e.nodo_i], nodos[e.nodo_j]
        b, h = _dimensiones(secs[e.seccion_id])
        if b - 2 * recubrimiento <= 0 or h - 2 * recubrimiento <= 0:
            raise ValueError(f"Recubrimiento {recubrimiento} incompatible con la sección {b}×{h}.")
        esf = esfuerzos[e.id]
        if _clasificar(ni, nj) == "columna":
            d = diseno_elemento.disenar_columna(esf, b, h, fc, fy, recubrimiento)
            long = armado._posiciones_columna(b, h, recubrimiento, d.numero_barra, d.n_barras)
            # Estribo de columna por la regla ACI 25.7.2.1 (el diseño no dimensiona estribos de columna).
            s = max(0.05, min(16 * armado._diametro_m(d.numero_barra), 48 * d_est, min(b, h)))
            tipo, designacion, cumple = "columna", d.disponer, d.cumple
        else:
            d = diseno_elemento.disenar_viga(esf, b, h, fc, fy, recubrimiento)
            # Si la sección es insuficiente a flexión (d.flexion=None) se dibujan barras nominales
            # (2#5) solo para que el elemento se vea; cumple=False / designacion lo marcan.
            num = d.flexion.numero_barra if d.flexion else 5
            n_inf = d.flexion.n_barras if d.flexion else 2
            long = armado._posiciones_viga(b, h, recubrimiento, num, n_inf)
            s = d.estribo.espaciamiento / 1000.0      # mm → m
            tipo, designacion, cumple = "viga", d.disponer, d.cumple
        elementos.append({
            "id": e.id, "i": e.nodo_i, "j": e.nodo_j, "tipo": tipo,
            "long": long,
            "estribo": {"d": d_est, "s": s, "w": b - 2 * recubrimiento, "h": h - 2 * recubrimiento},
            "designacion": designacion, "demanda": _demanda(esf), "cumple": cumple,
        })
    return {"recubrimiento": recubrimiento, "elementos": elementos}
