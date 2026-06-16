// shell.js — UI de carga de un modelo propio. Según el selector de origen, POST a
// ./visor (modelo FEA de bajo nivel) o ./visor-edificio (Proyecto autorado:
// columnas/muros/losas+cargas). Ambos devuelven el mismo bundle {escena,
// resultados, esfuerzos} → onModelo(bundle, json). No conoce three.js: solo DOM + fetch.
export function crearShell({ onModelo }) {
  const cont = document.getElementById('shell');

  const origen = document.createElement('select');
  origen.setAttribute('aria-label', 'origen del JSON');
  origen.append(
    new Option('modelo FEA', './visor'),
    new Option('edificio autorado', './visor-edificio'),
  );

  const file = document.createElement('input');
  file.type = 'file';
  file.accept = '.json,application/json';
  file.setAttribute('aria-label', 'cargar JSON');

  const textarea = document.createElement('textarea');
  textarea.placeholder = 'pega aquí el JSON (modelo FEA o edificio, según el selector)';
  textarea.rows = 4;
  textarea.style.width = '16em';

  const btnAnalizar = document.createElement('button');
  btnAnalizar.type = 'button';
  btnAnalizar.textContent = 'cargar';

  const btnDescargar = document.createElement('button');
  btnDescargar.type = 'button';
  btnDescargar.textContent = 'descargar .json';
  btnDescargar.disabled = true;

  const estado = document.createElement('span');
  estado.id = 'shell-estado';

  cont.append(origen, file, textarea, btnAnalizar, btnDescargar, estado);

  let ultimoModelo = null;   // el último JSON cargado con éxito (para descargar)

  async function analizar(texto) {
    let json;
    try {
      json = JSON.parse(texto);
    } catch (e) {
      estado.textContent = 'JSON inválido: ' + e.message;
      return;   // no postea, no toca la escena
    }
    const ruta = origen.value;
    let bundle;
    try {
      const r = await fetch(ruta, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(json),
      });
      if (!r.ok) {
        const d = await r.json().catch(() => ({}));
        throw new Error(d.detail || ('HTTP ' + r.status));
      }
      bundle = await r.json();
    } catch (e) {
      estado.textContent = 'Error: ' + e.message;
      return;   // conserva la escena actual
    }
    ultimoModelo = json;
    btnDescargar.disabled = false;
    estado.textContent = (ruta === './visor-edificio' ? 'edificio' : 'modelo') + ' cargado';
    onModelo(bundle, json);
  }

  file.addEventListener('change', () => {
    const f = file.files && file.files[0];
    if (!f) return;
    const lector = new FileReader();
    lector.onload = () => analizar(String(lector.result));
    lector.onerror = () => { estado.textContent = 'No se pudo leer el archivo'; };
    lector.readAsText(f);
  });

  btnAnalizar.addEventListener('click', () => analizar(textarea.value));

  btnDescargar.addEventListener('click', () => {
    if (!ultimoModelo) return;
    const blob = new Blob([JSON.stringify(ultimoModelo, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = (origen.value === './visor-edificio' ? 'edificio' : 'modelo') + '.json';
    a.click();
    URL.revokeObjectURL(url);
  });
}
