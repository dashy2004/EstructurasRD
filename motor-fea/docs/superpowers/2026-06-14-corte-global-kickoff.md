# KICKOFF — #4b Corte global (plano de sección)

> **Para una sesión limpia.** Lee este archivo primero. Es el punto de arranque de **#4b**, el
> segundo sub-proyecto de "#4 vista en secciones". **#4a (sección de miembro) ya está hecho y
> mergeado.** Este doc NO es un spec: enmarca el problema, lista lo reutilizable y las preguntas
> abiertas para que arranques el ciclo **brainstorming → spec → plan → subagent-driven** sin
> volver a derivar el contexto.
>
> **Primer paso en la sesión limpia:** invocar `superpowers:brainstorming` con este doc como
> contexto y resolver las "Preguntas abiertas" (§5) con el usuario, una a una.

**Fecha:** 2026-06-14
**Rama:** `engine/shell-web-webxr` (worktree host-managed en `~/Downloads/EstructurasRD-engine`).
`master` local == rama (fast-forward; NO se pushea a `origin/main`, que es la línea WPF vieja sin
ancestro común). Mismo flujo que #1–#4a.

---

## 1. Qué es #4b

Un **plano de corte global** que **rebana el modelo 3D** del pórtico y muestra la **rebanada 2D**
(como una planta o una elevación). A diferencia de #4a (que corta **un** miembro elegido por pick),
#4b corta **todos** los miembros que atraviesan un plano y compone la vista 2D del conjunto.

```
plano de corte (p.ej. z = 1.5 m)
        ╱─────────╱
   ════╪═════════╪════   ← intersección con vigas/columnas
      ╱│        ╱│
     ● │       ● │       cada miembro que cruza → un punto/sección en la rebanada
       │         │
  vista 2D = lo que el plano atraviesa (planta o elevación)
```

## 2. Contexto / decisiones ya tomadas (de la descomposición de #4)

En el brainstorming de #4 (ver `specs/2026-06-14-vista-secciones-design.md` §1) se acordó:
- **#4** = `#4a` sección de miembro (**HECHO**) + `#4b` corte global (**este**).
- #4b es **más pesado**: requiere **intersección geometría-vs-plano**, un **gizmo/control del plano**
  y **posiblemente un helper de server** (a decidir en brainstorming; lo demás del motor es front-only).
- El usuario, al enmarcar #4, pidió "ambos / los tres propósitos": **revisar armado, leer esfuerzos,
  documentar/exportar**. #4a cubrió armado+esfuerzos+export por miembro; #4b lo lleva al **conjunto**.

## 3. Datos disponibles (DTOs, NO modificar salvo que el brainstorming decida un helper de server)

- **`escena`** (`GET /escena`): `nodos[]` `{id, p:[x,y,z]}`; `barras[]` `{i, j, b, h, tipo, id}`;
  `bbox {min,max}`. En el front: `basePos[id]` = `THREE.Vector3` por nodo; `barras[]` = `{mesh,i,j,id,b,h}`.
- **`esfuerzos`** (`GET /esfuerzos`, `POST /visor`): `elementos[]` `{id, longitud,
  diagrama:[[s,N,Vy,Vz,T,My,Mz],...]}`. La interpolación lineal en `s` ya existe en el front como
  `esfuerzosEnEstacion(el, s)` (app.js, de #4a) — exacta (cargas nodales → N/V constantes, M lineal).
- **`diseño`/`armado`** (`GET /diseno`, `GET /armado`, solo modo-ejemplo): `elementos[]` por `id` con
  `long:[{x,y,d}]` + `estribo:{d,s,w,h}` (+ diseño: `tipo,designacion,cumple`). Fuente del armado de
  una sección: `datosSeccion(id,s,L)` en app.js ya hace diseño→armado→bare.
- Unidades: m; fuerzas N (→kN), momentos N·m (→kN·m). Convención interna de esfuerzo: tracción +.

## 4. Qué reutilizar (ya construido en #1–#4a)

- **`viz/static/svgutil.js`**: `nodo(tag,attrs)`, `descargarSVG(svg,nombre)`, `descargarPNG(svg,nombre)`
  (rasteriza a canvas nativo, vendorless). → reúsalo para dibujar y exportar la rebanada.
- **`viz/static/seccion2d.js`**: `seccionSVG(datos)` puro (corte b×h + armado + cue + 6 esfuerzos de un
  miembro). → reusable si la rebanada muestra la sección de cada miembro cortado.
- **`viz/static/app.js`** — patrón de **modo overlay** ya probado 3 veces (losa/diseño, diagramas,
  sección): entrada nueva en `selEstado` (en `renderEscena` cuando hay `esfuerzos`); `entrarX()`;
  ramas en `setEstado` (+ `veniaEspecial`); teardown en `resetOverlays` y `limpiarEscena`; geometría
  3D con `dispose` (ver `construirCintas`/`disposeCintas` de #3 y `construirAnilloSeccion`/
  `disposeAnillo` de #4a); `exag` slider; pick por raycaster sobre `barras`.
- **Matemática de eje de miembro** (de `construirCintas`/`posicionarAnillo`): `axis = normalize(vj-vi)`,
  `t1 = cross(axis, up)` con fallback, `t2 = cross(axis,t1)` — útil para orientar el plano y proyectar.
- En estado overlay no-deformado, `despNodo` devuelve 0 → las barras quedan en base (no pelean con el
  loop de animación). Mismo truco para #4b.

## 5. Preguntas abiertas para el brainstorming (resolver con el usuario, una a una)

1. **Orientación del plano:** ¿solo **ejes-alineados** (horizontal `z=cte` = planta; vertical
   `x/y=cte` = elevación) — más simple y cubre planta/elevación — o **plano arbitrario**? (Recomendación
   de arranque: ejes-alineados; el arbitrario puede ser mejora futura.)
2. **Control del plano:** ¿un **dropdown de orientación + slider de posición** (reusa el patrón
   `exag`), o un **gizmo 3D arrastrable**? (El slider es consistente y barato; el gizmo es más rico.)
3. **Qué muestra la rebanada 2D:**
   - (a) **planta/elevación esquemática**: cada miembro cortado como un punto/rectángulo en su posición
     proyectada sobre el plano, opcionalmente coloreado por un esfuerzo en el corte; o
   - (b) **galería de secciones**: la sección transversal (`seccionSVG`) de cada miembro cortado; o
   - (c) **híbrido**: esquema 2D + pick de un miembro cortado → su `seccionSVG`.
   (Pensar qué sirve al propósito "documentar".)
4. **Front-only vs helper de server:** ¿la intersección barra-plano y la proyección se hacen 100% en
   el front (preferido, como #4a), o conviene un endpoint que devuelva los cortes? (Por defecto:
   front-only; el cálculo es trivial — intersección de segmento `vi→vj` con un plano.)
5. **Esfuerzos en el corte:** ¿anotar N/V/M de cada miembro en su intersección (vía
   `esfuerzosEnEstacion` en `s = fracción del cruce`), o solo geometría? (#4a ya mostró el valor de los
   esfuerzos por sección.)
6. **Export:** confirmar SVG + PNG de la rebanada (reusa `svgutil`); ¿algo más (DXF/medidas)? (YAGNI).
7. **Marcador 3D:** ¿dibujar el plano de corte en la escena 3D (un `PlaneHelper`/quad semitransparente)
   + resaltar los miembros cortados? (Consistente con el anillo de #4a.)
8. **Casos borde:** miembros contenidos en el plano; miembros que no cruzan; cero cruces; el plano
   fuera del bbox. Definir respuestas en el spec (§10 del patrón de specs previos).

## 6. Esbozo técnico (NO vinculante — material para el brainstorming)

- **Intersección segmento-plano:** para cada barra con `vi=basePos[i]`, `vj=basePos[j]` y un plano
  `n·x = d`: `f = (d - n·vi) / (n·(vj-vi))`; si `f ∈ [0,1]` la barra cruza en `P = lerp(vi,vj,f)` y la
  estación es `s = f · longitud`. Plano eje-alineado → `n` es un eje canónico y `f` es trivial.
- **Proyección a 2D:** elegir dos ejes del plano (para `z=cte`: x,y del mundo = planta). Cada `P` da un
  punto 2D; cada miembro cortado se dibuja ahí (punto/rect orientado con su sección, o `seccionSVG`).
- **Reusos directos:** `esfuerzosEnEstacion(el, s)` (ya existe) para los esfuerzos en el cruce;
  `datosSeccion(id, s, L)` para el armado; `seccionSVG`/`svgutil` para dibujo+export.

## 7. Limitación heredada (documentar igual que #3 §6.3 / #4a §9)

El front no recibe el **triedro local** real de cada elemento (`vector_referencia`) en el DTO `escena`,
así que la **orientación** de cada sección dentro de la rebanada será aproximada (eje+up global), como
en #3/#4a. Los **valores** (posición del corte, esfuerzos) son exactos. El arreglo exacto = añadir el
triedro local al `escena` DTO (cambio de server) — candidato a evaluar si #4b lo necesita de verdad.

## 8. Definición de hecho (esperada, a confirmar en el spec)

- Modo `"corte"` (o similar) en el `<select>`, en modo-ejemplo y custom.
- Control del plano (orientación + posición) que actualiza la rebanada en vivo.
- Rebanada 2D de los miembros cortados (según decisión §5.3), con export SVG/PNG.
- Marcador 3D del plano + miembros cortados resaltados.
- Teardown limpio (sin residuos al cambiar de modo / cargar otro modelo), patrón de #3/#4a.
- Verificación manual en navegador real (Playwright); sin runner JS (YAGNI). 225 tests Python siguen verde.
- Mergeado a `master` local por fast-forward; rama conservada; sin push a `main`.

## 9. Punteros

- Spec #4a: `docs/superpowers/specs/2026-06-14-vista-secciones-design.md`
- Plan #4a: `docs/superpowers/plans/2026-06-14-vista-secciones.md`
- Spec #3 (diagramas, patrón de overlay 3D + limitación §6.3): `docs/superpowers/specs/2026-06-14-diagramas-pvm-design.md`
- Código: `src/motor_fea/viz/static/{app.js, svgutil.js, seccion2d.js, diagramas2d.js, index.html}`
- Server (frontera, solo si se decide un helper): `src/motor_fea/api/{servidor.py, contrato.py}`,
  `src/motor_fea/viz/{escena.py, armado.py, diseno.py}`
- Correr el visor: `.venv/bin/python -m motor_fea.api.cli --serve --port 8000` → http://127.0.0.1:8000/
- Gotchas del entorno: ver `.remember/remember.md` (GateGuard rebota 1er Bash / 1ª edición / comandos
  destructivos → presentar hechos y reintentar; sweeps sintéticos de pointer → errores
  `setPointerCapture` de OrbitControls, artefacto del harness; recargar con `?v=N` por la caché).
