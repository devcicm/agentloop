# Wizard de credenciales

En fuentes: `dotnet run --project src/AgentLoop.Cli -- setup`.
En la consola interactiva o en el ejecutable: `setup`. Consulta: `setup --status`.
No acepta claves como argumentos, no imprime valores y no envia secretos al modelo.

```text
setup (operador; operacion inactiva)
  -> consultar estado y origen sin mostrar valor
  -> confirmar CONFIGURAR o REEMPLAZAR
  -> capturar secreto sin eco (Escape cancela)
  -> validar valor y ruta; rechazar enlaces
  -> comprobar Git: destino ignorado y no versionado
  -> crear temporal con permisos del usuario
  -> escribir y sustituir archivo dedicado
  -> descartar cliente cacheado; siguiente objetivo usa cliente nuevo
```

Destino: `local-secrets/nvidia-api-key.txt`, bajo la raiz resuelta por SecretStore.
`NVIDIA_API_KEY` tiene prioridad sobre el archivo. No se aceptan credenciales de
TokenRouter como respaldo NVIDIA. GitHub sigue usando `gh auth login` y el
almacen del sistema: el wizard no extrae su token ni inicia publicaciones.

El wizard exige consola interactiva, pide confirmacion antes de reemplazar y no
realiza solicitudes de red. Guardar una clave no prueba su validez ni el acceso
a un modelo. No se modifica la credencial actual por instalar esta funcion.

## Limites

- El archivo es texto plano con ACL del usuario en Windows o modo 0600 en Unix.
  No es cifrado; usuarios privilegiados y procesos de ese usuario pueden leerlo.
- En una distribucion sin repositorio Git no se exige Git instalado. En un repo
  la comprobacion es obligatoria; un error impide guardar.
- La busqueda ancestral del almacen y la raiz de datos todavia comparten
  resolucion. Consulta el destino mostrado antes de confirmar.
- Las herramientas tipadas protegen `local-secrets`; una terminal arbitraria
  no constituye un sandbox. Usa aislamiento para ejecutar codigo no confiable.
- Las cadenas de .NET no permiten garantizar borrado de todas las copias de RAM.

## Revision del 7 de octubre de 2026

Auditoria previa: 1208 blobs, 579 arboles y 86 commits, incluidos objetos no
referenciados; cero candidatos en objetos y archivos versionables. Revision
adicional: 735 archivos de texto en `.agentloop` y `.vs`, cero candidatos;
472 archivos binarios, de otros formatos o mayores de 20 MB no incluidos.
El almacen dedicado se usa para comparacion exacta en memoria y nunca se imprime.
Esto no demuestra ausencia de secretos opacos o en formatos no inspeccionados.
Informes locales ignorados: `.agentloop/setup-secrets-before.json` y
`.agentloop/setup-runtime-secrets.json`.

La implementacion compilo en Release sin errores ni advertencias. No se ejecuto
el wizard para sustituir la clave real, ni nuevas pruebas de escritura o de red.
El candidato ZIP anterior conserva su evidencia original; requiere regenerarse
para incluir esta implementacion. No se publico ningun cambio.
