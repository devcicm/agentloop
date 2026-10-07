# Credenciales locales

`local-secrets/` esta excluido de Git, salvo este README. El archivo dedicado
es `nvidia-api-key.txt`: una sola linea con la clave NVIDIA, sin comillas.

La resolucion efectiva en [SecretStore.cs](../src/AgentLoop.Infrastructure/Credentials/SecretStore.cs) es:

1. Variable `NVIDIA_API_KEY`, si contiene un valor.
2. Archivo `nvidia-api-key.txt`.

No se reutilizan claves TokenRouter para NVIDIA. Se busca `local-secrets/` desde
el binario y el directorio actual, subiendo por sus ancestros.

Configura con `setup` en la consola o `dotnet run --project src/AgentLoop.Cli -- setup`.
Consulta `setup --status`. La entrada es oculta; no pases secretos como argumentos.
El wizard comprueba que el archivo no este versionado y este ignorado, rechaza
redirecciones de rutas y guarda con acceso limitado al usuario. El archivo sigue
siendo texto plano: no es un almacen cifrado. Los archivos creados manualmente no
reciben automaticamente esos permisos. No se envia la clave al LLM.

El endpoint vive en `src/AgentLoop.Infrastructure/Providers/Nvidia/NvidiaOptions.cs`.
La consola muestra estado y procedencia, nunca fragmentos de la clave.
No crear otra copia ni incluir secretos en prompts, documentacion o evidencia.

GitHub CLI guarda su token en el keyring del sistema operativo. Usar `gh auth
login` y `gh auth status`; no copiar su token al codigo ni a un archivo del repo.
Los clientes LLM reciben la credencial desde SecretStore en la composicion del
host; Core y Kernel no resuelven ni almacenan secretos.

Auditoria local sin mostrar valores: `python scripts/audit-git-secrets.py`.
Revisa todos los objetos Git, incluidos los no referenciados, refs/reflogs y
archivos versionables. Compara tambien las claves del almacen local sin imprimirlas.
El informe por defecto queda en `.agentloop/`, excluido de Git. No detecta todos
los formatos opacos posibles; los ejemplos de mascara uniforme se clasifican aparte.
