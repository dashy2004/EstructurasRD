# LosasPlus — Edición de Escritorio (Windows)

Aplicación de escritorio **Avalonia / .NET 8** para autoría y análisis estructural:
editor 2D de planta (losas, vigas, columnas), descenso de cargas, memoria de cálculo
y visor. Esta entrega es **autónoma (self-contained)**: incluye todo lo necesario para
ejecutarse, **no requiere instalar .NET** ni ninguna otra dependencia en la PC.

---

## ✨ Novedades de esta versión

- **Editor de losas — selección múltiple y borrado en lote.** En el editor 2D de
  planta ahora puede seleccionar **varias losas a la vez** y eliminarlas todas de
  una sola vez:
  - **Ctrl + Click** sobre cada losa para agregarla o quitarla de la selección.
  - **Arrastrar una caja** (clic en una zona vacía y arrastrar) para seleccionar
    todas las losas que la caja toque.
  - Con las losas seleccionadas, presione el botón **🗑 Eliminar** o la tecla
    **Supr (Delete)** del teclado para borrarlas todas.
  - La acción es **deshacible** con **Ctrl + Z**.

> El resto de elementos (vigas y columnas) también puede eliminarse con el mismo
> botón o la tecla Supr.

---

## 💻 Requisitos

- **Windows 10 o 11, 64 bits** (x64).
- ~250 MB de espacio en disco.
- No requiere conexión a Internet ni privilegios de administrador para ejecutarse.

---

## 📦 Instalación en Windows

La aplicación se distribuye como un archivo **ZIP portátil** (no necesita instalador).

1. **Descargue** el archivo `LosasPlus-win-x64.zip` desde la sección de
   *Releases* (o desde el medio que se le haya entregado: USB, correo, etc.).

2. **Descomprima** el ZIP en una carpeta de su elección, por ejemplo:

   ```
   C:\EstructurasRD\LosasPlus\
   ```

   > Clic derecho sobre el ZIP → **Extraer todo…** → elija la carpeta destino.

3. **Si Windows bloqueó el archivo** (por venir de Internet): clic derecho sobre el
   ZIP **antes** de extraer → **Propiedades** → marque **Desbloquear** → **Aceptar**.
   Luego extraiga. Esto evita avisos de SmartScreen sobre cada archivo.

4. Entre a la carpeta extraída y **ejecute** `LosasPlus.exe` (doble clic).

5. La **primera vez**, Windows puede mostrar la pantalla azul de
   **"Windows protegió su PC" (SmartScreen)** porque la aplicación no está firmada
   con un certificado comercial. Para continuar:
   - Haga clic en **Más información**.
   - Luego en **Ejecutar de todas formas**.

   Esto solo ocurre la primera vez.

### (Opcional) Crear acceso directo en el Escritorio

- Clic derecho sobre `LosasPlus.exe` → **Mostrar más opciones** → **Enviar a** →
  **Escritorio (crear acceso directo)**.

---

## ▶️ Cómo usar el editor de losas

1. Abra `LosasPlus.exe`.
2. Vaya al **Editor 2D de Planta**.
3. Seleccione el **Nivel** en el desplegable superior.
4. Use la herramienta **↖ Puntero** para seleccionar:
   - **Click** = seleccionar una losa.
   - **Ctrl + Click** = agregar/quitar losas a la selección.
   - **Arrastrar una caja** sobre varias losas = seleccionarlas todas.
5. Presione **🗑 Eliminar** o **Supr** para borrar las losas seleccionadas.
6. **Ctrl + Z** deshace la última eliminación.

---

## 🛠️ Solución de problemas

| Problema | Solución |
|---|---|
| "Windows protegió su PC" | Clic en **Más información → Ejecutar de todas formas**. |
| El antivirus marca el `.exe` | Es un falso positivo común en apps .NET sin firmar. Agregue la carpeta como excepción o desbloquee el ZIP (paso 3). |
| No abre / se cierra al instante | Verifique que descomprimió **toda** la carpeta del ZIP (no ejecute el `.exe` desde dentro del ZIP). |
| Falta algún archivo nativo | Vuelva a extraer el ZIP completo en una carpeta sin tildes ni caracteres especiales en la ruta. |

---

## 🔁 Actualizar a una versión nueva

1. Cierre la aplicación.
2. Borre la carpeta anterior (o renómbrela como respaldo).
3. Extraiga el nuevo ZIP en su lugar.

Sus proyectos y datos guardados **no** viven dentro de esta carpeta, así que no se
pierden al reemplazarla.

---

*LosasPlus — Port a Avalonia/.NET 8 del motor de cálculo de losas (F. Perdomo, Losas v5.00).*
