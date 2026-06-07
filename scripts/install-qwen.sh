#!/usr/bin/env bash
# ============================================================================
# install-qwen.sh — Instala una IA local (Qwen, vía Ollama) para EstructurasRD.
#
# Uso:
#   ./scripts/install-qwen.sh                 # modelo de visión por defecto
#   QWEN_MODEL=qwen3-vl:8b ./scripts/install-qwen.sh
#
# La IA es SOLO LECTURA respecto al código fuente: se usa para analizar
# PDF/DXF/imágenes y proponer elementos estructurales (losas, columnas, ejes).
# NUNCA modifica el código de la aplicación. Ver docs/qwen-setup.md.
# ============================================================================
set -euo pipefail

# Modelo por defecto: Qwen con visión (necesario para PDF/imágenes con detalles
# estructurales). Cambialo con la variable QWEN_MODEL. Para texto solo: qwen3.
QWEN_MODEL="${QWEN_MODEL:-qwen2.5vl:7b}"

echo "==> Modelo objetivo: $QWEN_MODEL"

# 1. Instalar Ollama si falta (https://ollama.com).
if ! command -v ollama >/dev/null 2>&1; then
  echo "==> Ollama no encontrado. Instalando (requiere sudo)…"
  curl -fsSL https://ollama.com/install.sh | sh
else
  echo "==> Ollama ya instalado: $(ollama --version 2>/dev/null | head -1)"
fi

# 2. Asegurar que el servicio esté corriendo (endpoint local 127.0.0.1:11434).
if ! curl -fsS http://127.0.0.1:11434/api/version >/dev/null 2>&1; then
  echo "==> Levantando 'ollama serve' en background…"
  nohup ollama serve >/tmp/ollama.log 2>&1 &
  sleep 3
fi

# 3. Descargar el modelo (varios GB la primera vez).
echo "==> Descargando '$QWEN_MODEL' (puede tardar)…"
ollama pull "$QWEN_MODEL"

# 4. Prueba rápida.
echo "==> Verificando…"
curl -fsS http://127.0.0.1:11434/api/tags | grep -q "$(echo "$QWEN_MODEL" | cut -d: -f1)" \
  && echo "OK: '$QWEN_MODEL' disponible en http://127.0.0.1:11434" \
  || { echo "ERROR: el modelo no aparece en /api/tags"; exit 1; }

echo
echo "Listo. Ajustá qwen.config.json si cambiaste el modelo/endpoint."
echo "Recordá: la IA es SOLO LECTURA del código — solo propone elementos."
