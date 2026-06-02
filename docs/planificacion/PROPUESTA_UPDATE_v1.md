# Propuesta de actualización LosasPlus → Suite Estructural

> **Documento de planificación técnica.** Versión 1.0 · 2026-05-21.
> Audiencia: equipo de desarrollo (Claude Code u otro).
> Pre-requisito: lectura previa de `docs-analisis-disest/{LOSAS,VIGAS,COLUMNAS,DISENO_VIGAS,ZAPATAS}.md` (carpeta hermana a este repo).

---

## 0. Resumen ejecutivo (1 página)

LosasPlus v0.7 es hoy un **editor moderno + generador de memoria** para losas, montado encima del motor `Losas.exe` (F. Perdomo, Pieper-Martens). El objetivo de esta actualización es **convertirlo en una suite completa de diseño estructural** que reemplace funcionalmente toda la suite DISEST (Losas, VigaContinua, Columna, Diseno, Interaccion, Zapata, AnaZap), añadiendo:

1. **Módulos faltantes**: Vigas continuas, Columnas (biaxial + esbeltez), Diseño de secciones RC (viga-T, cortante, torsión), Zapatas (rectangulares y poligonales), Diagramas de interacción P-M.
2. **Visualización 3D** del modelo completo (no solo el plano de losas).
3. **Apartado educativo por sección**: fórmulas reales, derivación paso a paso, citas a normas y bibliografía técnica verificable. Esto es lo que diferencia el producto de un "Excel modernizado" o de "código IA-generado".
4. **Integración con los motores DISEST**: seguir usando `Losas.exe`, `VigaContinua.exe`, `Columna.exe`, `Zapata.exe`, `AnaZap.exe` como kernels de cálculo cuando estén disponibles, y tener motores propios de respaldo cuando el usuario no tenga la suite DISEST instalada.
5. **Productividad masiva**: lo que tomaba 30 minutos por losa escribiendo `.DL` a mano y otros 30 minutos por viga en VigaContinua, debe tomar segundos con paste-from-Excel, dibujo por planta, propagación entre niveles, multi-edición.

Trade-off principal: el alcance crece de "wrapper de Losas.exe" a "CAD estructural ligero + diseñador RC + generador de memorias + tutor". El roadmap propuesto en §7 fragmenta el trabajo en 7 fases (~6 a 12 meses calendario en una persona; menos con paralelización).

---

## 1. Análisis del estado actual (v0.7)

### 1.1 Inventario del repo

```
LosasPlus/
├── src.Core/                  Librería .NET 8, sin WPF — 64 archivos .cs
│   ├── Models/                Proyecto, Sistema, Losa, CargasGlobales, SalidaPerdomo
│   ├── Models/Cad/            Entidades de dibujo (Linea, Polilinea, Arco, Punto, Texto, Muro, PdfReferencia)
│   ├── Calculo/CalculoEngine.cs       (25 KB) cálculo de espesor + αfm + cuantías
│   ├── Services/                       parser .DL/.TXT, DLDoctor, DXF import, Excel paste, plugin host, …
│   ├── Validation/Rules/      R-001 espesor/carga, espesor vs calculado, aspecto, tipo válido
│   ├── Persistence/           ProyectoSerializer (.lpx.json), backup, configs
│   └── Generation/            MemoriaGenerator (.docx, plurinivel)
│
├── src/                       App WPF LosasPlus (editor + visor)
│   ├── MainWindow.xaml (99 KB) + .cs (22 KB)
│   ├── Views/Cad/             CadCanvasHost (95 KB), CadView (44 KB), PaletaMuros…
│   ├── Views/                  ReglamentoView, DLDoctor, TxtTabla, TipoLosaIcon, …
│   └── Services/              PdfImportador, ReglamentoService, ThemeCustomizer
│
├── src.Memoria/               App WPF MemoriaPlus (standalone, sólo genera .docx)
│   └── Views/                  Datos generales, Niveles, Cargas, Generar, Explorador
│
├── src.UI.Shared/             Iconos SVG compartidos (26 tipos), converters
├── tests/LosasPlus.Tests/     501 tests xUnit
├── plugins/                   ejemplo.csx, excel-export.csx
├── docs/referencia/ui-design/ Screenshots y HTML de Stitch (referencia UI)
└── ICONOS/                     Iconos source (25 .svg) — duplica src.UI.Shared/Resources/icons
```

### 1.2 Capacidades actuales (confirmadas en README + tests)

**Cálculo (CalculoEngine.cs):**
- Cond 1D / 2D automático por relación Ly/Lx.
- Espesor h_calc con ACI 9.5.2.1 (1D) y ACI 9.5.3.2 (2D).
- αfm completo §9.5.3.3 con Iviga/Ilosa por franja y estado OK/CHK.
- Espesor equivalente vigueta+bloque (T-section).
- Cómputos métricos (cantidad bovedillas, V_concreto, V_total).
- Refuerzo distribuido ASTM A615 (#3..#8) con áreas nominales.

**Editor:**
- Multi-sistema, multi-nivel.
- DataGrid con cálculo en vivo.
- Selector visual de tipos Pieper-Martens (catálogo completo de 26 tipos del binario v5.20).
- Pegar desde Excel (Lx, Ly, espesor, carga, opcionalmente Tipo + Rec).
- DLDoctor: detecta 7 patrones de corrupción del .DL.
- Multi-select + bulk-apply.
- Undo/Redo (snapshots JSON).

**Workflow:**
- Persistencia `.lpx.json` + auto-backup.
- Importación `.DL`/`.TXT` legacy.
- Salida dual `.txt` (texto + tabla editable estilo Excel).
- Exportación CSV / XLSX.
- Atajos reasignables.
- Búsqueda global.

**Memoria:**
- MemoriaGenerator con plantillas `.docx` + placeholders.
- Plurinivel automático.
- MemoriaPlus.App standalone con flujo de 4 pestañas (Datos / Niveles / Cargas / Generar).

**CAD (en construcción):**
- CadCanvasHost de 95 KB sugiere módulo CAD avanzado en desarrollo.
- PLAN_CAD_V1.md (21 KB) + PLAN_V1.1_DIBUJO_AVANZADO.md (25 KB) son los planes detallados ya escritos por el equipo.
- Importadores: DXF (15 KB), PDF como referencia.
- Servicios: AdyacenciaDetector, AnalisisMuros, GrafoAdyacencia, LayoutSolver, MotorCotas, MotorGeometriaAnalitica, SnappingEngine, TopologiaAdyacencia.

**Plugins:**
- Sandbox Roslyn `.csx`.
- Hooks documentados.
- Manifest JSON por plugin.
- Plugin ejemplo + plugin excel-export.

**Tests:**
- 501 tests xUnit.
- Cobertura: cálculo, persistencia, validación, DLDoctor, DXF importer, parsers, registries, memoria.

### 1.3 Qué NO hay (gaps respecto al alcance objetivo)

| Funcionalidad | Estado |
|--------------|--------|
| Módulo de **vigas continuas** | 🚫 ausente |
| Módulo de **columnas** (biaxial + esbeltez) | 🚫 ausente |
| Módulo de **diseño de sección RC** (cortante, torsión, viga-T) | 🚫 ausente |
| Módulo de **zapatas** (rectangulares + poligonales) | 🚫 ausente |
| **Diagramas de interacción P-M** | 🚫 ausente |
| **Visualización 3D** del modelo | 🚫 ausente |
| **Apartado educativo / teórico** por sección | 🚫 ausente |
| Integración con `VigaContinua.exe`, `Columna.exe`, `Zapata.exe`, `AnaZap.exe` | 🚫 ausente (sólo `Losas.exe` integrado) |
| Combinaciones de carga (.DZP/.CEZ) como librería transversal | 🚫 ausente (Combinaciones.DZP solo se referencia desde Zapata) |
| Análisis sísmico modal / estático equivalente | 🚫 ausente |
| Modelo estructural unificado (columnas + vigas + losas + zapatas en un solo árbol) | 🚫 parcial (hay Proyecto → Sistema → Losa, falta vigas/cols/zaps) |

---

## 2. Visión target: de "editor de losas" a "suite estructural"

```
                  ┌────────────────────────────────────────┐
                  │           LosasPlus 2.0 (Suite)        │
                  └────────────────────────────────────────┘
                                     │
   ┌────────────┬───────────┬────────┴─────────┬───────────┬──────────┐
   │            │           │                  │           │          │
┌──┴───┐  ┌─────┴────┐  ┌───┴────┐  ┌──────────┴────┐  ┌───┴───┐  ┌──┴────┐
│Losas │  │Vigas     │  │Columnas│  │Zapatas        │  │ 3D    │  │Educ.  │
│      │  │continuas │  │+ P-M   │  │Rectangulares  │  │Viewer │  │Module │
│(YA)  │  │          │  │+esbelt.│  │+ poligonales  │  │       │  │       │
└──┬───┘  └────┬─────┘  └───┬────┘  └───────┬───────┘  └───┬───┘  └──┬────┘
   │            │           │                │              │         │
   ▼            ▼           ▼                ▼              ▼         ▼
Losas.exe  VigaCont.exe  Columna.exe    Zapata.exe     CAD model   PDFs +
+ motor    + motor       Interaccion    AnaZap.exe     + topology  fórmulas
propio     propio        + motor        + motor        + 3D mesh   + tests
                         propio         propio                     interactivos

   └────────────┴───────────┴────────────────┴──────────────┘
                            │
                            ▼
                ┌───────────────────────────┐
                │  Modelo unificado en      │
                │  src.Core (Proyecto →     │
                │  Edificio → Niveles →     │
                │  {Cols, Vigas, Losas,     │
                │  Zaps, Muros})            │
                └───────────────────────────┘
                            │
                            ▼
                ┌───────────────────────────┐
                │  Generación de memoria    │
                │  .docx unificada por      │
                │  edificio completo        │
                └───────────────────────────┘
```

### 2.1 Principios de diseño

1. **No tirar lo existente.** v0.7 está al 90% de los módulos de losas. Extender, no reescribir.
2. **Motor DISEST opcional pero preferido.** Si `Losas.exe`, `VigaContinua.exe`, `Columna.exe`, `Zapata.exe` están presentes en la carpeta `engine/`, usarlos. Si no, motor propio en `src.Core` cubre la funcionalidad básica.
3. **Modelo unificado.** Un único árbol jerárquico `Proyecto → Edificio → Nivel → Elementos`, no silos por módulo.
4. **Combinaciones de carga centralizadas.** `Combinaciones.DZP` y `.CEZ` se convierten en una librería transversal (`src.Core/Cargas/Combinaciones.cs`), no copy-paste por módulo.
5. **Verificabilidad.** Cada cálculo tiene tests xUnit con valor analítico exacto (no comparar con DISEST, comparar con teoría — porque DISEST puede tener bugs y queremos un motor verificado).
6. **Citabilidad.** Cada fórmula en el código y en la UI educativa cita la fuente: §X.Y.Z de la norma, página del libro, ecuación del paper.

---

## 3. Módulos nuevos por implementar

> Cada módulo se documenta acá con: alcance, dependencias del análisis DISEST que ya hicimos, qué motor usar, qué UI necesita, qué tests, qué citas.

### 3.1 Módulo VIGAS (vigas continuas)

**Fuente del análisis:** `docs-analisis-disest/VIGAS.md` (motor VigaContinua v7.10 Oct 2013, verificado con `fixtures/TEST_MCP_VIGAS.TXT` — Mmax=wL²/8 exacto).

**Modelo de dominio** (extender `src.Core/Models/Sistema.cs` o crear `Viga.cs`):

```csharp
public class Viga
{
    public string Nombre { get; set; }
    public List<TipoApoyo> Apoyos { get; set; }   // articulado / empotrado por apoyo
    public List<double> MomentosAplicados { get; set; }  // ton·m por apoyo
    public bool VoladizoIzq { get; set; }
    public bool VoladizoDer { get; set; }
    public List<Tramo> Tramos { get; set; }  // 1..20
}

public class Tramo
{
    public double Longitud { get; set; }   // m
    public double RigidezRelativa { get; set; } = 1.0;  // I/Ic
    public List<SubTramo> SubTramos { get; set; }
}

public class SubTramo
{
    public double Longitud { get; set; }    // m
    public double WInic { get; set; }       // ton/m
    public double WFin { get; set; }        // ton/m
    public double PFinal { get; set; }      // ton (0 si termina en apoyo)
}
```

**Motor de cálculo propio** (`src.Core/Calculo/VigaContinuaEngine.cs`):

Método: **matriz de rigidez directa** (más simple que tres momentos para N variable de tramos).

```
Para N+1 nodos (apoyos), 2 GDL por nodo (translación vertical + rotación):
  K = ensamblar contribuciones de cada tramo
  F = ensamblar cargas equivalentes nodales (distribuidas lineales + puntuales)
  Aplicar BCs: w=0 en cada apoyo, θ=0 si empotrado
  Resolver K·d = F → desplazamientos y rotaciones
  Por cada tramo: integrar para obtener M(x), V(x) en N puntos
```

**Verificación obligatoria** (test xUnit):
- Viga simple L=5m, w=1 ton/m → Mmax = wL²/8 = 3.125 ton·m exacto (ya confirmado contra VigaContinua.exe).
- Viga continua 2 tramos iguales → M apoyo central = wL²/8, M vano = wL²/14.22.
- Voladizo: M empotramiento = wL²/2.

**Integración opcional con VigaContinua.exe**: como segundo motor pluggable. Trade-off: el binario v7.10 NO acepta argumentos CLI, solo carga interactiva. La integración requiere UI Automation (SendKeys/UIA) o flujo semi-manual ("ejecutar viga en VigaContinua y pegar el .TXT en LosasPlus" — análogo al modelo actual con Losas.exe).

**UI necesaria:**

1. **Pestaña "Vigas"** en MainWindow, paralela a "Losas".
2. **Editor de viga**: diagrama esquemático con tramos + apoyos + voladizos clickables. Panel lateral con la lista de tramos y sub-tramos.
3. **DataGrid de tramos** con: Longitud, I/Ic, # sub-tramos.
4. **DataGrid de sub-tramos** (anidado o panel inferior).
5. **Diagrama de momentos y cortantes** (gráfica): renderizar M(x), V(x) al lado del esquema.
6. **Tabla de resultados**: por sección crítica → Mu, Vu, As req, Av/s req, disposición sugerida.

**Citas obligatorias en el apartado educativo:**
- ACI 318-08 §9.2 (combinaciones de carga). [ACI, 2008]
- ACI 318-08 §9.3 (factores φ).
- ACI 318-08 §10.3 (diseño a flexión).
- ACI 318-08 §11.2 (cortante).
- ACI 318-08 §10.5 (cuantía mínima).
- ASCE 7-05 §2 (combinaciones).
- Para el método matricial: MacGregor & Wight, *Reinforced Concrete: Mechanics and Design*, 4ª ed., 2005, Cap. 6. [MacGregor, 2005]

### 3.2 Módulo COLUMNAS (biaxial + esbeltez + diagrama P-M)

**Fuente del análisis:** `docs-analisis-disest/COLUMNAS.md` (motor Columna.exe v2.00 Feb 2011, F-COL1 a F-COL6 verificadas).

**Modelo de dominio** (`src.Core/Models/Columna.cs`):

```csharp
public class Columna
{
    public string Id { get; set; }
    public SeccionColumna Seccion { get; set; }
    public MaterialesRC Materiales { get; set; }
    public List<CasoCargaColumna> Cargas { get; set; }  // por caso (D, L, E, W…)
    public PandeoConfig Pandeo { get; set; }            // null si no aplica
}

public class SeccionColumna
{
    public TipoSeccion Tipo { get; set; }       // RECTANGULAR / CIRCULAR
    public double Cx { get; set; }              // cm
    public double Cy { get; set; }              // cm
    public double Bp { get; set; }              // recubrimiento al centro de As (cm)
    public double Hp { get; set; }
    public double D { get; set; }               // circular
    public TipoRefuerzo RefuerzoTransversal { get; set; }  // ESTRIBOS / ZUNCHO
    public int BarrasX { get; set; } = 2;
    public int BarrasY { get; set; } = 2;
}

public class PandeoConfig
{
    public double LuX, LuY;     // m, longitudes no arriostradas
    public double KrX = 1.0, KrY = 1.0;
    public bool Indesplazable { get; set; } = true;  // sólo sistemas indesplazables (ACI 318-08 §10.10.6)
}
```

**Motor de cálculo propio** (`src.Core/Calculo/ColumnaEngine.cs`):

```
diseñar(seccion, materiales, solic):
    # Si pandeo activo:
    if solic.con_pandeo:
        para cada direccion (X, Y):
            r = h / sqrt(12)                       # F-COL6
            esbeltez = Kr * Lu / r
            esbeltez_crit = 34 - 12*(M1/M2)        # ≤40 (ACI §10.10.1)
            si esbeltez < crit: δns = 1.0
            sino:
                Pc = pi^2 * EI / (Kr*Lu)^2         # ACI §10.10.6
                Cm = 0.6 + 0.4*(M1/M2)
                δns = Cm / (1 - Pu / (0.75*Pc))    # ACI Ec. 10-9
            Mc = δns * M2
        Mux = Mc_X; Muy = Mc_Y

    # Diseño biaxial: iterar (c, β) para minimizar As
    para cada (c, β) candidato:
        Nn, Mnx, Mny = integrar_seccion(c, β, As_actual)
        verificar φNn ≥ Nu, φMnx ≥ Mux, φMny ≥ Muy
    devolver As_total, c_optimo, β_optimo
```

**Verificación obligatoria:**
- φ estribos = **0.65 exacto** (confirmado con Diseno.TMP).
- ρ_geom = As/Ag = 1.085% (confirmado con TEST_MCP_COLUMNA_Diseno.TMP).
- Esbeltez con r=h/√12: para Cy=60 cm, Lu=3m → 17.32 exacto.
- Compresión pura: Pn = 0.85·f'c·Ag.

**Submódulo P-M (diagrama de interacción)**: motor propio basado en iteración de profundidad del eje neutro `c` desde 0 a infinito. Para cada c, calcular Nn y Mn integrando el bloque equivalente de Whitney + el aporte de cada barra. Generar curva con ~30 puntos.

**UI necesaria:**

1. **Pestaña "Columnas"** en MainWindow.
2. **Editor de columna**: sección visual con barras, checkbox Pandeo expansible.
3. **Panel de solicitaciones por caso**: tabla 11 casos × (Nu, Mux, Muy) sincronizada con Combinaciones global.
4. **Visualización del diagrama P-M**: gráfica 2D N-Mx + N-My + curva 3D Nu-Mux-Muy con punto del caso resaltado (rojo si fuera de envolvente, verde si dentro).
5. **Visualización de la sección con eje neutro inclinado**: dibuja el polígono comprimido (zona < c) y las barras con su estado (compresión / tracción).
6. **Reporte de armaduras**: As total, disposición sugerida con barras comerciales (#3 a #11), cuantía ρ, comparación con ρ_min y ρ_max.

**Citas educativas:**
- ACI 318-08 §10.2 (hipótesis a flexión y compresión).
- ACI 318-08 §10.3.5 (sección controlada por compresión / tensión).
- ACI 318-08 §10.10 (efectos de esbeltez).
- Pieper-Martens, *Bemessung von durchlaufenden Platten* (1973) — para coherencia con módulo Losas.
- Park & Paulay, *Reinforced Concrete Structures*, Wiley 1975, Cap. 5. [Park-Paulay, 1975]
- Nilson, Darwin & Dolan, *Design of Concrete Structures*, 13ª ed., 2004, Cap. 8 (columnas). [Nilson, 2004]

### 3.3 Módulo DISENO_VIGAS (sección RC genérica, flexión + cortante + torsión)

**Fuente del análisis:** `docs-analisis-disest/DISENO_VIGAS.md` (motor Diseno.exe v4.24 Oct 2013, fórmulas F-DIS1 a F-DIS5 inferidas).

**Modelo de dominio** (`src.Core/Models/SeccionDiseno.cs`):

```csharp
public class SeccionDiseno
{
    public TipoSeccionDiseno Tipo { get; set; }   // RECTANGULAR / VIGA_T
    public double B, H;                            // cm, ancho del alma y peralte total
    public double Bf, Hf;                          // cm, ancho y espesor del ala (T)
    public double Dp;                              // cm, recubrimiento al centro de As
    public double RelacionAsCompresion;            // A's/As
}

public class SolicitacionesSeccion
{
    public double Mu, Nu, Vu, Tu;   // ton·m, ton, ton, ton·m
    public bool Primaria = true;
    public bool Sismo = false;
}
```

**Motor propio** (`src.Core/Calculo/DisenoSeccionEngine.cs`):

Implementar las 5 fórmulas F-DIS1 a F-DIS5 documentadas en DISENO_VIGAS.md. Caso clave: flexión a viga-T, donde el eje neutro puede caer dentro del ala (caso rectangular ancho) o en el alma (T efectivo).

**Integración con módulo VIGAS**: cuando el motor VigaContinua produce M(x), V(x), por cada sección crítica llamar a `DisenoSeccionEngine.diseñar(...)` para obtener As, A's, Av/s.

**UI necesaria:**
- Sub-vista dentro del módulo Vigas (panel inferior "Diseño RC por sección crítica").
- Editor independiente "Diseñar sección" para casos puntuales (sin pertenecer a una viga continua).

**Citas educativas:**
- ACI 318-08 §10.3 (flexión).
- ACI 318-08 §11.2 (cortante).
- ACI 318-08 §11.5 (torsión).
- ACI 318-08 §8.10 (viga-T, ancho efectivo).
- MacGregor & Wight 2005, Cap. 5 (flexión), Cap. 6 (cortante).

### 3.4 Módulo ZAPATAS

**Fuente del análisis:** `docs-analisis-disest/ZAPATAS.md` (motores Zapata.exe v6.10 Abr 2013 + AnaZap.exe v2.07 Ene 2013).

**Modelo de dominio** (`src.Core/Models/Zapata.cs`):

```csharp
public class Zapata
{
    public TipoGeometriaZapata Tipo { get; set; }  // RECTANGULAR / POLIGONAL
    public ColumnaSobre Columna { get; set; }
    public ZapataRectangular Rect { get; set; }
    public ZapataPoligonal Pol { get; set; }
    public CoeficientesSismicos Sismo { get; set; }
    public Suelo Suelo { get; set; }
    public List<CasoCargaZapata> Cargas { get; set; }  // 11 casos × (N, Mx, My, opcionalmente Xn, Yn)
}

public class Suelo
{
    public double SigmaAdm = 1.5;       // kg/cm²
    public double Kcd = 1.333;          // factor corta duración
    public double PesoRelleno = 1.6;    // ton/m³
    public double PesoHormigon = 2.4;
}

public class CoeficientesSismicos
{
    public double Rho = 1.0;
    public double Sds = 1.0;            // g
    public double Ev = 0.30;            // fracción de Sds; DGRS=0.30, ASCE=0.20
}
```

**Motor propio** (`src.Core/Calculo/ZapataEngine.cs`):

Implementar:
- F-ZAP1: presión bajo zapata rígida sin despegue (Navier).
- F-ZAP2: iteración de eje neutro para despegue parcial.
- F-ZAP3: búsqueda de dimensiones Lx, Ly, H que cumplan todos los criterios (100/50/25 % en compresión).
- F-ZAP4: punzonamiento ACI 318-08 §11.11.
- F-ZAP5: cortante en una dirección.
- F-ZAP6: momento de diseño en cara de columna.

Para zapatas poligonales (módulo AnaZap), implementar:
- Cálculo de propiedades geométricas de polígonos (área, centroide, inercias Ix, Iy, Ixy) con teorema del Polígono (Stokes / Green discreto).
- Iteración de eje neutro para polígono.

**UI necesaria:**
- Pestaña "Zapatas" con diagrama 3D de la zapata + columna + diagrama de presiones (heatmap de colores rojos → amarillos → verdes).
- Editor de polígono para AnaZap (clicks en pantalla para definir vértices, con snap a coordenadas).
- Panel de combinaciones que se enlaza con la librería global.

**Citas educativas:**
- ACI 318-08 §15 (zapatas).
- ACI 318-08 §11.11 (punzonamiento).
- ASCE 7-05 §2.3 (combinaciones últimas), §2.4 (servicio), §12.4 (E con Ev).
- R-026 (Cargas mínimas, MOPC RD).
- R-027 (Sismo, MOPC RD).
- Bowles, *Foundation Analysis and Design*, 5ª ed., 1996, Cap. 8. [Bowles, 1996]

### 3.5 Módulo COMBINACIONES (librería transversal)

**Fuente:** `docs-analisis-disest/ZAPATAS.md` + `Combinaciones.DZP` + `Combinaciones.CEZ`.

**Decisión arquitectónica**: extraer la noción de "combinaciones de carga" del módulo Zapatas a una librería compartida en `src.Core/Cargas/`.

```csharp
public class CombinacionesProyecto
{
    public List<CasoCarga> Casos { get; set; }              // D, L, Lr, Ex, Ey, Wx, Wy, Tx, Ty…
    public List<CombinacionServicio> Servicio { get; set; }  // para verificación de esfuerzos
    public List<CombinacionUltima> Ultimas { get; set; }     // para diseño RC
    public NormaCombinaciones Norma { get; set; }            // ASCE_7_05 / ASCE_7_22 / R026 / R027 / Custom
}
```

Permitir al usuario:
- Importar desde `.DZP` / `.CEZ` (parser ya parcialmente posible — son texto plano).
- Editar interactivamente (DataGrid con factores).
- Aplicar la misma definición de combinaciones a todos los módulos (losas, vigas, columnas, zapatas).

**UI**: pestaña "Cargas y Combinaciones" en el nivel proyecto, no por módulo.

### 3.6 Módulo INTERACCION (diagrama P-M)

**Fuente del análisis:** `docs-analisis-disest/COLUMNAS.md` sección INTERACCION (motor Interaccion.exe v3.31 Ago 2013).

**Implementación**: parte del módulo COLUMNAS (3.2) pero con UI propia.

- Para una sección y armadura dadas, generar la curva ØP-ØM (~30 puntos).
- Soportar diagrama 2D N-Mx, N-My, y curva 3D N-Mx-My.
- Marcar puntos de los casos de carga sobre la envolvente.
- Vista de la sección con el eje neutro animado al pasar el cursor sobre el diagrama (educativo).

Si el binario `Interaccion.exe` está disponible, opción de delegar a él (pero requiere UI Automation por la complejidad de su flujo).

---

## 4. Módulo 3D — visualización del modelo

### 4.1 Alcance

Visualizar el modelo estructural completo del edificio:
- Cimentación (zapatas) → columnas → vigas → losas → muros.
- Vista navegable (rotar, paneo, zoom).
- Coloreo por tipo de elemento o por nivel de carga.
- Indicación visual de elementos con violación normativa.
- Vista de cortes (planta, alzado, sección horizontal/vertical).

### 4.2 Tecnología propuesta

Trade-offs (presentar alternativas como pidió el operador):

| Opción | Pro | Contra |
|--------|-----|--------|
| **A) HelixToolkit.Wpf (WPF 3D)** | Stack puro WPF, no agrega dependencias pesadas; tutoriales abundantes; integra bien con MVVM existente | Limitado en performance para modelos grandes (>1000 elementos); shaders básicos |
| **B) WPF + DirectX vía SharpDX** | Performance máximo en Windows | Mucha complejidad; tira de la arquitectura .NET 8 (SharpDX activo); riesgo de mantenibilidad |
| **C) Embebido de Three.js en WebView2** | Visualización moderna, igual a CAD web; reutilizable en posible exportador HTML | Necesita comunicación JS ↔ WPF; tamaño adicional ~150 MB de runtime; latencia |
| **D) Embebido de Veldrid (.NET nativo)** | Cross-platform si algún día se sale de WPF; performance bueno | Inmaduro respecto a HelixToolkit; menos ejemplos |

**Recomendación inicial: A (HelixToolkit)**. Permite ship rápido un v1; si las limitaciones se notan, migrar a B o D después manteniendo la abstracción.

### 4.3 Geometría a renderizar

Por cada elemento del modelo:

```
Columna: cilindro o prisma rectangular extruido desde (cota_nivel_n) hasta (cota_nivel_n+1)
Viga: prisma rectangular B×H extruido a lo largo de la línea entre dos columnas
Losa: prisma plano H × área_poligonal en la cota del nivel
Zapata: prisma sólido bajo cada columna en cota cimentación
Muro: prisma rectangular extruido entre dos cotas, en planta como línea
```

Coloreo:
- **Por nivel de utilización** (heatmap): gris si OK, naranja si 80-100% de capacidad, rojo si > 100%.
- **Por elemento crítico** (modo "qué falla primero"): semáforo verde / amarillo / rojo.
- **Por tipo** (modo "vista normal"): hormigón color cemento, acero color rojizo si está expuesto.

### 4.4 Interacciones

- **Click en elemento**: abre el editor de ese elemento en la pestaña correspondiente (Losas / Vigas / etc.).
- **Hover**: tooltip con datos esenciales (B×H, As, Mu, Vu).
- **Cortes**: arrastrar un plano para revelar sección.
- **Sincronía con árbol del proyecto**: seleccionar un nivel en el árbol resalta su contorno en 3D.

### 4.5 Arquitectura

```
src.Core/Visualizacion3D/
├── ModeloGeometria3D.cs            # ensambla la geometría desde el modelo lógico
├── ProyectoToScenePipeline.cs      # convierte Proyecto → Scene HelixToolkit
└── ColoreoStrategies.cs            # estrategias de coloreo (utilización, tipo, custom)

src/Views/Visualizacion3D/
├── View3D.xaml                     # contenedor HelixToolkit.Wpf
├── View3D.xaml.cs
└── Camera3DController.cs           # cámaras predefinidas (planta, alzado, isométrico)
```

---

## 5. Apartado educativo por módulo

### 5.1 Filosofía

El usuario opera el programa pero también **aprende mientras lo usa**. Cada cálculo crítico tiene una vista "Ver derivación" que muestra:

1. La **fórmula** en LaTeX / MathML.
2. La **derivación paso a paso** (sustitución de valores del caso actual).
3. La **cita normativa exacta** (§ del reglamento, página del libro).
4. Un **diagrama explicativo** (sección con bloque de compresión, viga con cargas, etc.).
5. **Tests interactivos** (mini-quiz opcional para reforzar).

Esto diferencia el producto de un "Excel con UI bonita" o de "código IA-generado sin sustento". Es **trazable a fuentes reales**.

### 5.2 Estructura del módulo educativo

```
src.Core/Educacion/
├── Modelo/
│   ├── Articulo.cs           # un capítulo educativo
│   ├── Formula.cs            # con LaTeX, variables, unidades
│   ├── DerivacionPaso.cs     # paso de cálculo con expresión y resultado
│   └── Cita.cs               # norma:sección o libro:capitulo:página
├── Contenido/
│   ├── Losas/                # un .md por concepto
│   │   ├── PieperMartens.md
│   │   ├── MetodoCoeficientes_ACI.md
│   │   ├── Balanceo_Apoyos.md
│   │   ├── Camellado.md
│   │   └── Voladizos.md
│   ├── Vigas/
│   │   ├── MetodoRigidez.md
│   │   ├── DisenoFlexion_ACI.md
│   │   ├── DisenoCortante_ACI.md
│   │   └── Torsion_ACI.md
│   ├── Columnas/
│   │   ├── FlexionCompuesta.md
│   │   ├── DiagramaInteraccion.md
│   │   ├── EsbeltezACI_10_10.md
│   │   └── FactorPhi.md
│   └── Zapatas/
│       ├── PresionSuelo_Navier.md
│       ├── EjeNeutro_Despegue.md
│       ├── Punzonamiento.md
│       └── CombinacionesASCE.md

src/Views/Educacion/
├── EducacionPanel.xaml       # vista lateral o modal
└── Mathjax_WebView.xaml      # renderiza LaTeX vía MathJax embebido
```

### 5.3 Ejemplo concreto: artículo "Método de Pieper-Martens para losas continuas"

```markdown
# Método de Pieper-Martens para losas continuas en dos direcciones

## Origen del método

El método fue desarrollado por **Klaus Pieper** y **Peter Martens** de la
Technische Universität Braunschweig (Alemania) y publicado originalmente como:

> Pieper, K. y Martens, P. *Bemessung von durchlaufenden Platten ohne Stützen­raster*
> [Diseño de losas continuas sin marco de columnas]. Beton- und Stahlbetonbau, 1973.

(Cita: este es el paper original. Para una traducción/adaptación al español, ver
F. Perdomo, *Losas continuas apoyadas en tres y cuatro bordes*, 2011 — documento
incluido en la suite DISEST original).

## Hipótesis fundamentales

1. **Semi-empotramiento en bordes continuos**: el momento de tramo se calcula
   como el promedio entre el momento de tramo para apoyo simple y para
   empotramiento perfecto en los bordes continuos.
2. **Rigidez a torsión sin armadura de torsión en esquinas**: la losa puede
   tomar el momento torsor pero no se diseña refuerzo específico.
3. **Carga viva máxima 2/3 de la carga total**: límite de aplicabilidad.

## Fórmulas

Para una losa rectangular de luces $L_x$, $L_y$ con relación $L_y / L_x$ en
$[0.5, 2.0]$:

$$M_{fx} = \frac{q \cdot L_x^2}{F_x}$$

$$M_{fy} = \frac{q \cdot L_y^2}{F_y}$$

donde $F_x, F_y$ son coeficientes adimensionales tomados de las **Tablas 1 a 12**
del paper original, según las condiciones de borde.

Para los momentos de empotramiento perfecto en los apoyos:

$$M_{S0x} = \frac{q \cdot L_x^2}{S_x} \qquad M_{S0y} = \frac{q \cdot L_y^2}{S_y}$$

## Procedimiento de balanceo

Cuando dos losas son adyacentes y comparten un borde, los momentos de
empotramiento $M_{S01}$ y $M_{S02}$ pueden diferir. El momento de diseño $M_S$
sobre el apoyo se calcula:

- Si $L_{max} \le 5 \cdot L_{min}$:
  $$M_S = \frac{M_{S01} + M_{S02}}{2} \ge 0.75 \cdot \max|M_{S0}|$$
- Si $L_{max} > 5 \cdot L_{min}$:
  $$M_S = \max|M_{S0}|$$

(Nota: el `.hlp` del programa Losas.exe usa el factor 5; el PDF teórico
del autor usa el factor 3 en §3.3. Existe una discrepancia documental
que no afecta esta implementación si se sigue el `.hlp` por ser el
documento operativo del binario distribuido.)

## Limitaciones declaradas

- Aplicable sólo a losas sobre apoyos lineales rígidos (vigas con peralte
  $> 4 \cdot h_{losa}$ o cumplimiento de §13.6.1.6 ACI 318-08).
- **No verifica fuerza cortante** — debe hacerse por separado.
- **No calcula deformaciones** — debe hacerse por separado.
- Carga viva $\le \frac{2}{3}$ de carga total sin factorizar.

## Comparación con métodos alternativos

| Método | Origen | Ventaja | Desventaja |
|--------|--------|---------|------------|
| Pieper-Martens | TU Braunschweig, 1973 | Sencillo, válido para 3 y 4 bordes | No incluye corte ni deformaciones |
| Coeficientes ACI 318-63 (Método 2) | ACI 318 antiguo Apéndice | Aprobado por ACI | Sólo 4 bordes, factores distintos |
| Método del pórtico equivalente | ACI 318 §13.7 | General, incluye losas planas | Requiere análisis matricial |
| Diferencias finitas / EF | Numérico general | Cualquier geometría y BC | Requiere software pesado |

## Referencias

- Pieper, K. & Martens, P., *Bemessung von durchlaufenden Platten*,
  Beton- und Stahlbetonbau, 1973.
- Perdomo, F., *Losas continuas apoyadas en tres y cuatro bordes*, DISEST,
  2011 (manual del módulo Losas v5.20).
- ACI Committee 318, *Building Code Requirements for Structural Concrete
  (ACI 318-08) and Commentary*, American Concrete Institute, 2008.
- Nilson, A. H., Darwin, D. y Dolan, C. W., *Design of Concrete Structures*,
  13ª ed., McGraw-Hill, 2004, Cap. 12.
```

(Este es el contenido modelo para UN artículo. Debe replicarse el patrón para los ~25 artículos del módulo completo.)

### 5.4 Vista "Ver derivación" en la UI

Al lado de cada resultado calculado (ej. "Mfx = 0.959 ton·m"), un botón pequeño 🛈 que abre un popover:

```
┌─ Cálculo de Mfx (losa #1) ──────────────────────────────────────┐
│                                                                  │
│  Fórmula (Pieper-Martens 1973, §2.1):                            │
│                                                                  │
│      Mfx = q · Lx² / Fx                                          │
│                                                                  │
│  Sustituyendo:                                                   │
│      q  = 2.000 ton/m² (CARGA, .DL)                              │
│      Lx = 4.000 m       (LX, .DL)                                │
│      Fx = 33.95          (TABLA 6, aspecto Ly/Lx = 0.875,        │
│                          tipo 40, interpolado entre 0.85 y 0.90) │
│                                                                  │
│      Mfx = 2 · 4² / 33.37 = 0.959 ton·m                          │
│                                                                  │
│  Citas:                                                          │
│   📄 Pieper-Martens 1973, ec. 2.1                                │
│   📄 Perdomo 2011, Losas.hlp                                     │
│   📄 ACI 318-08 §13.6.1.6 (verificación de apoyos rígidos)       │
│                                                                  │
│  [Abrir artículo completo →]                                     │
└──────────────────────────────────────────────────────────────────┘
```

---

## 6. Cambios necesarios en `src.Core` (no romper lo existente)

### 6.1 Modelo unificado

Reorganizar `Models/`:

```
src.Core/Models/
├── Proyecto.cs                    (ya existe, extender)
├── Edificio.cs                    ★ NUEVO — nivel sobre Proyecto
├── Nivel.cs                       ★ NUEVO — agrupa elementos por planta
├── Elementos/                     ★ NUEVO
│   ├── ElementoEstructural.cs     # base abstracta
│   ├── Columna.cs                 ★ NUEVO
│   ├── Viga.cs                    ★ NUEVO
│   ├── Losa.cs                    (extraer de Sistema.cs)
│   ├── Zapata.cs                  ★ NUEVO
│   └── Muro.cs                    (ya existe en Models/Cad)
├── Sistema.cs                     (mantener, simplificar)
└── Cad/                           (mantener)

src.Core/Cargas/
├── CombinacionesProyecto.cs       ★ NUEVO
├── CasoCarga.cs                   ★ NUEVO
└── ParserDzpCez.cs                ★ NUEVO

src.Core/Calculo/
├── CalculoEngine.cs               (ya existe, mantener; renombrar a LosasEngine?)
├── VigaContinuaEngine.cs          ★ NUEVO
├── ColumnaEngine.cs               ★ NUEVO
├── DisenoSeccionEngine.cs         ★ NUEVO
├── ZapataEngine.cs                ★ NUEVO
└── Comun/                         ★ NUEVO
    ├── PropiedadesACI.cs          # ρ, ω, φ, β1, bloque de Whitney
    ├── InterpolacionLineal.cs
    └── PoligonoGeometria.cs       # área, centroide, inercia de polígono
```

### 6.2 Compatibilidad hacia atrás

- El formato `.lpx.json` actual sigue cargando.
- `ProyectoSerializer` extendido con migración: si `.lpx.json` v1 → v2, agrega `Edificio` envolvente con un solo `Nivel` con los `Sistemas` actuales.
- Tests v0.7 deben seguir pasando (501 tests verdes).

### 6.3 Nuevas dependencias

| Paquete NuGet | Para | Tamaño |
|---------------|------|-------:|
| `HelixToolkit.Wpf` | Visualización 3D | ~5 MB |
| `MathNet.Numerics` | Resolver sistemas K·d=F en VigaContinua | ~3 MB |
| `OxyPlot.Wpf` | Gráficas 2D (M(x), V(x), P-M) | ~2 MB |
| `MahApps.Metro.IconPacks.Material` | Iconos (opcional, ya tienen SVG propios) | ~1 MB |

No requiere paquetes con licencias restrictivas. Todo MIT/Apache.

---

## 7. Roadmap por fases

### Fase 1 — Refactor del modelo (2-3 semanas)

- ★ Extraer Edificio + Nivel sobre Proyecto.
- ★ Migrar `.lpx.json` v1 → v2 sin romper proyectos existentes.
- ★ Tests de migración.
- ★ Tests existentes verdes.

### Fase 2 — Combinaciones transversales (1-2 semanas)

- ★ Parser de `.DZP` y `.CEZ`.
- ★ Modelo `CombinacionesProyecto`.
- ★ UI pestaña "Cargas y Combinaciones" en nivel proyecto.
- ★ Tests con fixtures `Combinaciones.DZP` y `Combinaciones.CEZ` reales.

### Fase 3 — Módulo VIGAS (4-6 semanas)

- ★ `VigaContinuaEngine` (motor matricial propio).
- ★ Tests con casos analíticos exactos (Mmax=wL²/8, wL²/12, voladizo wL²/2, dos tramos iguales).
- ★ UI pestaña Vigas: editor + DataGrid de tramos + sub-tramos + diagramas M(x), V(x).
- ★ Integración opcional con VigaContinua.exe (semi-manual, análogo al flujo actual con Losas.exe).
- ★ Acoplamiento con `DisenoSeccionEngine` (sub-fase 3.5).

### Fase 4 — Módulo DISENO_SECCION (2-3 semanas)

- ★ `DisenoSeccionEngine` con F-DIS1 a F-DIS5.
- ★ Soporte sección T (caso eje neutro en ala vs alma).
- ★ Tests xUnit con casos analíticos.
- ★ UI sub-vista "Diseño RC por sección" dentro de Vigas + editor standalone.

### Fase 5 — Módulo COLUMNAS + INTERACCION (4-6 semanas)

- ★ `ColumnaEngine` con F-COL1 a F-COL6.
- ★ Motor de iteración (c, β) para flexión biaxial.
- ★ Submódulo P-M con 30 puntos de envolvente.
- ★ Análisis de esbeltez §10.10 indesplazable.
- ★ UI pestaña Columnas: editor + diagrama P-M 2D/3D + visualización sección con eje neutro.

### Fase 6 — Módulo ZAPATAS (4-5 semanas)

- ★ `ZapataEngine` rectangular con F-ZAP1 a F-ZAP6.
- ★ Iteración de eje neutro para despegue parcial.
- ★ Búsqueda de dimensiones óptimas Lx, Ly, H.
- ★ Sub-módulo poligonal (AnaZap): propiedades geométricas + presiones bajo polígono.
- ★ UI pestaña Zapatas: editor rectangular + editor poligonal + heatmap presiones.

### Fase 7 — Módulo 3D (3-4 semanas)

- ★ HelixToolkit.Wpf integrado.
- ★ Pipeline Proyecto → Scene 3D.
- ★ Coloreo por utilización.
- ★ Interacciones (click → editor, hover → tooltip, cortes).

### Fase 8 — Módulo EDUCATIVO (continuo)

- ★ Infraestructura: `Articulo`, `Formula`, `Cita`, `DerivacionPaso`.
- ★ Renderer LaTeX vía MathJax embebido en WebView2 (o alternativa Markdig + MathJax-server).
- ★ 25-30 artículos iniciales (uno por concepto crítico).
- ★ Popover "Ver derivación" en cada resultado clave.
- ★ Bibliografía mínima compilada en un solo lugar (`docs/bibliografia.md`).

### Fase 9 — Acoplamiento + memoria unificada (2-3 semanas)

- ★ `MemoriaGenerator` extendido para todo el edificio (no solo losas).
- ★ Generación de memoria multi-elemento con todos los módulos.
- ★ Tests end-to-end.

**Total estimado**: 26-35 semanas en una persona dedicada full-time. ~6-9 meses calendario realistas.

---

## 8. UI nuevas necesarias (listado)

### 8.1 A nivel de MainWindow

Pestañas top-level adicionales:

- **Vigas** (Fase 3).
- **Columnas** (Fase 5).
- **Zapatas** (Fase 6).
- **3D** (Fase 7).
- **Cargas y Combinaciones** (Fase 2, antes de los módulos de diseño).
- **Educación / Teoría** (Fase 8, accesible desde menú principal).

### 8.2 Por módulo

| Módulo | Vistas nuevas |
|--------|---------------|
| Vigas | EditorViga, DataGridTramos, DataGridSubTramos, DiagramaMV, DisenoSeccionPanel |
| Columnas | EditorColumna, DiagramaPM2D, DiagramaPM3D, SeccionConEjeNeutro, PandeoPanel |
| Zapatas | EditorZapataRect, EditorZapataPoligonal, HeatmapPresiones, DiagramaPresiones3D |
| Combinaciones | EditorCasosCarga, EditorCombinaciones, ImportadorDzp |
| 3D | View3D, Camera3DController, ColoreoStrategiesSelector, CortePlanoControl |
| Educación | EducacionPanel, ArticuloViewer (MathJax), DerivacionPopover, ListadoBibliografia |

### 8.3 Componentes reutilizables

- `SeccionViewer` (vista 2D de sección con barras y eje neutro) — usar en Columnas y Diseno.
- `DiagramaCurvaViewer` (OxyPlot wrapper) — usar en M(x), V(x), P-M.
- `HeatmapViewer` — usar en presiones de zapata y posiblemente en stress de losa.

---

## 9. Riesgos y trade-offs

| Riesgo | Mitigación |
|--------|------------|
| Alcance demasiado grande para una sola persona | Roadmap fragmentado en fases independientes; cada fase es entregable y testeable por sí sola |
| Motor propio diverge de DISEST | Tests xUnit contra casos analíticos exactos (no contra DISEST), más fixtures de DISEST para regresión |
| Visualización 3D rinde mal con > 500 elementos | Mitigación tecnológica con LOD (nivel de detalle); culling de elementos fuera de vista; opción de exportar a IFC para visualizar en Tekla/SAFE |
| Módulo educativo crece sin control | Definir 25-30 artículos prioritarios y congelar; aceptar contribuciones via PR |
| Integración con binarios DISEST se complica (UIA frágil) | Hacer la integración opcional; motor propio cubre el 100% sin DISEST |
| Plurinivel + 3D + Educativo sobrecargan la UI | Carga lazy: pestañas se inicializan al activarse, no al boot |
| Combinaciones DGRS vs ASCE introducen inconsistencias | Modelo `NormaCombinaciones` explícito, el usuario elige; tests con casos reales `.DZP` |

---

## 10. Bibliografía y citas obligatorias

Lista mínima que TODOS los módulos deben citar. Todas verificables.

### Normas (oficiales)

- **ACI 318-08**: *Building Code Requirements for Structural Concrete and Commentary*. American Concrete Institute, 2008.
- **ACI 318-19**: edición vigente. ACI, 2019.
- **ASCE 7-05**: *Minimum Design Loads for Buildings and Other Structures*. American Society of Civil Engineers, 2005.
- **ASCE 7-22**: edición vigente. ASCE, 2022.
- **R-001**: *Recomendaciones provisionales para el análisis sísmico de estructuras*. MOPC RD, 1979.
- **R-024**: *Recomendaciones para la Construcción Antisísmica*. MOPC RD.
- **R-026**: *Reglamento de Cargas Mínimas para el Diseño de Edificaciones*. MOPC RD.
- **R-027**: *Reglamento para el Análisis y Diseño Sísmico de Estructuras*. MOPC RD.
- **DGRS**: convención dominicana citada por DISEST con Ev=0.30·Sds (vs ASCE 0.20).

### Libros de texto (referencia técnica)

- **MacGregor, J. & Wight, J.**, *Reinforced Concrete: Mechanics and Design*, Pearson, 4ª ed. 2005 o posteriores.
- **Nilson, A., Darwin, D. & Dolan, C.**, *Design of Concrete Structures*, McGraw-Hill, 13ª ed. 2004 o posteriores.
- **Park, R. & Paulay, T.**, *Reinforced Concrete Structures*, Wiley, 1975.
- **Bowles, J. E.**, *Foundation Analysis and Design*, McGraw-Hill, 5ª ed. 1996.
- **Hibbeler, R. C.**, *Structural Analysis*, Pearson, ediciones recientes — para método matricial de vigas.

### Papers originales

- **Pieper, K. & Martens, P.**, *Bemessung von durchlaufenden Platten ohne Stützen­raster*. Beton- und Stahlbetonbau, 1973. (Origen del método de losas usado por DISEST.)

### Documentos internos DISEST (atribución)

- **Perdomo, F.**, *Losas continuas apoyadas en tres y cuatro bordes*. Manual del módulo Losas v5.20, 2011 (incluido en la suite DISEST).
- **Perdomo, F.**, archivos `.hlp` de cada módulo DISEST (Losas.hlp, VigaContinua.hlp, AnaZap.hlp, Zapata.hlp).

---

## 11. Atribución y aspectos legales

LosasPlus mantiene la atribución a F. Perdomo en la sección "Acerca de" y en el README. El motor `Losas.exe` y demás binarios DISEST NO se redistribuyen con LosasPlus (`.gitignore` ya lo excluye explícitamente). La librería de fórmulas reimplementadas en `src.Core/Calculo/` se basa en normas públicas (ACI, ASCE) y bibliografía técnica citada — no es un port del binario, es una implementación independiente verificada contra los mismos casos analíticos.

Si el operador obtiene autorización del Ing. Perdomo para redistribuir su binario, el `.gitignore` se puede ajustar para incluir `engine/Losas.exe` (y demás) en releases — pero por defecto se distribuye sin ellos.

---

## 12. Próximos pasos para el agente de implementación (Claude Code u otro)

1. **Leer**: este documento + `docs-analisis-disest/{LOSAS,VIGAS,COLUMNAS,DISENO_VIGAS,ZAPATAS}.md` (en la carpeta hermana al repo).
2. **Verificar el estado actual** ejecutando `dotnet test` en `LosasPlus/`. Confirmar 501 tests verdes.
3. **Empezar por Fase 1** (refactor del modelo a Edificio + Nivel). No tocar el flujo de losas existente; sólo envolverlo.
4. **Cada fase**:
   - Branch dedicado (ej. `feat/fase2-combinaciones`).
   - Tests xUnit que cubran los casos analíticos del .md correspondiente.
   - PR con descripción que cite las secciones de este documento.
5. **No avanzar a la siguiente fase hasta**:
   - Todos los tests verdes.
   - El módulo es accesible desde la UI sin romper la pestaña Losas existente.
   - La memoria .docx incluye un placeholder para el nuevo módulo (aunque sea vacío al principio).
6. **Reportar al operador** cada hito mayor (final de fase) con: screenshot, demo en video corto, commit hash.

---

**Fin del documento.** Versión 1.0 lista para revisión del operador antes de pasarse a Claude Code.
