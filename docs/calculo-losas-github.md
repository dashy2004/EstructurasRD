# Calcular las losas de Revit en GitHub

CDCRD-Digital mantiene el flujo y el generador de revisión. El flujo de EstructurasRD delega en la versión fijada de CDCRD-Digital y conserva las entradas del repositorio que inicia la ejecución. [Guía principal](https://github.com/dashy2004/CDCRD-Digital/tree/codex/revision-ia/docs/revision-ia)

La acción **Calcular losas exportadas de Revit** procesa la exportación de paños y sus usos mediante la misma versión de `puente-losas` y `plugin-losas` indicada en el flujo. Utiliza el motor FEM de EstructurasRD, la comparación Pieper-Martens y la selección de acero del complemento. El trabajo corre en Linux con Python. Revit interviene al leer el modelo original y al aplicar cambios posteriores.

## Ejecutar desde la rama

Abra la rama `codex/memoriaplus-diseno` en GitHub y use **Add file → Upload files** dentro de la carpeta `entradas-losas`, o prepare los dos archivos localmente y haga un push. En un repositorio público use exclusivamente ejemplos sintéticos. Para un expediente real, utilice una copia privada y revise los permisos de acceso a los resultados.

| Archivo en la rama | Contenido |
| --- | --- |
| `entradas-losas/proyecto.json` | Exportación de losas de Revit con el bloque `losas`, marcas únicas, contornos, nivel y espesores en las unidades del contrato del puente. |
| `entradas-losas/usos.json` | Objeto con las marcas y sus asignaciones de uso, mampostería, vuelo o borde según corresponda. Revise las asignaciones y los supuestos antes de interpretar los resultados. |

Al subir el par en un mismo commit, GitHub inicia el cálculo en **Actions**. Si se suben en dos commits, la primera ejecución explica cuál falta. La segunda lee los dos. El seguimiento por push está configurado en `codex/memoriaplus-diseno` y `main`. Los pushes que cambian el propio motor o el flujo calculan también el par presente. En ausencia del par ejecutan la demostración sintética de seis paños. El botón **Run workflow** permite indicar dos rutas JSON alternativas dentro de `entradas-losas/` cuando el flujo también está en la rama predeterminada. Para la demostración manual deje ambos campos vacíos.

El contrato y el ejemplo usados por la acción están en la versión fijada del repositorio del puente. Se pueden descargar los dos ejemplos, renombrarlos `proyecto.json` y `usos.json` y probar el recorrido por push. [Ejemplo de losas y usos](https://github.com/dashy2004/CDCRD-Digital/tree/8340d0e3229b7e96a6d031826b56e521e7c171b7/puente-losas/ejemplo) · [Complemento pyRevit y cálculo de acero](https://github.com/dashy2004/CDCRD-Digital/tree/8340d0e3229b7e96a6d031826b56e521e7c171b7/plugin-losas)

## Leer la salida

Abra el resumen de la ejecución y descargue **calculo-losas** en **Artifacts**. Se conserva durante siete días. `informe_losas.xlsx` contiene las hojas Losas, Apoyos PM y Supuestos. `resultados.json` conserva los resultados FEM y Pieper-Martens por marca. `acero_pm.json` conserva la selección comercial del complemento y `lectura.json` identifica las entradas por SHA-256 y las revisiones exactas de código. `Revision para IA.md` transcribe los momentos PM con sus marcas y unidades e indica cuáles archivos adjuntar manualmente en una conversación. El informe se puede entregar al asistente que elija el usuario.

La pantalla **Generar** de MemoriaPlus espera un `data.json` preparado por los lectores del motor de memorias. El archivo `resultados.json` del puente tiene otro esquema, organizado en `panos` y `pieper_martens`. Para incorporar automáticamente esta salida al Word hace falta un adaptador que conserve marcas, apoyos, unidades y procedencia. El Excel y los JSON descargados se pueden revisar como respaldo independiente. El alcance de este ejemplo es el predimensionado de losas. Las hojas de diseño sísmico y los análisis nativos de ETABS y SAFE siguen un flujo diferente.
