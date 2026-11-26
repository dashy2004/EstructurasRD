"""Corre el mismo puente FEM/Pieper-Martens que consume el export de Revit."""

import argparse
import hashlib
import json
import subprocess
import sys
import tempfile
from pathlib import Path

from openpyxl import load_workbook


def entrada_relativa(nombre: str, carpeta: Path) -> Path:
    ruta = (Path.cwd() / nombre).resolve()
    if not ruta.is_relative_to(carpeta.resolve()) or ruta.suffix.lower() != ".json":
        raise ValueError("Elija un archivo JSON dentro de entradas-losas/.")
    if not ruta.is_file():
        raise ValueError(f"Archivo ausente: {nombre}")
    return ruta


def revision(repo: Path) -> str:
    return subprocess.check_output(
        ["git", "-C", str(repo), "rev-parse", "HEAD"], text=True
    ).strip()


def revision_para_chatgpt(datos: dict, lectura: dict) -> str:
    """Transcribe valores de la corrida sin convertir unidades ni recalcular el diseño."""
    def celda(valor: object) -> str:
        return str(valor).replace("\\", "\\\\").replace("|", "\\|").replace("\n", " ").replace("\r", " ")

    lineas = [
        "# Revisión de losas exportadas",
        "",
        lectura["alcance"] + ".",
        "",
        "En cada fila, el espesor se lee de panos[].espesor.h_usado_m. "
        "Mfx y Mfy son valores del bloque pieper_martens.losas por marca. "
        "La última columna conserva las unidades declaradas por ese bloque.",
        "",
        "| Marca | Espesor usado (m) | Mfx | Mfy | Unidad PM |",
        "| --- | ---: | ---: | ---: | --- |",
    ]
    for pano in datos["panos"]:
        marca = pano["mark"]
        pm = datos["pieper_martens"]["losas"][marca]
        fila = [marca, pano["espesor"]["h_usado_m"], pm["mfx"], pm["mfy"], pm["unidades"]]
        lineas.append("| " + " | ".join(map(celda, fila)) + " |")
    lineas.extend([
        "",
        "Para revisar en ChatGPT, adjunte este archivo, resultados.json y lectura.json. "
        "Para preguntas sobre barras y separaciones adjunte también acero_pm.json. "
        "Solicite que cada observación cite su marca, método, campo y unidad de origen "
        "y que identifique los datos faltantes. El informe Excel contiene los supuestos.",
        "",
        "El cálculo FEM, la franja analítica de los vuelos y los momentos Pieper-Martens "
        "provienen del puente de losas. El archivo lectura.json identifica los commits "
        "del motor y del complemento y las huellas de los dos JSON de entrada. "
        "La revisión profesional de geometría, cargas, bordes y combinaciones sigue pendiente.",
        "",
    ])
    return "\n".join(lineas)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--puente", type=Path, required=True)
    parser.add_argument("--salida", type=Path, required=True)
    parser.add_argument("--proyecto", default="")
    parser.add_argument("--usos", default="")
    parser.add_argument("--detectar-entradas", action="store_true",
                        help="Usar el par convencional en entradas-losas/ cuando exista.")
    args = parser.parse_args()
    if args.detectar_entradas and not (args.proyecto or args.usos):
        par = ("entradas-losas/proyecto.json", "entradas-losas/usos.json")
        if any(Path(ruta).exists() for ruta in par):
            args.proyecto, args.usos = par
            if not all(Path(ruta).is_file() for ruta in par):
                parser.error("El cálculo por push requiere entradas-losas/proyecto.json y entradas-losas/usos.json.")
    if bool(args.proyecto) != bool(args.usos):
        parser.error("Indique juntos proyecto y usos o deje los dos vacíos.")

    if args.proyecto:
        proyecto = entrada_relativa(args.proyecto, Path("entradas-losas"))
        usos = entrada_relativa(args.usos, Path("entradas-losas"))
        titulo = "Predimensionado de losas exportadas"
    else:
        ejemplo = args.puente / "puente-losas" / "ejemplo"
        proyecto = ejemplo / "edificio-ejemplo.json"
        usos = ejemplo / "usos-ejemplo.json"
        titulo = "Ejemplo de prueba - losas"

    huellas = {}
    for clave, ruta in (("proyecto", proyecto), ("usos", usos)):
        with ruta.open("rb") as archivo:
            huellas[clave] = hashlib.file_digest(archivo, "sha256").hexdigest()
    if args.salida.exists():
        raise FileExistsError(f"La carpeta de salida ya existe: {args.salida}")
    args.salida.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=args.salida.parent) as tmp:
        trabajo = Path(tmp)
        subprocess.run(
            [sys.executable, "-m", "puente_losas.cli", "--proyecto", str(proyecto),
             "--usos", str(usos), "--salida", str(trabajo), "--nombre", titulo],
            check=True,
        )
        crudo = trabajo / "resultados.json"
        datos = json.loads(crudo.read_text(encoding="utf-8"))
        panos = {p["mark"] for p in datos["panos"]}
        if len(panos) != len(datos["panos"]):
            raise ValueError("Hay marcas de losa repetidas en el proyecto.")
        if not panos or panos != set(datos["pieper_martens"]["losas"]):
            raise ValueError("Las marcas del FEM y de Pieper-Martens no coinciden.")
        subprocess.run(
            [sys.executable, "-m", "plugin_losas.cli", "acero",
             "--resultados", str(crudo), "--salida", str(trabajo)], check=True,
        )
        acero = trabajo / "acero_pm.json"
        if set(json.loads(acero.read_text(encoding="utf-8"))["losas"]) != panos:
            raise ValueError("Las marcas del complemento y del puente no coinciden.")
        libro = trabajo / "informe_losas.xlsx"
        wb = load_workbook(libro, read_only=True)
        try:
            hojas = wb.sheetnames
            if not hojas:
                raise ValueError("El libro generado no contiene hojas.")
        finally:
            wb.close()
        lectura = {
            "alcance": titulo,
            "origen": "archivos_seleccionados" if args.proyecto else "ejemplo_sintetico",
            "panos": len(panos),
            "hojas_excel": hojas,
            "entradas_sha256": huellas,
            "revision_motor": revision(Path(__file__).resolve().parents[1]),
            "revision_puente": revision(args.puente),
            "metodos": ["FEM de placa de EstructurasRD", "Pieper-Martens de puente-losas"],
        }
        recibo = trabajo / "lectura.json"
        recibo.write_text(json.dumps(lectura, indent=2, ensure_ascii=False), encoding="utf-8")
        guia = trabajo / "Revision para ChatGPT.md"
        guia.write_text(revision_para_chatgpt(datos, lectura), encoding="utf-8")
        publicacion = trabajo / "publicacion"
        publicacion.mkdir()
        for archivo in (libro, crudo, acero, recibo, guia):
            archivo.rename(publicacion / archivo.name)
        publicacion.rename(args.salida)
        print(f"{len(panos)} marcas cotejadas; libro verificado con {len(hojas)} hojas.")


if __name__ == "__main__":
    main()
