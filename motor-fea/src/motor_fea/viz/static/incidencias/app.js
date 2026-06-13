// app.js — Visor VR de incidencias. three.js sin build (import-map).
import * as THREE from 'three';
import { VRButton } from 'three/addons/webxr/VRButton.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

const msg = (t) => { document.getElementById('msg').textContent = t || ''; };

// --- Ancla de georreferencia de la maqueta (origen del solar). Editable. ---
const georref = { lat0: 18.4861, lon0: -69.9312, rumbo_deg: 0.0, escala: 1.0 };

// --- Estado de datos (marcadores) — lógica pura sobre un array ---
let seq = 1;
const incidencias = [];               // {id, category, severity, description, recursos[], mesh, pos}

function crearIncidencia(pos) {
  const inc = { id: 'm' + (seq++), category: '', severity: 'media',
                description: '', recursos: [], pos: { x: pos.x, y: pos.y, z: pos.z } };
  incidencias.push(inc);
  return inc;
}
function borrarIncidencia(inc) {
  const i = incidencias.indexOf(inc);
  if (i >= 0) { scene.remove(inc.mesh); incidencias.splice(i, 1); }
}
function serializar() {
  return {
    version: 1, georref,
    incidencias: incidencias.map((c) => ({
      id: c.id, category: c.category, subcategory: null, severity: c.severity,
      description: c.description, status: 'pending', images: [],
      vr: { pos: c.pos, recursos: c.recursos },
    })),
  };
}

// --- Escena ---
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x101418);
scene.add(new THREE.GridHelper(40, 40), new THREE.AxesHelper(2));
scene.add(new THREE.HemisphereLight(0xffffff, 0x444444, 1.2));
const camera = new THREE.PerspectiveCamera(70, innerWidth / innerHeight, 0.1, 1000);
camera.position.set(8, 6, 12);
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(innerWidth, innerHeight);
renderer.xr.enabled = true;
document.body.appendChild(renderer.domElement);
addEventListener('resize', () => {
  camera.aspect = innerWidth / innerHeight; camera.updateProjectionMatrix();
  renderer.setSize(innerWidth, innerHeight);
});

// VR si hay soporte; si no, órbita (degradación elegante, como el visor FEA).
if (navigator.xr) {
  navigator.xr.isSessionSupported('immersive-vr').then((ok) => {
    if (ok) document.body.appendChild(VRButton.createButton(renderer));
  });
}
const controls = new OrbitControls(camera, renderer.domElement);

// Cargar la maqueta glTF.
new GLTFLoader().load('./maqueta_ejemplo.gltf',
  (g) => scene.add(g.scene),
  undefined,
  () => msg('No se pudo cargar la maqueta glTF.'));

// --- Marcadores: raycast por clic (desktop) ---
const raycaster = new THREE.Raycaster();
const markerGeo = new THREE.SphereGeometry(0.25, 16, 16);
const markerMat = new THREE.MeshStandardMaterial({ color: 0xff3344 });

function colocarMarcador(inc) {
  inc.mesh = new THREE.Mesh(markerGeo, markerMat);
  inc.mesh.position.set(inc.pos.x, inc.pos.y, inc.pos.z);
  inc.mesh.userData.inc = inc;
  scene.add(inc.mesh);
}

let activa = null;
renderer.domElement.addEventListener('pointerdown', (ev) => {
  const ndc = new THREE.Vector2((ev.clientX / innerWidth) * 2 - 1,
                                -(ev.clientY / innerHeight) * 2 + 1);
  raycaster.setFromCamera(ndc, camera);
  const hits = raycaster.intersectObjects(scene.children, true);
  if (!hits.length) return;
  const marcadorPrevio = hits.find((h) => h.object.userData.inc);
  if (marcadorPrevio) { abrirFicha(marcadorPrevio.object.userData.inc); return; }
  const inc = crearIncidencia(hits[0].point);
  colocarMarcador(inc);
  abrirFicha(inc);
});

// --- Ficha (panel HTML) ---
const ficha = document.getElementById('ficha');
const $ = (id) => document.getElementById(id);
function abrirFicha(inc) {
  activa = inc;
  $('f-categoria').value = inc.category;
  $('f-severidad').value = inc.severity;
  $('f-descripcion').value = inc.description;
  $('f-recursos').value = inc.recursos.join(', ');
  ficha.style.display = 'block';
}
$('btn-guardar-ficha').onclick = () => {
  if (!activa) return;
  activa.category = $('f-categoria').value;
  activa.severity = $('f-severidad').value;
  activa.description = $('f-descripcion').value;
  activa.recursos = $('f-recursos').value.split(',').map((s) => s.trim()).filter(Boolean);
  ficha.style.display = 'none';
};
$('btn-borrar').onclick = () => { if (activa) { borrarIncidencia(activa); ficha.style.display = 'none'; } };
$('btn-clasificar').onclick = async () => {
  msg('Clasificando…');
  try {
    const r = await fetch('/api/incidencias/clasificar', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ descripcion: $('f-descripcion').value }),
    });
    const a = await r.json();
    $('f-categoria').value = a.categoria || $('f-categoria').value;
    $('f-severidad').value = a.severidad || $('f-severidad').value;
    if (a.accion_sugerida) $('f-recursos').value = a.accion_sugerida;
    msg(a.sospechoso ? 'IA: revisar manualmente.' : '');
  } catch { msg('IA no disponible; llená la ficha a mano.'); }
};

// --- Import / Export ---
$('btn-exportar').onclick = async () => {
  await fetch('/api/incidencias', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(serializar()),
  }).then((r) => msg(r.ok ? 'Exportado al servidor.' : 'Error al exportar.'));
  const blob = new Blob([JSON.stringify(serializar(), null, 2)], { type: 'application/json' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob); a.download = 'incidencias.json'; a.click();
};
$('btn-importar').onclick = () => $('file-importar').click();
$('file-importar').onchange = async (ev) => {
  const doc = JSON.parse(await ev.target.files[0].text());
  incidencias.splice(0).forEach((c) => scene.remove(c.mesh));
  for (const it of doc.incidencias || []) {
    const pos = (it.vr && it.vr.pos) || { x: 0, y: 0, z: 0 };
    const inc = { id: it.id || ('m' + seq++), category: it.category || '',
                  severity: it.severity || 'media', description: it.description || '',
                  recursos: (it.vr && it.vr.recursos) || [], pos };
    incidencias.push(inc); colocarMarcador(inc);
  }
  msg(`Importadas ${incidencias.length} incidencias.`);
};

renderer.setAnimationLoop(() => { controls.update(); renderer.render(scene, camera); });
