# Diseño — Visor estructural WebXR (Fase 1: geometría)

**Fecha:** 2026-06-05
**Estado:** aprobado en brainstorming, pendiente de revisión del spec escrito
**Alcance de este documento:** Fase 1 (MVP de geometría). Las fases 2–4 quedan
esbozadas para validar que la arquitectura las soporta sin rehacer nada.

---

## 1. Objetivo

Añadir al `motor-fea` una opción de **visualización 3D en VR y móvil** del modelo
estructural, accesible desde el navegador (WebXR). Un solo código corre en:

- Visores autónomos (Meta Quest 2/3, Pico) vía su navegador.
- Teléfonos Android/iOS (órbita táctil; VR cardboard opcional a futuro).
- PC con casco cableado (Valve Index, Quest vía Link).

Propósito múltiple acordado: revisión de ingeniería, presentación a clientes,
demo/marketing y educación. La Fase 1 entrega la base geométrica sobre la que se
montan deformada, mapas de color y armado en fases siguientes.

## 2. Decisiones tomadas (brainstorming)

| Decisión | Elección | Razón |
|---|---|---|
| Plataforma | **WebXR** (navegador) | Un código → Quest + celular + PC-VR, sin apps nativas ni tiendas. |
| Stack visor | **three.js vanilla** (CDN + import-map) | Cero toolchain JS; un dev Python lo mantiene. WebXR integrado. |
| Entrega | **Servidor local en vivo** (FastAPI) | Re-análisis al vuelo; FastAPI ya está declarado en `pyproject` (extra `api`). |
| Contenido MVP | **Geometría** (esqueleto de pórtico) | Base sobre la que se construye todo lo demás. |

## 3. Arquitectura

Tres unidades nuevas, todas en la **capa de frontera**. No tocan `core/` ni
`normativa/` (se respeta la regla "las capas no se mezclan" del README).

```
ModeloEstructural ──► viz/escena.py ──► SceneDTO (dict/JSON)
                       (función pura)        │
                                             ▼
                       api/servidor.py ──► GET /escena  (FastAPI)
                       (I/O delgado)    ──► sirve viz/static/*
                                             │
                                             ▼
                       viz/static/app.js ── fetch /escena ─► three.js ─► WebXR
```

### 3.1 `src/motor_fea/viz/escena.py` — exportador (función pura)

Convierte un `ModeloEstructural` en un `SceneDTO` listo para render. **Sin
dependencias de web ni de NumPy** — se testea con asserts normales.

Responsabilidades:

1. Serializar nodos: `{id, p:[x,y,z]}`.
2. Serializar barras: por cada `ElementoFrame`, emitir `{id, i, j, tipo, b, h}`.
   - **Clasificación columna/viga**: por la orientación del elemento. Se calcula
     el vector `i→j`; si la componente vertical domina (|Δz| es la mayor de las
     tres en valor absoluto) → `"columna"`, si no → `"viga"`. Exacto y barato.
   - **Dimensiones de sección `b`,`h`**: `Seccion` guarda A, Iy, Iz, J (no b/h).
     Se derivan resolviendo `b·h = A` y `b·h³/12 = Iz` →
     `h = sqrt(12·Iz/A)`, `b = A/h`. Si el resultado no es físico
     (A≤0, Iz≤0, o b/h fuera de un rango razonable, p.ej. relación > 50:1), se
     usa un grosor visual por defecto `B_VIS = H_VIS = 0.20 m`. Esto cubre las
     secciones degeneradas de test (p.ej. `cadena_2gdl` con I=1e-8).
3. Calcular `bbox` (min/max de las coords de los nodos) para auto-encuadre.
4. Losas: **vacío en Fase 1** (el modelo de pórtico no tiene entidad losa; viven
   en `diafragma`/`losa_fem`). El campo `losas` se emite como `[]` y queda como
   punto de extensión para una sobrecarga futura que reciba datos de diafragma.

Firma:

```python
def exportar_escena(modelo: ModeloEstructural) -> dict: ...
```

Valida el modelo con `modelo.validar()` antes de exportar; si hay errores,
lanza `ValueError` con el mensaje agregado (mismo patrón que `solver.resolver`).

### 3.2 `src/motor_fea/api/servidor.py` — servidor (I/O delgado)

App FastAPI mínima. Se importa solo cuando FastAPI está instalado (extra `api`).

Endpoints Fase 1:

- `GET /escena` → JSON del `SceneDTO`. El modelo a servir se carga al arrancar
  desde una ruta pasada por CLI (`--serve modelo.json`) o, en su defecto, un
  modelo de ejemplo embebido para que `--serve` sin argumentos muestre algo.
  - Modelo inválido → **HTTP 400** con `{detail: "<errores de validar()>"}`.
- Estáticos: monta `viz/static/` en `/` (sirve `index.html`, `app.js`).

Reservados para fases futuras (no implementar ahora, solo dejar el esqueleto de
rutas documentado): `GET /resultados?caso=...` (deformada/modos),
`GET /losa/...` (mapas de color).

### 3.3 `src/motor_fea/viz/static/{index.html, app.js}` — visor WebXR

three.js vanilla por import-map desde CDN (sin build). Responsabilidades de
`app.js`:

1. `fetch('/escena')` → parsear `SceneDTO`.
2. Construir la escena:
   - Nodos opcionales como puntos pequeños (ayuda de depuración, toggle).
   - Barras como `Mesh` de prisma (`BoxGeometry` escalada a `b×h×L` y orientada
     i→j) o `Line` en modo bajo consumo. Material por `tipo`
     (columna vs viga) para lectura rápida.
   - Losas: planos semitransparentes (vacío en Fase 1).
   - Rejilla de piso (`GridHelper`) + ejes (`AxesHelper`) como referencia.
3. Auto-encuadrar la cámara usando `bbox`.
4. Controles con **degradación elegante**:
   - Si `navigator.xr` y la sesión inmersiva está disponible → mostrar
     `VRButton`; locomoción por **teletransporte** (raycast del control +
     gatillo). Escala 1:1.
   - Si no → `OrbitControls` (girar/zoom con dedo o ratón). El botón VR se
     oculta.

## 4. Contrato `SceneDTO`

JSON plano, render-agnóstico (cualquier visor futuro lo consume igual):

```jsonc
{
  "unidades": "m",
  "bbox": { "min": [x, y, z], "max": [x, y, z] },
  "nodos": [ { "id": 1, "p": [x, y, z] } ],
  "barras": [
    { "id": 10, "i": 1, "j": 2, "tipo": "columna", "b": 0.30, "h": 0.30 }
  ],
  "losas": []
}
```

Estable y extensible: las fases 2–4 añaden campos/endpoints (`/resultados`,
color por elemento) sin romper este contrato.

## 5. CLI

Extender `api/cli.py` con un flag nuevo, en el estilo de los existentes:

```
motor-fea --serve [MODELO.json] [--host 127.0.0.1] [--port 8000]
```

- Sin `MODELO.json` → sirve un modelo de ejemplo embebido.
- Importa `servidor` perezosamente dentro del handler; si falta FastAPI, imprime
  un mensaje claro ("instala el extra: pip install -e '.[api]'") y retorna 1.
  Así el CLI base sigue funcionando sin el extra `api`.

## 6. Manejo de errores

| Situación | Respuesta |
|---|---|
| Modelo inválido (`validar()` no vacío) | `exportar_escena` lanza `ValueError`; el endpoint lo traduce a HTTP 400 con el detalle. |
| Sección degenerada (b/h no físico) | Grosor visual por defecto; no falla. |
| FastAPI no instalado | CLI: mensaje accionable + exit 1. No rompe `--analyze`. |
| Dispositivo sin WebXR | Visor degrada a `OrbitControls`; oculta el botón VR. |
| `/escena` no responde (red) | `app.js` muestra un mensaje de error en pantalla, no una pantalla en blanco. |

## 7. Testing

| Qué | Cómo | Notas |
|---|---|---|
| `viz/escena.py` | Tests puros con `modelos_ref.voladizo()`: 1 barra clasificada `"viga"` (horizontal en X), `b≈h≈0.30` derivadas de A=0.09 e I=0.30⁴/12, bbox = `[0,0,0]..[3,0,0]`. Un caso vertical sintético → `"columna"`. Un caso degenerado (`cadena_2gdl`) → cae al grosor por defecto. Modelo inválido → `ValueError`. | Estilo del repo; corre con stdlib, sin extras. |
| `api/servidor.py` | `fastapi.testclient.TestClient`: `GET /escena` → 200 + claves esperadas; modelo inválido → 400. | `pytest.importorskip("fastapi")` al inicio del módulo → se salta si el extra `api` no está, preservando "la suite corre con stdlib pura". |
| Visor JS | Sin test unitario. Smoke manual documentado: abrir en celular (órbita) y en Quest (VR + teletransporte). | El costo de testear three.js no compensa para este alcance. |

**Criterio de aceptación Fase 1:**

1. `PYTHONPATH=src pytest -q` sigue en verde (108 actuales + nuevos puros).
2. Con el extra `api`: `motor-fea --serve fixture.json` levanta el server;
   `GET /escena` devuelve el `SceneDTO` correcto.
3. Abrir la URL en un teléfono muestra el esqueleto del pórtico y se puede orbitar.
4. Abrir la URL en un Quest permite entrar en VR y moverse por teletransporte a
   escala real.

## 8. Roadmap (fases siguientes — fuera de este spec)

| Fase | Entrega | Reusa |
|---|---|---|
| 2 | Deformada exagerada + animación de modos 1–3 (slider de exageración). | `solver.resolver`, `modal.modos` |
| 3 | Mapas de color en losas (momento/deflexión) + tocar→valor numérico. | `losa_fem` |
| 4 | Barras de refuerzo 3D dentro de secciones. | `aci318` |

Cada fase será su propio ciclo spec → plan → implementación.

## 9. Archivos afectados (Fase 1)

**Nuevos**
- `src/motor_fea/viz/__init__.py`
- `src/motor_fea/viz/escena.py`
- `src/motor_fea/viz/static/index.html`
- `src/motor_fea/viz/static/app.js`
- `src/motor_fea/api/servidor.py`
- `tests/test_escena.py`
- `tests/test_servidor.py`

**Modificados**
- `src/motor_fea/api/cli.py` (flag `--serve`)
- `pyproject.toml` (incluir `viz/static/*` como package data; el extra `api` ya existe)
- `README.md` (documentar `--serve` y el visor)
