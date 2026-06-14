// Helpers SVG compartidos: creación de nodos y descarga (SVG/PNG).
// Sin estado, sin three.js. Usados por diagramas2d.js y seccion2d.js.
const SVGNS = 'http://www.w3.org/2000/svg';

export function nodo(tag, attrs) {
  const n = document.createElementNS(SVGNS, tag);
  for (const k of Object.keys(attrs)) n.setAttribute(k, attrs[k]);
  return n;
}

function descargarBlob(blob, nombre) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = nombre;
  a.click();
  URL.revokeObjectURL(url);
}

export function descargarSVG(svg, nombre) {
  if (!svg) return;
  const texto = new XMLSerializer().serializeToString(svg);
  descargarBlob(new Blob([texto], { type: 'image/svg+xml' }), nombre);
}

// Rasteriza el SVG a PNG en un canvas nativo (sin librerías). Fondo opaco
// (el panel es oscuro) para que el PNG no salga transparente.
export function descargarPNG(svg, nombre, escala = 2) {
  if (!svg) return;
  const w = parseInt(svg.getAttribute('width'), 10) || 300;
  const h = parseInt(svg.getAttribute('height'), 10) || 300;
  const texto = new XMLSerializer().serializeToString(svg);
  const url = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(texto);
  const img = new Image();
  img.onload = () => {
    const canvas = document.createElement('canvas');
    canvas.width = w * escala;
    canvas.height = h * escala;
    const ctx = canvas.getContext('2d');
    ctx.fillStyle = '#101418';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
    canvas.toBlob((blob) => { if (blob) descargarBlob(blob, nombre); });
  };
  img.src = url;
}
