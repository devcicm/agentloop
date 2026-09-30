# Contexto y memoria del CLI interactivo

## Antes

El hilo se conservaba solamente durante la ejecucion y se recortaba por numero
de mensajes. No habia recuerdos persistentes ni comandos para guardar y
recuperar conversaciones. Un resultado grande de una herramienta podia ocupar
mucho contexto aunque hubiera pocos mensajes.

## Uso

```text
recordar idioma=Responde en espanol
recordar proyecto=AgentLoop, CLI con proveedor NVIDIA
memoria
contexto
contexto limite 24000
sesion guardar
nuevo
sesion cargar
contexto compactar
olvidar proyecto
```

- `recordar clave=valor`: guarda o actualiza un recuerdo del proyecto; claves
  sin distincion de mayusculas. Se incorpora al mensaje de sistema cada turno.
- `memoria`: muestra los recuerdos. `olvidar clave`: elimina uno del almacenamiento.
- `contexto`: informa mensajes, caracteres JSON, limites y mensajes descartados.
- `contexto limite N`: cambia el limite y recorta inmediatamente si hace falta;
  acepta de 2000 a 500000 caracteres. El ajuste dura esta ejecucion.
- `contexto compactar`: conserva solamente el ultimo intercambio completo que
  quepa en el limite. Descarta los anteriores; no genera un resumen.
- `sesion guardar`: guarda el historial actual y reemplaza la copia anterior.
- `sesion cargar`: reemplaza el historial con la copia guardada, tras validar
  roles y correspondencia entre llamadas y resultados de herramientas.
- `nuevo`: borra el hilo vivo; conserva recuerdos, variables y plantilla.

## Archivos y alcance

Los recuerdos se guardan en `.agentloop/memory.json` y se cargan al arrancar el
CLI interactivo. La conversacion se guarda en `.agentloop/session.json` solamente
con el comando correspondiente; cargarla tambien es manual. Ambos archivos son
locales al proyecto y `.agentloop/` esta excluido de Git. Las escrituras usan un
archivo temporal y reemplazo del destino.

La sesion contiene mensajes; no restaura modelo, plantilla, variables, limite
personalizado ni contadores de tokens. Los recuerdos se almacenan por separado
y solo se crean por orden explicita. No hay extraccion automatica de recuerdos.
Cambiar entre GLM y Kimi conserva el hilo vivo y los recuerdos del proyecto.
Estas funciones corresponden al CLI interactivo. Desde el ajuste de arquitectura
del 28 de septiembre, el texto libre y `run` dentro del CLI usan el reactor de
eventos y reciben los recuerdos y antecedentes. Al terminar guardan un resumen
en el hilo y un informe en `.agentloop/operations/`. `chat` es la utilidad
conversacional explicita. Los comandos independientes no cargan esta memoria.
Ver [validacion de eventos](validacion-eventos-2026-09-28.md).

## Limites

El historial conserva hasta 40 mensajes y 48000 caracteres JSON por defecto.
El recorte elimina intercambios completos desde el principio, incluyendo sus
llamadas y resultados de herramientas. Si un unico intercambio supera el
limite, puede eliminarse completo. Guardar la sesion antes permite conservar
una copia; cargarla vuelve a aplicar los limites actuales.

La memoria admite hasta 12000 caracteres JSON y claves de hasta 80 caracteres.
Se rechazan archivos de memoria mayores de 100 KB y sesiones mayores de 4 MB.
Una peticion completa mayor de 600000 caracteres JSON se detiene antes de
enviarse al proveedor, incluyendo el contexto acumulado durante las herramientas.
Este control no resume resultados ni recupera automaticamente un turno excesivo.

Los caracteres no equivalen a tokens: estos limites no garantizan que una
peticion quepa en la ventana de cada modelo. No se implementa tokenizacion exacta,
busqueda vectorial ni resumen semantico. Los recuerdos se envian al proveedor
como parte del contexto cuando se realiza un turno.
