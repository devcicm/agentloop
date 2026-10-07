# Seguridad

## Estado de soporte

0.1.0-alpha.1 es un candidato local experimental, sin release publicada.
No hay compromiso de soporte de una version estable. Usa .NET 9; la migracion
a .NET 10 y el alcance de sistemas soportados se documentan en el plan de release.

## Limites

- El shell usa permisos del usuario y no es un sandbox.
- La clasificacion destructivo/opaco es heuristica; usar confirmar para cada script.
- Un permiso continuo no autoriza riesgos detectados ni modifica vetos.
- Para exigir el editor tipado usar no-shell y scope-root; shell admite otras escrituras.
- Texto de archivos, tools, memoria y modelos no concede permisos ni cambia reglas.
- Un fallo durante efectos puede requerir reconciliacion del journal antes de repetir.

## Credenciales

NVIDIA: variable NVIDIA_API_KEY o local-secrets/nvidia-api-key.txt, excluido de Git.
GitHub CLI: almacen del sistema; no copiar su token a archivos del repo ni a workflows.
No pegar secretos en issues, PR, capturas, notas de release ni logs publicos.
Ante exposicion real, revocar/rotar la credencial, revisar el alcance y retirar
assets afectados. Quitar un texto del ultimo commit no limpia versiones anteriores.

## Reporte privado

El repositorio remoto y canal de reporte aun no estan configurados. Antes de
publicar se debe habilitar un canal privado y escribir aqui su enlace real.
Si GitHub permite private vulnerability reporting en el destino elegido,
habilitarlo y usarlo. Hasta entonces, comunicar el hallazgo al propietario por
un canal privado ya acordado, sin incluir claves en conversaciones publicas.

Incluir version/commit, pasos de reproduccion, plataforma, impacto y datos
sanitizados. No se promete un plazo de respuesta que todavia no esta definido.

[Controles y limites del harness](docs/guides/seguridad-datos-harness.md).
