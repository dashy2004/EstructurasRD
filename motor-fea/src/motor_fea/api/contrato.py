"""Contrato de (de)serialización JSON del motor — frontera con el C# (ADR-0001).

Esta es la capa 3: traduce entre el JSON del modelo/resultados y los objetos
puros de ``motor_fea.core``. El núcleo de análisis no conoce JSON; sólo este
módulo y el CLI tocan I/O.

Esquema del modelo (entrada)::

    {
      "nodos":      [{"id": 1, "x": 0.0, "y": 0.0, "z": 0.0}, ...],
      "materiales": [{"id": 1, "E": 2.0e10, "nu": 0.2, "densidad": 2400.0}, ...],
      "secciones":  [{"id": 1, "area": .., "inercia_y": .., "inercia_z": ..,
                      "constante_torsion": ..}, ...],
      "elementos":  [{"id": 1, "nodo_i": 1, "nodo_j": 2, "material_id": 1,
                      "seccion_id": 1, "vector_referencia": [0,0,1]}, ...],
      "apoyos":     [{"nodo_id": 1, "ux": true, "uy": true, ...}, ...],
      "cargas":     [{"nodo_id": 2, "fx": 0, "fy": 0, "fz": -1000, ...}, ...]
    }

Esquema de resultados (salida)::

    {
      "n_gdl": 12,
      "desplazamientos": {"1": [ux,uy,uz,rx,ry,rz], "2": [...]},
      "reacciones":      {"1": [fx,fy,fz,mx,my,mz]}
    }
"""
from __future__ import annotations

import json

from motor_fea.core.modelo import (
    Apoyo,
    CargaNodal,
    ElementoFrame,
    Material,
    ModeloEstructural,
    Nodo,
    Seccion,
)
from motor_fea.core.solver import ResultadoAnalisis, resolver


def modelo_desde_dict(d: dict) -> ModeloEstructural:
    """Construye un :class:`ModeloEstructural` desde un dict (JSON ya parseado)."""
    m = ModeloEstructural()
    for n in d.get("nodos", []):
        m.nodos.append(Nodo(int(n["id"]), float(n["x"]), float(n["y"]), float(n.get("z", 0.0))))
    for mat in d.get("materiales", []):
        m.materiales.append(Material(int(mat["id"]), float(mat["E"]),
                                     float(mat.get("nu", 0.2)), float(mat.get("densidad", 2400.0))))
    for s in d.get("secciones", []):
        m.secciones.append(Seccion(int(s["id"]), float(s["area"]), float(s["inercia_y"]),
                                   float(s["inercia_z"]), float(s["constante_torsion"])))
    for e in d.get("elementos", []):
        vr = e.get("vector_referencia", [0.0, 0.0, 1.0])
        m.elementos.append(ElementoFrame(int(e["id"]), int(e["nodo_i"]), int(e["nodo_j"]),
                                         int(e["material_id"]), int(e["seccion_id"]),
                                         (float(vr[0]), float(vr[1]), float(vr[2]))))
    for a in d.get("apoyos", []):
        m.apoyos.append(Apoyo(int(a["nodo_id"]),
                              bool(a.get("ux", False)), bool(a.get("uy", False)), bool(a.get("uz", False)),
                              bool(a.get("rx", False)), bool(a.get("ry", False)), bool(a.get("rz", False))))
    for c in d.get("cargas", []):
        m.cargas.append(CargaNodal(int(c["nodo_id"]),
                                   float(c.get("fx", 0.0)), float(c.get("fy", 0.0)), float(c.get("fz", 0.0)),
                                   float(c.get("mx", 0.0)), float(c.get("my", 0.0)), float(c.get("mz", 0.0))))
    return m


def modelo_a_dict(m: ModeloEstructural) -> dict:
    """Serializa un :class:`ModeloEstructural` a un dict JSON-able (round-trip exacto)."""
    return {
        "nodos": [{"id": n.id, "x": n.x, "y": n.y, "z": n.z} for n in m.nodos],
        "materiales": [{"id": x.id, "E": x.E, "nu": x.nu, "densidad": x.densidad} for x in m.materiales],
        "secciones": [{"id": s.id, "area": s.area, "inercia_y": s.inercia_y,
                       "inercia_z": s.inercia_z, "constante_torsion": s.constante_torsion} for s in m.secciones],
        "elementos": [{"id": e.id, "nodo_i": e.nodo_i, "nodo_j": e.nodo_j,
                       "material_id": e.material_id, "seccion_id": e.seccion_id,
                       "vector_referencia": list(e.vector_referencia)} for e in m.elementos],
        "apoyos": [{"nodo_id": a.nodo_id, "ux": a.ux, "uy": a.uy, "uz": a.uz,
                    "rx": a.rx, "ry": a.ry, "rz": a.rz} for a in m.apoyos],
        "cargas": [{"nodo_id": c.nodo_id, "fx": c.fx, "fy": c.fy, "fz": c.fz,
                    "mx": c.mx, "my": c.my, "mz": c.mz} for c in m.cargas],
    }


def resultado_a_dict(r: ResultadoAnalisis) -> dict:
    """Serializa el resultado del análisis a un dict JSON-able."""
    return {
        "n_gdl": r.n_gdl,
        "desplazamientos": {str(k): list(v) for k, v in r.desplazamientos.items()},
        "reacciones": {str(k): list(v) for k, v in r.reacciones.items()},
    }


def analizar_dict(modelo_dict: dict) -> dict:
    """Pipeline completo dict→dict: deserializa, resuelve y serializa el resultado."""
    modelo = modelo_desde_dict(modelo_dict)
    return resultado_a_dict(resolver(modelo))


def analizar_json(texto: str) -> str:
    """Pipeline texto→texto: JSON del modelo → JSON de resultados (indentado)."""
    return json.dumps(analizar_dict(json.loads(texto)), indent=2)
