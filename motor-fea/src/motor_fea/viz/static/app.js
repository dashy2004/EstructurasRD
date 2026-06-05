import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { VRButton } from 'three/addons/webxr/VRButton.js';

const msg = document.getElementById('msg');
const setMsg = (t) => { msg.textContent = t; };

// --- Escena base ---
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x101418);
scene.add(new THREE.GridHelper(50, 50, 0x444444, 0x222222));
scene.add(new THREE.AxesHelper(2));
scene.add(new THREE.HemisphereLight(0xffffff, 0x303030, 1.3));

const camera = new THREE.PerspectiveCamera(60, innerWidth / innerHeight, 0.05, 1000);

const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(innerWidth, innerHeight);
renderer.setPixelRatio(devicePixelRatio);
renderer.xr.enabled = true;
document.body.appendChild(renderer.domElement);

// Rig: movemos este grupo para teletransportarnos en VR.
const rig = new THREE.Group();
rig.add(camera);
scene.add(rig);

const controls = new OrbitControls(camera, renderer.domElement);

const MAT = {
  columna: new THREE.MeshStandardMaterial({ color: 0x4a90d9 }),
  viga:    new THREE.MeshStandardMaterial({ color: 0xd98a4a }),
};

function addBarra(b, nodos) {
  const pi = nodos[b.i], pj = nodos[b.j];
  if (!pi || !pj) return;
  const vi = new THREE.Vector3(pi[0], pi[1], pi[2]);
  const vj = new THREE.Vector3(pj[0], pj[1], pj[2]);
  const L = vi.distanceTo(vj);
  if (L === 0) return;
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(b.b, b.h, L), MAT[b.tipo] || MAT.viga);
  mesh.position.copy(vi).lerp(vj, 0.5);
  mesh.lookAt(vj);   // orienta el lado +Z (largo L) hacia el nodo j
  scene.add(mesh);
}

async function cargar() {
  let data;
  try {
    const r = await fetch('./escena');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    data = await r.json();
  } catch (e) {
    setMsg('Error cargando /escena: ' + e.message);
    return;
  }
  const nodos = {};
  for (const n of data.nodos) nodos[n.id] = n.p;
  for (const b of data.barras) addBarra(b, nodos);

  // Auto-encuadre con bbox.
  const mn = new THREE.Vector3(data.bbox.min[0], data.bbox.min[1], data.bbox.min[2]);
  const mx = new THREE.Vector3(data.bbox.max[0], data.bbox.max[1], data.bbox.max[2]);
  const centro = mn.clone().add(mx).multiplyScalar(0.5);
  const radio = Math.max(mn.distanceTo(mx) / 2, 1);
  controls.target.copy(centro);
  camera.position.copy(centro).add(new THREE.Vector3(radio * 1.6, radio * 1.2, radio * 1.6));
  controls.update();
  setMsg(`${data.barras.length} barras · ${data.nodos.length} nodos`);
}

// --- WebXR: botón solo si hay soporte ---
if (navigator.xr && navigator.xr.isSessionSupported) {
  navigator.xr.isSessionSupported('immersive-vr').then((ok) => {
    if (ok) document.body.appendChild(VRButton.createButton(renderer));
  });
}

// --- Teletransporte en VR ---
const piso = new THREE.Mesh(
  new THREE.PlaneGeometry(500, 500).rotateX(-Math.PI / 2),
  new THREE.MeshBasicMaterial({ visible: false }));
scene.add(piso);

const marca = new THREE.Mesh(
  new THREE.CircleGeometry(0.25, 32).rotateX(-Math.PI / 2),
  new THREE.MeshBasicMaterial({ color: 0x00ff88 }));
marca.visible = false;
scene.add(marca);

const raycaster = new THREE.Raycaster();
const rotMatrix = new THREE.Matrix4();
let destino = null;

function crearControl(i) {
  const c = renderer.xr.getController(i);
  c.addEventListener('selectstart', () => { c.userData.activo = true; });
  c.addEventListener('selectend', () => {
    c.userData.activo = false;
    if (destino) {
      const cabeza = new THREE.Vector3().setFromMatrixPosition(camera.matrixWorld);
      rig.position.x += destino.x - cabeza.x;
      rig.position.z += destino.z - cabeza.z;
    }
  });
  c.add(new THREE.Line(
    new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(0, 0, 0), new THREE.Vector3(0, 0, -5)]),
    new THREE.LineBasicMaterial({ color: 0x00ff88 })));
  rig.add(c);
  return c;
}
const xrControls = [crearControl(0), crearControl(1)];

function actualizarTeletransporte() {
  destino = null;
  marca.visible = false;
  for (const c of xrControls) {
    if (!c.userData.activo) continue;
    rotMatrix.identity().extractRotation(c.matrixWorld);
    raycaster.ray.origin.setFromMatrixPosition(c.matrixWorld);
    raycaster.ray.direction.set(0, 0, -1).applyMatrix4(rotMatrix);
    const hits = raycaster.intersectObject(piso);
    if (hits.length) {
      destino = hits[0].point;
      marca.position.copy(destino);
      marca.visible = true;
    }
  }
}

addEventListener('resize', () => {
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(innerWidth, innerHeight);
});

renderer.setAnimationLoop(() => {
  if (renderer.xr.isPresenting) actualizarTeletransporte();
  else controls.update();
  renderer.render(scene, camera);
});

cargar();
