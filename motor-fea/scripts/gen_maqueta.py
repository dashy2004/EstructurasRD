"""Genera una maqueta glTF 2.0 de ejemplo (solar + estructura) sin dependencias.
Salida: src/motor_fea/viz/static/incidencias/maqueta_ejemplo.gltf (buffer embebido).

La maqueta es un solar plano (losa fina 20×20 m) con una caja-estructura (4×3×4 m)
encima, suficiente para recorrer en VR y anclar marcadores. Sustituible por el
export Revit→glTF real (misma app, otra URL)."""
import base64
import json
import struct
from pathlib import Path

SALIDA = (Path(__file__).resolve().parent.parent
          / "src/motor_fea/viz/static/incidencias/maqueta_ejemplo.gltf")


def _caja(cx, cy, cz, sx, sy, sz):
    hx, hy, hz = sx / 2, sy / 2, sz / 2
    verts = [
        (cx - hx, cy - hy, cz - hz), (cx + hx, cy - hy, cz - hz),
        (cx + hx, cy + hy, cz - hz), (cx - hx, cy + hy, cz - hz),
        (cx - hx, cy - hy, cz + hz), (cx + hx, cy - hy, cz + hz),
        (cx + hx, cy + hy, cz + hz), (cx - hx, cy + hy, cz + hz),
    ]
    caras = [0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1,
             1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0]
    return verts, caras


def main():
    verts, idx = [], []
    for (cx, cy, cz, sx, sy, sz) in [
        (0.0, -0.1, 0.0, 20.0, 0.2, 20.0),   # solar (losa fina)
        (0.0, 1.5, 0.0, 4.0, 3.0, 4.0),      # estructura
    ]:
        base = len(verts)
        v, f = _caja(cx, cy, cz, sx, sy, sz)
        verts += v
        idx += [base + i for i in f]

    pos_bytes = b"".join(struct.pack("<3f", *v) for v in verts)
    idx_bytes = b"".join(struct.pack("<H", i) for i in idx)
    pad = (4 - len(pos_bytes) % 4) % 4              # alinear los índices a 4 bytes
    buf = pos_bytes + b"\x00" * pad + idx_bytes
    uri = "data:application/octet-stream;base64," + base64.b64encode(buf).decode()

    xs = [v[0] for v in verts]; ys = [v[1] for v in verts]; zs = [v[2] for v in verts]
    gltf = {
        "asset": {"version": "2.0", "generator": "motor-fea gen_maqueta"},
        "scenes": [{"nodes": [0]}], "scene": 0,
        "nodes": [{"mesh": 0}],
        "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1}]}],
        "buffers": [{"byteLength": len(buf), "uri": uri}],
        "bufferViews": [
            {"buffer": 0, "byteOffset": 0, "byteLength": len(pos_bytes), "target": 34962},
            {"buffer": 0, "byteOffset": len(pos_bytes) + pad,
             "byteLength": len(idx_bytes), "target": 34963},
        ],
        "accessors": [
            {"bufferView": 0, "componentType": 5126, "count": len(verts), "type": "VEC3",
             "min": [min(xs), min(ys), min(zs)], "max": [max(xs), max(ys), max(zs)]},
            {"bufferView": 1, "componentType": 5123, "count": len(idx), "type": "SCALAR"},
        ],
    }
    SALIDA.parent.mkdir(parents=True, exist_ok=True)
    SALIDA.write_text(json.dumps(gltf, indent=2), encoding="utf-8")
    print(f"escrito {SALIDA} ({len(buf)} bytes de buffer)")


if __name__ == "__main__":
    main()
