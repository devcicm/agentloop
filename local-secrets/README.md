# local-secrets

Esta carpeta guarda credenciales en texto plano **fuera del control de versiones**.
El `.gitignore` de la raíz ignora `local-secrets/` completo, salvo este README.

## nvidia-api-key.txt

Una sola línea con la API key de NVIDIA NIM, sin comillas ni prefijo:

```
sk-xxxxxxxxxxxxxxxxxxxxxxxx
```

Se obtiene en https://build.nvidia.com/

`AgentLoop/Configuration/SecretStore.cs` lee este archivo. Si no existe o está
vacío, cae a la variable de entorno `NVIDIA_API_KEY`. El archivo legado
`tokenrouter-api-key.txt` y `TOKENROUTER_API_KEY` solo se aceptan durante la
migración.

> Aviso: texto plano en el árbol del repo. `git add -f`, un backup de la carpeta
> o compartir el proyecto comprimido exponen la key. Para endurecerlo, usa
> `dotnet user-secrets` o un gestor de secretos.
