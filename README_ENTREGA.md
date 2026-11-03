# Analizador de ensamblador — Fase 2

## Uso

Ejecutable para Windows con .NET Framework 4.x. Ábrelo y selecciona un archivo `.ens`. Presenta la separación e identificación léxica, el resultado sintáctico-semántico de cada línea y la tabla de símbolos.

## Alcance

Valida los segmentos de pila, datos y código, las declaraciones `DB`, `DW`, `DUP` y `EQU`, instrucciones asignadas al Equipo 2, tamaños de operandos y etiquetas usadas después de declararse. Las etiquetas se muestran con valor `Pendiente`; no se genera código máquina.

El ZIP incluye tres plantillas de conformidad en `plantillas_conformidad/`. Todas sus líneas deben resultar correctas. Las entradas heredadas se conservaron en el proyecto de trabajo y se excluyeron de esta entrega porque contienen líneas incompatibles con el subconjunto asignado.

Los rangos numéricos implementados son −128 a 255 para byte y −32768 a 65535 para palabra. La diapositiva de clase 17 da rangos distintos; se conservó el criterio de Moodle para constantes con o sin signo y esa diferencia queda pendiente de confirmación con la profesora.
