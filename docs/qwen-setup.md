# IA local (Qwen) para EstructurasRD — setup

Objetivo: usar una IA **local** (Qwen vía Ollama) para leer **PDF / DXF / imágenes**
con detalles estructurales y **proponer elementos** (losas, columnas, ejes) que el
ingeniero confirma. **La IA es SOLO LECTURA del código** — nunca modifica el código
fuente de la app.

## 1. Instalar

```bash
./scripts/install-qwen.sh
# o con otro modelo:
QWEN_MODEL=qwen3-vl:8b ./scripts/install-qwen.sh
```

El script instala **Ollama** (requiere `sudo` + descarga de varios GB), levanta el
servicio en `http://127.0.0.1:11434` y descarga el modelo.

> **Sobre "qwen3.6":** no es un tag publicado. Para leer PDF/imágenes hace falta un
> Qwen **con visión**: por defecto `qwen2.5vl:7b` (estable en Ollama). Si tu Ollama
> ya trae `qwen3-vl`, usalo con `QWEN_MODEL=qwen3-vl:8b`. Para texto solo: `qwen3`.

## 2. Configurar

`qwen.config.json` (raíz del repo):

```json
{ "endpoint": "http://127.0.0.1:11434", "modelo": "qwen2.5vl:7b",
  "soloLectura": true, "permitirModificarCodigo": false }
```

Ajustá `modelo`/`endpoint` si cambiaste algo en el paso 1.

## 3. Integración en la app (read-only)

El contrato vive en `src.Core/IA/AnalizadorEstructuralIA.cs`:

- `IAnalizadorEstructuralIA.AnalizarAsync(archivoPath)` → `PropuestaElementos`
  (`ColumnaPropuesta`, `LosaPropuesta`, `EjePropuesto` — solo geometría en metros).
- La implementación (futura `QwenAnalizador`) llamará al endpoint local de Ollama
  (`POST /api/chat` con la imagen/PDF) y parseará la respuesta a esos records.

**Guardrail (obligatorio):** la IA **solo devuelve datos**; el ingeniero revisa y
aplica. La interfaz no expone nada que escriba código ni el proyecto.
`QwenConfig.PermitirModificarCodigo` es `false` y no debe cambiarse.

## 4. Flujo previsto (cuando se implemente)

1. Usuario abre un plano (PDF/DXF/imagen) en EstructurasRD.
2. "Analizar con IA" → `QwenAnalizador.AnalizarAsync(archivo)` → `PropuestaElementos`.
3. La app muestra los elementos propuestos **para revisión** (no los crea solos).
4. El ingeniero confirma → se crean losas/columnas/ejes en el **nivel activo**.
5. El cálculo (Pieper-Martens) y el diseño los hace el **motor**, no la IA.

## 5. Estado

- ✅ Script de instalación, config, contrato read-only y docs.
- ⏳ Pendiente: implementar `QwenAnalizador` (llamada a Ollama + parseo) y el
  comando UI de revisión. Requiere el modelo ya instalado (paso 1) y haber
  validado primero el modelo por niveles (ver `docs/plan-anclaje-niveles.md`).
