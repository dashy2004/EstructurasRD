// shell.js — UI de carga de un modelo propio. POST /visor → onModelo(bundle, modeloJson).
// No conoce three.js: solo DOM + fetch. El render lo hace el callback onModelo.
export function crearShell({ onModelo }) {
  const cont = document.getElementById('shell');

  const file = document.createElement('input');
  file.type = 'file';
  file.accept = '.json,application/json';
  file.setAttribute('aria-label', 'cargar modelo .json');

  const textarea = document.createElement('textarea');
  textarea.placeholder = 'pega aquí el JSON del modelo';
  textarea.rows = 4;
  textarea.style.width = '16em';

  const btnAnalizar = document.createElement('button');
  btnAnalizar.type = 'button';
  btnAnalizar.textContent = 'analizar';

  const btnDescargar = document.createElement('button');
  btnDescargar.type = 'button';
  btnDescargar.textContent = 'descargar .json';
  btnDescargar.disabled = true;

  const estado = document.createElement('span');
  estado.id = 'shell-estado';

  cont.append(file, textarea, btnAnalizar, btnDescargar, estado);

  let ultimoModelo = null;   // el último JSON cargado con éxito (para descargar)

  async function analizar(texto) {
    let modeloJson;
    try {
      modeloJson = JSON.parse(texto);
    } catch (e) {
      estado.textContent = 'JSON inválido: ' + e.message;
      return;   // no postea, no toca la escena
    }
    let bundle;
    try {
      const r = await fetch('./visor', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(modeloJson),
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
    ultimoModelo = modeloJson;
    btnDescargar.disabled = false;
    estado.textContent = 'modelo cargado';
    onModelo(bundle, modeloJson);
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
    a.download = 'modelo.json';
    a.click();
    URL.revokeObjectURL(url);
  });
}
