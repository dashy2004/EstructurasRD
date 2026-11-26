# Fuentes de la memoria y revisión del checklist

En **MemoriaPlus → Generar → Archivos** seleccione el modelo ETABS (`.edb`), la cimentación SAFE (`.fdb`), el libro de cargas (`.xls`, `.xlsx` o `.xlsm`) o el listado de Losas.exe (`.txt` y su `.dl`). Un manifiesto local `fuentes.json` permite asociar libros de espectro y Art. 38 con su bloque de ETABS. **Extraer y guardar datos** escribe un `data.json` reutilizable. La lectura de modelos requiere CSI instalado y licencia disponible. El extractor trabaja con copias. Las hojas de Excel se leen con sus valores guardados.

En **GitHub** pegue el enlace a un `data.json` público y elija dónde guardar su copia local. En **Proyecto o JSON** puede abrir los datos de disco. **Generar memoria de diseño** produce un Word editable. **Preparar para ChatGPT** guarda un Markdown con los datos del informe y su SHA-256. El usuario revisa nombres, ubicaciones y resultados antes de adjuntarlo. La aplicación transmite únicamente la solicitud de lectura a GitHub cuando se utiliza esa opción.

El botón **Generar** y el cálculo de losas de GitHub utilizan contratos diferentes. El informe de losas devuelve sus propios resultados y un archivo de revisión, que se pueden consultar independientemente. [Cálculo desde la exportación de Revit](calculo-losas-github.md)

## Revisión sísmica por piso

El flujo **Comprobar derivas de diseño por piso** llama al verificador de CDCRD-Digital fijado en `4739679`. El ejemplo de regresión reproduce el caso de igualdad del autotest de esa herramienta. La entrada usa altura de entrepiso y desplazamiento de diseño en metros, dirección, caso, tipo estructural, categoría de riesgo y condiciones de la tabla. Un cociente de deriva elástica exportado de un modelo necesita conversión y trazabilidad antes de utilizarse como desplazamiento de diseño.

El push a la rama `codex/memoriaplus-diseno` de un archivo `entradas-sismo/derivas.json` activa la verificación y conserva la entrada y el resultado en el artefacto `revision-derivas`. En un repositorio público use exclusivamente ejemplos sintéticos. Para expedientes reales, utilice un repositorio privado autorizado o ejecute el verificador local. Un resultado **INCOMPLETO** identifica datos físicos o de aplicabilidad pendientes. El resultado describe la revisión de las filas aportadas. La deriva global, el espectro y las demás comprobaciones requieren sus propios datos.

## Reutilización del cálculo existente

CDCRD-Digital ya implementa verificaciones de deriva, escalado de cortante dinámico, irregularidades torsionales y verticales, masa modal y zapatas. La integración pendiente consiste en convertir tablas de ETABS y libros de cálculo en entradas de las unidades y edición que espera cada verificador y en mostrar las evidencias en la memoria. Por ejemplo, el ítem 39 requiere distinguir los ejes, los casos y los cortantes estáticos y dinámicos de cada bloque. Una coincidencia de nombres de hoja es insuficiente para adjudicar un resultado al modelo.

Para digitalizar el Excel sísmico empezaría por guardar una tabla normalizada por parámetro con su unidad, eje, caso, valor, libro, hoja y celda. Después ejecutaría el mismo caso en el código y en una copia recalculada en Excel. El ensayo debe cubrir datos ausentes, fórmulas con error, direcciones intercambiadas y resultados guardados de una revisión anterior. Las hojas patrón pueden permanecer en el equipo de trabajo. [Verificadores de CDCRD-Digital](https://github.com/dashy2004/CDCRD-Digital/tree/4739679733444007a6520a6c49b7c389f45056f4/herramientas)

## Próximas funciones por criterio

| Lista e ítem | Función por completar | Prueba de aceptación propuesta |
|---|---|---|
| Estructura 32, 35, 39 | Digitalizar los libros de espectro y cuasiestático y conciliar los cortantes estático y dinámico por bloque, dirección y combinación. Conservar las fórmulas, unidades, factores y edición normativa. | Reproducir un libro patrón recalculado en Excel y detectar un cambio de unidad, versión o bloque. |
| Estructura 37 y 38 | Convertir la salida correcta de ETABS a deriva de diseño, vincular la revisión por piso con la memoria y calcular por separado la deriva global. | Contrastar los resultados con las tablas del mismo modelo. Registrar parámetros ausentes y evitar atribuir aprobación global a una sola fila. |
| Estructura 46, 55, 58 | Conciliar las reacciones de ETABS con SAFE y vincularlas a los identificadores de cimentaciones, dimensiones y detalles de planos. | Recuperar combinación, unidades, objeto y fuente para cada discrepancia. |
| Sanitaria 1 y 16; Eléctrica 8 | Relacionar memoria hidráulica, isométricas y diagrama unifilar con las redes y los circuitos leídos de Revit. | Señalar cada elemento sin sistema leído, sin plano o sin memoria de respaldo. |
| Arquitectura 4 | Vincular el plano de curvas de nivel con superficie, fecha y lámina de Civil 3D. | Avisar si la superficie publicada está desactualizada. |
