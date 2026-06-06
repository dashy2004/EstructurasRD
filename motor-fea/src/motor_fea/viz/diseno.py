"""Cálculo del armado DISEÑADO por combinaciones LRFD para el visor (capa frontera).

Por cada elemento: corre un análisis por caso de carga (``esfuerzos_por_caso``), diseña el
refuerzo cubriendo todos los combos LRFD (``diseno_elemento.disenar_*_combos``) y empaqueta el
armado real + el combo gobernante + su demanda factorada (Pu/Mu/Vu) + cumple, reusando la
derivación de posiciones de ``viz.armado``. Función pura: usa core (casos), viz (escena/armado)
y diseno_elemento (que envuelve aci318); no toca HTTP ni three.js.

Unidades del DTO: metros (posiciones, estribo) y N/N·m (demanda), como la escena.
"""
from __future__ import annotations

from motor_fea import diseno_elemento
from motor_fea.core.casos import esfuerzos_por_caso
from motor_fea.core.modelo import ModeloEstructural
from motor_fea.viz import armado
from motor_fea.viz.escena import _clasificar, _dimensiones


def calcular_diseno(modelo: ModeloEstructural, fc: float = 21.0, fy: float = 420.0,
                    recubrimiento: float = 0.04) -> dict:
    """DisenoDTO: armado por combinaciones LRFD + combo gobernante + demanda factorada por elemento."""
    if fc <= 0 or fy <= 0 or recubrimiento <= 0:
        raise ValueError("fc, fy y recubrimiento deben ser positivos.")
    errores = modelo.validar()
    if errores:
        raise ValueError("Modelo inválido: " + "; ".join(errores))

    epc = esfuerzos_por_caso(modelo)
    nodos = {n.id: n for n in modelo.nodos}
    secs = {s.id: s for s in modelo.secciones}
    d_est = armado._diametro_m(3)
    elementos: list[dict] = []
    for e in modelo.elementos:
        ni, nj = nodos[e.nodo_i], nodos[e.nodo_j]
        b, h = _dimensiones(secs[e.seccion_id])
        if b - 2 * recubrimiento <= 0 or h - 2 * recubrimiento <= 0:
            raise ValueError(f"Recubrimiento {recubrimiento} incompatible con la sección {b}×{h}.")
        esf_por_caso = {caso: epc[caso][e.id] for caso in epc}
        if _clasificar(ni, nj) == "columna":
            d = diseno_elemento.disenar_columna_combos(esf_por_caso, b, h, fc, fy, recubrimiento)
            long = armado._posiciones_columna(b, h, recubrimiento, d.numero_barra, d.n_barras)
            # Estribo de columna por la regla ACI 25.7.2.1 (el diseño no dimensiona estribos de columna).
            s = max(0.05, min(16 * armado._diametro_m(d.numero_barra), 48 * d_est, min(b, h)))
            tipo, designacion, cumple, combo = "columna", d.disponer, d.cumple, d.combo_gobernante
        else:
            d = diseno_elemento.disenar_viga_combos(esf_por_caso, b, h, fc, fy, recubrimiento)
            # Si la sección es insuficiente a flexión (d.flexion=None) se dibujan barras nominales
            # (2#5) solo para que el elemento se vea; cumple=False / designacion lo marcan.
            num = d.flexion.numero_barra if d.flexion else 5
            n_inf = d.flexion.n_barras if d.flexion else 2
            long = armado._posiciones_viga(b, h, recubrimiento, num, n_inf)
            s = d.estribo.espaciamiento / 1000.0      # mm → m
            tipo, designacion, cumple, combo = "viga", d.disponer, d.cumple, d.combo_flexion
        # Demanda factorada del combo gobernante (siempre N/N·m → evita el mismatch de unidades
        # de los dataclasses: columna mu en N·mm vs viga mu en N·m).
        pu, mu, vu = diseno_elemento._demanda_por_combo(esf_por_caso)[combo]
        elementos.append({
            "id": e.id, "i": e.nodo_i, "j": e.nodo_j, "tipo": tipo,
            "long": long,
            "estribo": {"d": d_est, "s": s, "w": b - 2 * recubrimiento, "h": h - 2 * recubrimiento},
            "designacion": designacion,
            "demanda": {"pu": abs(pu), "mu": abs(mu), "vu": abs(vu)},
            "combo": combo, "cumple": cumple,
        })
    return {"recubrimiento": recubrimiento, "elementos": elementos}
