"""Entrada compatible para ejecutar el cálculo de CDCRD-Digital desde EstructurasRD."""
import argparse
import os
import subprocess
import sys
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--cdcrd", default=os.environ.get("CDCRD_DIGITAL_DIR"))
    args, restantes = parser.parse_known_args()
    puente = argparse.ArgumentParser(add_help=False)
    puente.add_argument("--puente")
    ubicacion, _ = puente.parse_known_args(restantes)
    # El puente y el orquestador pueden residir en revisiones distintas de CDCRD.
    for base in (args.cdcrd, ubicacion.puente, Path.cwd() / "cdcrd",
                 Path.cwd() / "herramientas-cdcrd", Path.cwd().parent / "CDCRD-Digital"):
        if base:
            guion = Path(base) / "herramientas" / "calculo_losas.py"
            if guion.is_file():
                return subprocess.call([sys.executable, str(guion), *restantes])
    parser.error("Indique con --cdcrd o CDCRD_DIGITAL_DIR la copia de CDCRD-Digital con herramientas/calculo_losas.py.")


if __name__ == "__main__":
    raise SystemExit(main())
