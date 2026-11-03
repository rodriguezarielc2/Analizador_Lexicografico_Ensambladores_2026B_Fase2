# Analizador de ensamblador — Fase 2

Proyecto independiente de Fase 1. Conserva la carga de archivos `.ens`, la separación y clasificación léxica del Equipo 2, y agrega la revisión sintáctico-semántica por línea y la tabla de símbolos. La carpeta de Fase 1 y los archivos `.asm` originales no se modifican.

## Ejecutar y compilar

Abre `run.bat` para compilar Fase 2, ejecutar las verificaciones automatizadas y abrir la aplicación. Requiere Windows y el compilador de .NET Framework 4.x.

Para compilar y ejecutar las verificaciones sin iniciar una ventana:

```powershell
./build.ps1 -SinPruebaInterfaz
```

Al abrir un archivo `.ens`, la aplicación muestra el fuente, la lista léxica, el dictamen sintáctico-semántico por línea y la tabla de símbolos. Usa **Ctrl + rueda del mouse** para acercar o alejar; haz clic en el porcentaje de zoom para volver a 100%.

## Carpetas y archivos

- `AnalizadorFase2.cs`: ventana principal, carga de fuente, lista léxica, paginación y zoom.
- `LexicalAnalysisCore.cs`: análisis léxico conservado de Fase 1, con las adiciones específicas de esta etapa.
- `SyntaxSemanticCore.cs` y `SyntaxSemanticModels.cs`: validación por línea, diagnósticos y tabla de símbolos.
- `SyntaxSemanticResultsForm.cs`: presentación de resultados y tabla.
- `instrucciones_equipo2.txt`: catálogo limitado a las instrucciones asignadas al Equipo 2.
- `verificacion/tests/`: pruebas automatizadas del análisis léxico, semántico, interfaz y plantillas.
- `verificacion/plantillas_conformidad/`: tres fuentes `.ens` que ejercitan las reglas de esta etapa. Las pruebas comprueban que todas sus líneas sean correctas, que los encabezados Moodle estén en orden, que los cuatro campos de símbolos esperados coincidan y que el conjunto cubra todas las instrucciones asignadas.
- `verificacion/ejemplos/`: pruebas léxicas pequeñas versionadas. Tres fuentes ASM del usuario pueden estar presentes localmente para una prueba de procesamiento; `.gitignore` las excluye del repositorio y de la entrega.
- `build/`: ejecutable y catálogo temporales para abrir la aplicación desde `run.bat`; son productos generados y están excluidos del control de versiones.
- `Entregable_Fase2/`: contiene únicamente `Entrega_Fase2.zip`, el archivo que se sube a Moodle.

## Alcance implementado

El analizador valida el orden de pila, datos y código; el cierre con `ENDS`; las declaraciones `DB`, `DW`, `DUP` y `EQU`; los operandos y sus tamaños cuando aplica; y el uso de etiquetas previamente declaradas en instrucciones de transferencia. Para cada línea muestra `Correcta` o `Incorrecta` y una explicación. La tabla contiene símbolo, tipo, valor y tamaño.

Los encabezados `.stack segment`, `.data segment` y `.code segment` de Moodle se conservan. También se aceptan las formas alternativas que aparecen en las diapositivas: `STACK SEGMENT`, `DATA SEGMENT`, `CODE SEGMENT` y los encabezados abreviados `.STACK`, `.DATA`, `.CODE`. `LES` admite `DWORD PTR` en operandos de memoria, conforme al ejemplo del material de codificación del Equipo 2; se rechaza si el operando declarado no reserva al menos cuatro bytes.

La dirección de las etiquetas no se calcula: se muestra `Pendiente` y su tamaño como `No aplica`. Esta fase no genera código máquina ni emula el programa.

## Criterios y discrepancias de los materiales

- Para constantes byte, el analizador usa el intervalo −128 a 255; para palabra, −32768 a 65535. La diapositiva de clase 17 muestra intervalos distintos (−127 a 128 y −32768 a 32767), mientras que Moodle describe constantes con o sin signo. Se conservó el criterio de representación byte/palabra con signo y sin signo; conviene confirmar el intervalo con la profesora si exige literalmente la diapositiva.
- Para `AND` y `ADC`, los registros y operandos de memoria deben tener el mismo ancho. Un inmediato se acepta cuando cabe en el destino; por ejemplo, `AND AX, 1` es válido. El material general muestra ejemplos de ancho de inmediato para `MOV`, que no pertenece al subconjunto asignado al Equipo 2; esa regla no se extrapoló a `AND` y `ADC`.
- Los tres archivos en `verificacion/ejemplos/` son entradas heredadas, no casos que deban resultar completamente correctos. La verificación de conformidad se hace con `plantillas_conformidad/`.

## Entregable

Ejecuta `build.ps1` para compilar, correr las verificaciones y generar `Entregable_Fase2/Entrega_Fase2.zip`. El ZIP contiene un ejecutable, los cinco archivos fuente, el catálogo del Equipo 2, `README_ENTREGA.md`, las tres plantillas válidas y `MANIFEST.sha256`. Las copias locales de programas ASM permanecen fuera del repositorio y del ZIP. La prueba opcional de esas entradas se ejecuta solo si las copias están en el equipo; el resto de pruebas no depende de ellas. El proceso limpia copias generadas antiguas de la carpeta de entrega y rechaza un ZIP de más de 5 MB. Para compilar sin iniciar ventanas usa `./build.ps1 -SinPruebaInterfaz`.

La información disponible para esta revisión no define por separado los requisitos de una Fase 3; este paquete cubre la Fase 2 descrita en Moodle.
