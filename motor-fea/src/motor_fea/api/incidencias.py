"""Router FastAPI de la app de incidencias VR (capa frontera, requiere extra `api`).

Sirve la app estática, clasifica descripciones con IA (pluggable) y persiste el store
de incidencias en JSON. La georreferencia y la clasificación viven en otras capas;
este módulo es I/O delgado.
"""
from __future__ import annotations

import json
from pathlib import Path

from fastapi import APIRouter, HTTPException
from fastapi.responses import FileResponse
from pydantic import BaseModel

from motor_fea.viz.georref import Ancla, escena_a_geo, validar_rd
from motor_fea.viz.incidencias_clasificador import crear_clasificador

_STATIC = Path(__file__).resolve().parent.parent / "viz" / "static" / "incidencias"


class _ClasificarIn(BaseModel):
    descripcion: str


def _validar_doc(doc: dict) -> None:
    """Valida/normaliza el doc: cada incidencia con lat/lng en RD; deriva de vr.pos
    si falta usando doc['georref']. Lanza ValueError si algo cae fuera de RD."""
    g = doc.get("georref")
    ancla = Ancla(**g) if g else None
    for inc in doc.get("incidencias", []):
        lat, lon = inc.get("latitude"), inc.get("longitude")
        if lat is None or lon is None:
            pos = (inc.get("vr") or {}).get("pos")
            if ancla is None or pos is None:
                raise ValueError(f"incidencia {inc.get('id')} sin lat/lng ni georref+vr.pos")
            lat, lon = escena_a_geo(pos["x"], pos["z"], ancla)   # valida RD
            inc["latitude"], inc["longitude"] = lat, lon
        else:
            validar_rd(lat, lon)


def crear_router(store_path: Path, clasificador=None) -> APIRouter:
    router = APIRouter()
    clasif = clasificador or crear_clasificador()
    store_path = Path(store_path)

    @router.get("/incidencias/")
    def app_estatica():
        return FileResponse(_STATIC / "index.html")

    @router.post("/api/incidencias/clasificar")
    def clasificar(body: _ClasificarIn):
        return clasif.clasificar(body.descripcion).to_dict()

    @router.get("/api/incidencias")
    def cargar():
        if not store_path.exists():
            return {"version": 1, "georref": None, "incidencias": []}
        return json.loads(store_path.read_text(encoding="utf-8"))

    @router.post("/api/incidencias")
    def guardar(doc: dict):
        try:
            _validar_doc(doc)
        except (ValueError, KeyError, TypeError, AttributeError) as e:
            raise HTTPException(status_code=400, detail=str(e))
        store_path.write_text(json.dumps(doc, ensure_ascii=False, indent=2), encoding="utf-8")
        return {"ok": True, "n": len(doc.get("incidencias", []))}

    return router
