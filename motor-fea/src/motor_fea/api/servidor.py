"""Servidor FastAPI del visor WebXR (capa frontera). Requiere el extra `api`.

Expone GET /escena (SceneDTO), GET /resultados (deformada + modos), GET /losa
(heatmap de losa) y GET /armado (refuerzo 3D), y sirve los estáticos del visor. El análisis y la
exportación viven en otras capas; este módulo es I/O delgado.
"""
from __future__ import annotations

import json
from pathlib import Path

from fastapi import FastAPI, HTTPException
from fastapi.staticfiles import StaticFiles

from motor_fea.api.contrato import modelo_desde_dict
from motor_fea.core.modelo import (
    Apoyo, CargaNodal, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.viz.escena import exportar_escena
from motor_fea.viz.resultados import calcular_resultados
from motor_fea.viz.resultados_losa import calcular_resultados_losa
from motor_fea.viz.armado import calcular_armado

_STATIC = Path(__file__).resolve().parent.parent / "viz" / "static"


def modelo_ejemplo() -> ModeloEstructural:
    """Pórtico de un vano (4 columnas + 4 vigas de techo, 4×4 m en planta, 3 m de alto)."""
    m = ModeloEstructural()
    m.nodos += [
        Nodo(1, 0, 0, 0), Nodo(2, 4, 0, 0), Nodo(3, 4, 4, 0), Nodo(4, 0, 4, 0),
        Nodo(5, 0, 0, 3), Nodo(6, 4, 0, 3), Nodo(7, 4, 4, 3), Nodo(8, 0, 4, 3),
    ]
    m.materiales.append(Material(1, E=2.0e10))
    m.secciones.append(Seccion(1, area=0.09, inercia_y=6.75e-4,
                               inercia_z=6.75e-4, constante_torsion=1.14e-3))
    columnas = [(1, 5), (2, 6), (3, 7), (4, 8)]
    vigas = [(5, 6), (6, 7), (7, 8), (8, 5)]
    eid = 1
    for i, j in columnas + vigas:
        m.elementos.append(ElementoFrame(eid, i, j, 1, 1))
        eid += 1
    for n in (1, 2, 3, 4):
        m.apoyos.append(Apoyo.empotrado(n))
    for n in (5, 6, 7, 8):                      # carga lateral en +X (deformada visible)
        m.cargas.append(CargaNodal(n, fx=10000.0))
    return m


def cargar_modelo(ruta: str | None) -> ModeloEstructural:
    """Carga el modelo desde un JSON (esquema de contrato.py) o devuelve el de ejemplo."""
    if not ruta:
        return modelo_ejemplo()
    with open(ruta, encoding="utf-8") as f:
        return modelo_desde_dict(json.load(f))


def crear_app(modelo: ModeloEstructural) -> FastAPI:
    """Construye la app FastAPI que sirve `modelo` como escena 3D."""
    app = FastAPI(title="motor-fea · visor estructural")

    @app.get("/escena")
    def escena():
        try:
            return exportar_escena(modelo)
        except ValueError as ex:
            raise HTTPException(status_code=400, detail=str(ex))

    @app.get("/resultados")
    def resultados():
        try:
            return calcular_resultados(modelo)
        except ValueError as ex:
            raise HTTPException(status_code=400, detail=str(ex))

    @app.get("/losa")
    def losa():
        # Losa de ejemplo autónoma: el modelo de barras no tiene concepto de losa,
        # así que /losa no depende de `modelo` a propósito. Se sirve empotrada para
        # que el momento de apoyo (negativo) ejerza el rango completo del mapa de
        # color divergente (azul ↔ rojo), no solo el vano positivo.
        try:
            return calcular_resultados_losa(borde="empotrado")
        except ValueError as ex:
            raise HTTPException(status_code=400, detail=str(ex))

    @app.get("/armado")
    def armado():
        try:
            return calcular_armado(modelo)
        except ValueError as ex:
            raise HTTPException(status_code=400, detail=str(ex))

    # Montar al final: las rutas de API registradas arriba tienen prioridad.
    app.mount("/", StaticFiles(directory=str(_STATIC), html=True), name="static")
    return app


def servir(ruta: str | None = None, host: str = "127.0.0.1", port: int = 8000) -> None:
    """Levanta uvicorn sirviendo el visor. Bloqueante."""
    import uvicorn

    uvicorn.run(crear_app(cargar_modelo(ruta)), host=host, port=port)
