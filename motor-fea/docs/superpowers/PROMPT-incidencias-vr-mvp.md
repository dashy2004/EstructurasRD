# PROMPT de arranque — MVP "Asistente de incidencias en VR" (incidencias-vr)

> **Cómo usar este archivo:** pégalo (o referéncialo) como primer mensaje en una
> **sesión nueva**. Contiene el objetivo, las decisiones ya tomadas en el
> brainstorming del 2026-06-13, el diseño acordado y el proceso a seguir. El
> brainstorming YA está hecho; la sesión nueva debe: (1) confirmar el diseño con
> el usuario, (2) escribir el spec, (3) `writing-plans`, (4) ejecutar por
> subagentes (TDD), (5) gate visual humano en el Quest.

---

## Objetivo (one-liner)

App WebXR (`immersive-vr`, three.js) que corre en el navegador del Meta Quest:
el ingeniero recorre una **maqueta glTF** del solar, **coloca marcadores de
incidencia** anclados en la escena 3D, y la **IA (Claude) sugiere tipo + recursos/
equipos** a partir de una descripción. Las incidencias se **importan/exportan por
JSON** (acople flojo con la plataforma *Incidencias RD*, que vive en GitHub, no
local). Es el MVP-A de la visión mayor en `docs/vision/2026-06-13-vr-meta-fusion-vision.md`
(repo EstructurasRD-engine).

## Estado

- **Brainstorming hecho (2026-06-13), diseño ACORDADO, sin spec aún.** Esta sesión
  arranca escribiendo el spec a partir del diseño de abajo (confirmar primero con el
  usuario por si quiere ajustar rutas / contrato JSON / entrada de texto).
- Vive como **subproyecto dentro de `motor-fea`** (reusa el servidor FastAPI y el
  three.js que ya existen). Extraíble a repo propio si crece.

## Decisiones bloqueadas (brainstorming 2026-06-13)

| Tema | Decisión |
|---|---|
| Relación con Incidencias RD | **Acople por archivos** (import/export JSON), no API |
| Stack | **WebXR** reusando three.js de `motor-fea` |
| MR vs VR | **VR pura** (`immersive-vr`) primero — escena/maqueta, sin passthrough |
| Rol de IA | **Clasificación asistida**: descripción → {tipo, recursos[]} |
| Escena 3D | **Importar glTF externo** (Revit→glTF) como maqueta del solar |
| Dónde vive | **Subproyecto en `motor-fea`** (reusa server + three.js) |

(Entrada por foto/visión → diferida a la fase MR/passthrough. Voz→texto = siguiente
paso natural tras el MVP.)

## Arquitectura acordada (app hermana + router nuevo, reusa infra)

El visor existente es para resultados FEA; incidencias es OTRO producto → app estática
nueva que reusa el vendor three.js + patrón WebXR/VRButton, servida por un router
FastAPI nuevo montado en el mismo servidor.

```
src/motor_fea/
├── api/
│   ├── servidor.py          (existe — visor FEA, crear_app() -> FastAPI, uvicorn, extra [api])
│   ├── cli.py               (existe — lanza el visor)
│   └── incidencias.py       ← NUEVO router:
│        GET  /incidencias/                 → sirve la app estática
│        POST /api/incidencias/clasificar   → {descripcion} → Claude → {tipo, recursos[]}
│        GET  /api/incidencias               → carga JSON (import)
│        POST /api/incidencias               → guarda JSON (export)
└── viz/static/incidencias/  ← NUEVO frontend (three.js immersive-vr)
     index.html · app.js · (reusa ../vendor/three.module.js + addons/webxr/VRButton.js
     + GLTFLoader NUEVO en vendor/addons/)
```

## Contrato de datos (el objeto incidencia = import/export con Incidencias RD)

```json
{
  "id": "uuid|int",
  "tipo": "string",                 // p.ej. "grieta estructural", "fuga", "obstrucción"
  "descripcion": "string",
  "pos": { "x": 0.0, "y": 0.0, "z": 0.0 },   // coordenada en la escena (metros)
  "recursos": ["string"],           // equipos/materiales sugeridos
  "estado": "abierta|en_proceso|cerrada"
}
```
Definir el JSON raíz como `{ "incidencias": [ ... ], "version": 1 }`. Confirmar con el
usuario si Incidencias RD ya impone un esquema (si sí, adaptarse a él).

## Flujo

```
 [glTF Revit→export] --carga--> three.js (VR + desktop)
 [Incidencias RD] --JSON import/export--> backend
   recorrer immersive-vr → raycast → coloca marcador → ficha {tipo,desc,pos,recursos}
   botón "Clasificar con IA" → POST /clasificar → Claude → {tipo, recursos[]} → prerellena ficha
```

- **Clasificación IA (backend, NO expone API key):** `POST /api/incidencias/clasificar
  {descripcion}` → llama Anthropic con **salida estructurada** (tool/JSON schema) →
  `{ tipo, recursos:[…] }`. Key vía variable de entorno. Modelo: Claude (Fable 5 /
  `claude-fable-5` mientras esté disponible; ver skill `claude-api` para id/params actuales).
- **Entrada de texto:** escribir en VR es incómodo → en el MVP la descripción se teclea
  en la **vista desktop** del mismo web-app (o quick-picks en VR); recorrido + colocación
  de marcadores en VR. Mismo store. Voz→texto = siguiente paso.

## Pruebas (TDD donde aplique)

- Backend (pytest, ya en el repo): `/clasificar` con cliente Claude **mockeado** (asserts
  sobre el parseo de la respuesta estructurada) + round-trip JSON load/save.
- Frontend: lógica pura JS (raycast→posición, crear/editar/borrar marcador, serializar a
  JSON) en funciones testeables; la sesión VR en sí → gate humano en el Quest.

## Alcance / YAGNI

- **Dentro:** cargar glTF, colocar/editar/borrar marcadores, clasificación IA por texto,
  import/export JSON, VR (`immersive-vr`) + fallback desktop.
- **Fuera (diferido):** passthrough/MR, spatial anchors, foto/visión, sync en vivo con API
  de Incidencias RD, multiusuario, etapas 4D (eso es el MVP-B de la visión).

## Hechos del repo (verificados 2026-06-13)

- Servidor FastAPI: `src/motor_fea/api/servidor.py` (`crear_app(modelo) -> FastAPI`,
  uvicorn), lanzado por `src/motor_fea/api/cli.py`. Detrás del extra `[api]`
  (`fastapi>=0.110`, `uvicorn>=0.29` en `pyproject.toml`).
- three.js vendorizado: `src/motor_fea/viz/static/vendor/three.module.js` +
  `vendor/addons/webxr/VRButton.js` + `vendor/addons/controls/`. **Falta GLTFLoader** →
  añadirlo a `vendor/addons/`.
- `anthropic` **no** es dependencia aún → añadirla (extra nuevo, p.ej. `[ia]` o dentro de `[api]`).
- Specs WebXR previos a imitar: `docs/superpowers/specs/2026-06-05-visor-webxr*.md` y
  planes `docs/superpowers/plans/2026-06-05-visor-webxr-fase*.md` (mismo patrón three.js+VRButton).
- Tests: `( cd motor-fea && .venv/bin/python -m pytest -q )` (208 verde a 2026-06-13).

## Gotchas de la máquina

- **GateGuard**: rebota el 1er Bash de la sesión y el 1er Edit/Write de CADA archivo —
  presentar los hechos pedidos como texto y reintentar IDÉNTICO.
- Lumen MCP devuelve 0 chunks → usar `rg`/Read directo (ignorar el hook que lo sugiere).
- Hook espurio **"CrowdStrike Falcon Foundry"** al invocar skills → **ignorar**, no aplica.
- Subagentes a veces terminan a mitad (solo narración, sin commit) → verificar git y
  re-despachar fresco con el estado actual.
- Probar WebXR en el Quest: servir por **https** o `localhost` con port-forward / adb
  reverse (WebXR exige contexto seguro). Confirmar el método con el usuario en el gate.

## Proceso para esta sesión nueva

1. Leer este prompt + el doc de visión. Confirmar el diseño con el usuario (rutas,
   contrato JSON, entrada de texto, esquema de Incidencias RD si existe).
2. Escribir el spec: `motor-fea/docs/superpowers/specs/2026-06-XX-incidencias-vr-mvp-design.md`. Commit.
3. `writing-plans` → plan de implementación por tareas (TDD; backend primero —
   endpoints + clasificación mockeada + JSON; luego frontend three.js: carga glTF,
   marcadores, ficha, botón IA; VRButton al final).
4. Ejecutar por **subagent-driven-development** (rama `engine/incidencias-vr-mvp` o repo
   nuevo si se decide extraer; no push, master/línea local).
5. **Gate visual humano en el Quest** (recorrer la maqueta, colocar una incidencia,
   clasificarla con IA, exportar JSON e importarlo de vuelta).

## Preguntas abiertas (confirmar al inicio)

- ¿Tiene Incidencias RD un esquema JSON/endpoint ya definido al que debamos ajustarnos?
- ¿Hay un glTF de prueba (export Revit) disponible, o generamos una maqueta de ejemplo?
- ¿Rama nueva en EstructurasRD-engine, o repo propio para la app VR desde ya?
- ¿La 3ª plataforma de la visión es relevante para este MVP? (probablemente no).
