"""(De)serialización JSON del modelo canónico del edificio (Rebanada A).

Única capa con I/O del paquete ``edificio``. Contrato versionado: ``version`` en
la raíz. Round-trip exacto ``parse → serialize → parse``.
"""
from __future__ import annotations

import json

from motor_fea.edificio.modelo import (
    CargasGlobales,
    CargasLosa,
    Columna,
    Edificio,
    Losa,
    Metadata,
    Muro,
    Nivel,
    Proyecto,
    Zapata,
)

VERSION_CONTRATO = 1


# --------------------------- parse (dict → objetos) ---------------------------
def _zapata_desde(d: dict | None) -> Zapata | None:
    if not d:
        return None
    return Zapata(float(d["ancho"]), float(d["largo"]), float(d["peralte"]))


def _vertical_desde(d: dict):
    tipo = d.get("tipo")
    if tipo == "columna":
        x, y = d["posicion"]
        return Columna(int(d["id"]), (float(x), float(y)),
                       float(d["seccion"]["base"]), float(d["seccion"]["peralte"]),
                       float(d["cota_base"]), float(d["cota_tope"]), str(d["material"]),
                       _zapata_desde(d.get("zapata")))
    elif tipo == "muro":
        (x1, y1), (x2, y2) = d["linea"]
        return Muro(int(d["id"]), ((float(x1), float(y1)), (float(x2), float(y2))),
                    float(d["seccion"]["espesor"]),
                    float(d["cota_base"]), float(d["cota_tope"]), str(d["material"]),
                    _zapata_desde(d.get("zapata")))
    raise ValueError(f"Tipo de elemento vertical desconocido: {tipo!r}.")


def _losa_desde(d: dict) -> Losa:
    c = d.get("cargas", {})
    return Losa(int(d["id"]), str(d["tipo"]), float(d["espesor"]),
                tuple((float(p[0]), float(p[1])) for p in d["puntos"]),
                CargasLosa(float(c.get("muerta", 0.0)), float(c.get("viva", 0.0))))


def _nivel_desde(d: dict) -> Nivel:
    return Nivel(int(d["id"]), str(d["nombre"]), float(d["cota"]),
                 tuple(_losa_desde(l) for l in d.get("losas", [])))


def _edificio_desde(d: dict) -> Edificio:
    return Edificio(int(d["id"]), str(d["nombre"]),
                    [_nivel_desde(n) for n in d.get("niveles", [])],
                    [_vertical_desde(v) for v in d.get("elementos_verticales", [])])


def proyecto_desde_dict(d: dict) -> Proyecto:
    """Construye un :class:`Proyecto` desde un dict (JSON ya parseado)."""
    v = d.get("version")
    if v != VERSION_CONTRATO:
        raise ValueError(f"Versión de contrato no soportada: {v!r} (esperada {VERSION_CONTRATO}).")
    p = d.get("proyecto", {})
    cg = d.get("cargas_globales", {})
    return Proyecto(
        metadata=Metadata(str(p.get("nombre", "")), str(p.get("autor", "")),
                          str(p.get("codigo_obra", "")), str(p.get("ubicacion", "")),
                          str(p.get("fecha", ""))),
        cargas_globales=CargasGlobales(float(cg.get("muerta_adicional", 0.0)),
                                       float(cg.get("viva", 0.0))),
        combinaciones=[str(c) for c in d.get("combinaciones", [])],
        edificios=[_edificio_desde(e) for e in d.get("edificios", [])],
    )


# ------------------------- serialize (objetos → dict) -------------------------
def _zapata_a(z: Zapata | None) -> dict | None:
    if z is None:
        return None
    return {"ancho": z.ancho, "largo": z.largo, "peralte": z.peralte}


def _vertical_a(v: Columna | Muro) -> dict:
    base = {"id": v.id, "cota_base": v.cota_base, "cota_tope": v.cota_tope,
            "material": v.material}
    if isinstance(v, Columna):
        base.update({"tipo": "columna", "posicion": list(v.posicion),
                     "seccion": {"base": v.base, "peralte": v.peralte}})
    elif isinstance(v, Muro):
        base.update({"tipo": "muro", "linea": [list(p) for p in v.linea],
                     "seccion": {"espesor": v.espesor}})
    else:  # simétrico con _vertical_desde: tipo no soportado falla al escribir
        raise ValueError(f"Tipo de elemento vertical no soportado: {type(v).__name__!r}.")
    z = _zapata_a(v.zapata)
    if z is not None:
        base["zapata"] = z
    return base


def _losa_a(losa: Losa) -> dict:
    return {"id": losa.id, "tipo": losa.tipo, "espesor": losa.espesor,
            "puntos": [list(p) for p in losa.puntos],
            "cargas": {"muerta": losa.cargas.muerta, "viva": losa.cargas.viva}}


def _nivel_a(n: Nivel) -> dict:
    return {"id": n.id, "nombre": n.nombre, "cota": n.cota,
            "losas": [_losa_a(losa) for losa in n.losas]}


def _edificio_a(e: Edificio) -> dict:
    return {"id": e.id, "nombre": e.nombre,
            "niveles": [_nivel_a(n) for n in e.niveles],
            "elementos_verticales": [_vertical_a(v) for v in e.elementos_verticales]}


def proyecto_a_dict(p: Proyecto) -> dict:
    """Serializa un :class:`Proyecto` a un dict JSON-able versionado (round-trip exacto)."""
    m = p.metadata
    return {
        "version": VERSION_CONTRATO,
        "proyecto": {"nombre": m.nombre, "autor": m.autor, "codigo_obra": m.codigo_obra,
                     "ubicacion": m.ubicacion, "fecha": m.fecha},
        "cargas_globales": {"muerta_adicional": p.cargas_globales.muerta_adicional,
                            "viva": p.cargas_globales.viva},
        "combinaciones": list(p.combinaciones),
        "edificios": [_edificio_a(e) for e in p.edificios],
    }


def proyecto_desde_json(texto: str) -> Proyecto:
    """JSON (texto) → :class:`Proyecto`."""
    return proyecto_desde_dict(json.loads(texto))


def proyecto_a_json(p: Proyecto) -> str:
    """:class:`Proyecto` → JSON (texto indentado)."""
    return json.dumps(proyecto_a_dict(p), indent=2, ensure_ascii=False)
