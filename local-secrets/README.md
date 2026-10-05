# Credenciales locales

`local-secrets/` esta excluido de Git, salvo este README. El archivo dedicado
es `nvidia-api-key.txt`: una sola linea con la clave NVIDIA, sin comillas.

La resolucion efectiva en [SecretStore.cs](../Configuration/SecretStore.cs) es:

1. Variable `NVIDIA_API_KEY`, si contiene un valor.
2. Archivo `nvidia-api-key.txt`; si no existe, archivo legado `tokenrouter-api-key.txt`.
3. Variable legada `TOKENROUTER_API_KEY`.

Se busca `local-secrets/` desde el directorio del binario y el directorio actual,
subiendo por sus ancestros. El fallback legado conserva compatibilidad;
una clave de otro proveedor no acredita acceso a NVIDIA. Si el archivo NVIDIA
existe pero esta vacio, no se utiliza el archivo legado como reemplazo.

El endpoint esta en `Provider/Nvidia/NvidiaOptions.cs`. La consola enmascara la
clave al informar su procedencia. No crear una segunda copia del secreto ni
incluir una clave real en documentacion, prompts o evidencia versionada.

GitHub CLI guarda su token en el keyring del sistema operativo. Usar `gh auth
login` y `gh auth status`; no copiar su token al codigo ni a un archivo del repo.
Los clientes LLM reciben la credencial desde SecretStore en la composicion del
host; Core y Kernel no resuelven ni almacenan secretos.

Auditoria local sin mostrar valores: `python scripts/audit-git-secrets.py`.
Revisa todos los objetos Git, incluidos los no referenciados, refs/reflogs y
archivos versionables. Compara tambien las claves del almacen local sin imprimirlas.
El informe por defecto queda en `.agentloop/`, excluido de Git. No detecta todos
los formatos opacos posibles; los ejemplos de mascara uniforme se clasifican aparte.
