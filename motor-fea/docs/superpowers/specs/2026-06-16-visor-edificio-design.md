# Puente autoría → visor WebXR — Diseño (Rebanada V1)

**Fecha:** 2026-06-16
**Estado:** aprobado (alcance backend; front-end como paso siguiente)
**Predecesores:** B0/B1 (síntesis columnas+muros), C/C2 (cargas), `api/servidor.py` (visor).

## Problema

La síntesis (B0+B1+C+C2) produce una malla `ModeloEstructural` cargada a partir de un `Edificio` autorado. El visor consume `ModeloEstructural` (vía `/escena`, `/resultados`, o POST `/visor` con el JSON del **contrato FEA de bajo nivel**). Falta el camino desde el **contrato de autoría** (`Proyecto`/`Edificio`: columnas/muros/losas + cargas) hasta el visor: hoy el usuario autora un edificio pero no puede verlo sin sintetizar y exportar a mano.

## Alcance

Puente dict→dict + endpoint, sin tocar el front-end (el visor three.js ya pinta `escena`+`resultados`):

```
visor_edificio_dict(proyecto_dict: dict, n: int = 11) -> dict
```
en `api/contrato.py`. Compone los **mismos DTOs que `visor_dict`** (escena + deformada/modos + esfuerzos), pero partiendo del JSON de autoría:
1. `proy = proyecto_desde_dict(proyecto_dict)`.
2. `edi = proy.edificios[0]` (multi-edificio = fusión futura; ≥2 usa el primero, 0 → `ValueError`).
3. `modelo = sintetizar(edi)`; `modelo.cargas.extend(cargas_de_losas(edi, modelo))`.
4. Devuelve `{escena: exportar_escena(modelo), resultados: calcular_resultados(modelo), esfuerzos: esfuerzos_modelo_dict(modelo, n)}`.

Endpoint **POST `/visor-edificio`** en `servidor.py`, espejo de `/visor` (errores de contrato → 400).

**Fuera de alcance:** botón en el front-end que POSTee el JSON de autoría (paso siguiente, sin tests); fusión multi-edificio en una malla; edición de cargas en el visor (WebXR v1).

## Reutilización (cero lógica nueva de cálculo)
`sintetizar`, `cargas_de_losas`, `exportar_escena`, `calcular_resultados`, `esfuerzos_modelo_dict` ya existen y están testeadas. V1 solo las **encadena** y expone el camino de autoría. Las barras de muro (B1) salen como verticales ("columna") con sección visual `t×L` correcta vía `_dimensiones`.

## Garantías y errores
- El modelo debe quedar **apoyado** (columnas/muros con zapata) para que `calcular_resultados` resuelva; si no, matriz singular → `ValueError` → 400 (igual que `/visor`).
- Proyecto sin edificios → `ValueError` legible.
- Determinista (hereda determinismo de síntesis + cargas).

## Testing
`tests/test_visor_edificio.py` (proyecto con 4 columnas en esquinas + zapatas + losa cargada en el techo):
1. **Puente:** `proyecto_a_dict(proy)` → `visor_edificio_dict` → claves `{escena, resultados, esfuerzos}`; `escena.barras` no vacío; pipeline corre sin error (autoría→deformada).
2. **Muro visible:** un `Muro` en el proyecto → aparece como barra en `escena.barras`.
3. **Proyecto vacío → `ValueError`.**
4. **Endpoint** `POST /visor-edificio` (TestClient) → 200 con las 3 claves.
5. **Endpoint inválido** (dict basura) → 400.

## Self-review
| Requisito | Cubierto en |
|---|---|
| Autoría JSON → DTOs del visor | `visor_edificio_dict` + test 1 |
| Columnas y muros se ven | reutiliza `exportar_escena` + test 2 |
| Expuesto por HTTP | `POST /visor-edificio` + tests 4,5 |
| Errores de contrato → 400 | endpoint + test 5 |
| Proyecto vacío manejado | `ValueError` + test 3 |
