"""Cálculo del armado DISEÑADO por combinaciones LRFD para el visor (capa frontera).

Por cada elemento: corre un análisis por caso (``esfuerzos_por_caso``), diseña el refuerzo cubriendo
todos los combos LRFD (``diseno_elemento.disenar_*_combos``) — incluido el estribo de columna real
(cortante + confinamiento) — y empaqueta el armado + el combo gobernante + su demanda factorada +
cumple, reusando la geometría de ``viz.armado``. Función pura.

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
    """DisenoDTO: armado LRFD + combo gobernante + demanda factorada + estribo de columna diseñado."""
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
            d_estribo_m = armado._diametro_m(d.estribo.numero_barra)
            s = d.estribo.espaciamiento / 1000.0
            tipo, designacion, cumple, combo, estribo_txt = (
                "columna", d.disponer, d.cumple, d.combo_gobernante, d.estribo.disponer)
            muy_e, muz_e, util_e = d.muy, d.muz, d.utilizacion
        else:
            d = diseno_elemento.disenar_viga_combos(esf_por_caso, b, h, fc, fy, recubrimiento)
            num = d.flexion.numero_barra if d.flexion else 5
            n_inf = d.flexion.n_barras if d.flexion else 2
            long = armado._posiciones_viga(b, h, recubrimiento, num, n_inf)
            d_estribo_m = d_est
            s = d.estribo.espaciamiento / 1000.0
            tipo, designacion, cumple, combo, estribo_txt = (
                "viga", d.disponer, d.cumple, d.combo_flexion, "")
            muy_e, muz_e, util_e = 0.0, 0.0, 0.0
        pu, mu, vu = diseno_elemento._demanda_por_combo(esf_por_caso)[combo]
        elementos.append({
            "id": e.id, "i": e.nodo_i, "j": e.nodo_j, "tipo": tipo,
            "long": long,
            "estribo": {"d": d_estribo_m, "s": s, "w": b - 2 * recubrimiento, "h": h - 2 * recubrimiento},
            "designacion": designacion,
            "demanda": {"pu": abs(pu), "mu": abs(mu), "vu": abs(vu)},
            "muy": muy_e, "muz": muz_e, "utilizacion": util_e,
            "combo": combo, "estribo_txt": estribo_txt, "cumple": cumple,
        })
    return {"recubrimiento": recubrimiento, "elementos": elementos}
