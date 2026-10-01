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

El endpoint esta en `Provider/TokenRouterOptions.cs`. La consola enmascara la
clave al informar su procedencia. No crear una segunda copia del secreto ni
incluir una clave real en documentacion, prompts o evidencia versionada.
