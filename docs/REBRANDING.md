# Rebrand: LosasPlus → EstructurasRD

**Fecha:** Mayo 2026 · **Fase del roadmap:** Fase 0 del Plan de Expansión 3D.

## Contexto

El producto que originalmente nació como **LosasPlus** —editor de losas
con cálculo en vivo apoyado en el motor `Losas.exe` de F. Perdomo
(Pieper-Martens)— evolucionó durante 7 fases hasta convertirse en una
suite estructural integral que cubre:

- Sistemas y muros (Fases 1–2)
- Vigas continuas y verificación RC (Fases 3–4)
- Columnas RC con diagrama P-M uniaxial 2D (Fase 5)
- Zapatas aisladas con verificación ELU biaxial (Fase 6)
- Auditoría cruzada del proyecto y dashboard ejecutivo (Fase 7)

El nombre **LosasPlus** se quedó corto para describir el alcance actual,
y aún más para la siguiente expansión planificada (visor 3D con
HelixToolkit + interoperabilidad SAF). El repositorio en GitHub ya se
llamaba `dashy2004/EstructurasRD`, así que la marca paraguas existía de
facto.

## Decisión

A partir de la Fase 0 (esta iteración), el producto se presenta bajo
**EstructurasRD** como marca paraguas. **LosasPlus** persiste como
sub-marca del módulo histórico (motor de losas Pieper-Martens, lienzo
2D, plurinivel de losas), de manera análoga a cómo MemoriaPlus persiste
como sub-marca del módulo de memorias `.docx`.

## Lo que cambia (visible al usuario)

- **Título de la ventana** (`MainViewModel.TituloVentana`) — ahora
  prefija `EstructurasRD · …` en lugar de `LosasPlus · …`.
- **`Version`** mostrada en la sidebar — `EstructurasRD v0.7.0 — Suite
  Estructural`.
- **`CopyrightTexto`** del statusbar — incluye ambos nombres y el
  crédito del motor de losas.
- **README.md** — nuevo título `EstructurasRD`, descripción del alcance
  completo de la suite, sección de módulos, y URLs corregidas a
  `dashy2004/EstructurasRD` (antes apuntaban incorrectamente a
  `dashy2004/LosasPlus`).

## Lo que NO cambia (decisión consciente)

- **Namespaces `LosasPlus.*`** del código fuente. Un rename mecánico
  rompería temporalmente los 729 tests y embarrarí­a el git blame sin
  beneficio visible para el usuario final. Los namespaces son una
  decisión técnica histórica que vive sólo en código.
- **Nombre del ejecutable `LosasPlus.exe`**. Cambiar `AssemblyName`
  afectaría deployments existentes, atajos del menú inicio y referencias
  externas (incluido `MemoriaPlus.exe` que invoca a `LosasPlus.exe` en
  el flujo "Generar memoria"). Decisión explícita del usuario.
- **Nombre de la solución (`LosasPlus.sln`)** y de los proyectos
  (`LosasPlus.csproj`, `LosasPlus.Core.csproj`, etc.).
- **`MemoriaPlus`** mantiene su nombre como módulo independiente
  (memorias de cálculo `.docx`).

## Cómo se mapea LosasPlus dentro de EstructurasRD

| Nivel | Nombre |
|---|---|
| Marca paraguas | **EstructurasRD** |
| Módulo histórico (losas Pieper-Martens) | **LosasPlus** |
| Módulo de memorias `.docx` | **MemoriaPlus** |
| Módulos analíticos integrados | Vigas Continuas, Columnas, Zapatas, Auditoría Cruzada |
| Futuros módulos (post-rebrand) | Visor 3D, Interop SAF |

## Referencias cruzadas

- Plan completo de expansión 3D: `C:\Users\emilg\.claude\plans\ethereal-snuggling-dahl.md`
- Repo GitHub: <https://github.com/dashy2004/EstructurasRD>
- Branch del rebrand: `feat/rebrand-estructurasrd`
- Commit del rebrand: ver `git log feat/rebrand-estructurasrd`
