# Puerta de calidad local — 2026-10-11

Cambios evaluados: telemetría segura, sesión v2, edición recuperable y contrato
de transporte HTTP. Esta carpeta describe resultados locales; no contiene
prompts, credenciales ni llamadas remotas.

## Correcto

- `dotnet build AgentLoop.sln -c Release --no-restore`: cero errores y cero advertencias.
- `selftest-session`: persiste mensajes, intención, objetivo y restricciones.
- `selftest-provider`: rechaza HTTP, reintenta un 5xx y bloquea al agotar la cuota HTTP.
- `selftest-release`: 19/19. En un contexto que no permite modificar ACL, el
  respaldo recuperable falla cerrado y no altera el archivo; en un contexto
  propietario se ejerce el flujo preview → patch_id → apply.

## Resultado del coordinador

La suite completa `selftest` vuelve a pasar después de actualizar tres
contratos que cambiaron deliberadamente:

1. Una escritura sin `read_receipt` vigente se rechaza antes de modificar la
   evidencia, por lo que el cierre previamente comprobado sigue siendo válido.
2. Cancelar una tool de efecto desconocido deja una acción incierta y exige
   reconciliación; no se afirma que el efecto no ocurrió.
3. Un objetivo sin criterios usa intención `General` en la prueba de Journey.
   `Build` conserva el requisito de criterios explícitos.

No se realizó la migración a .NET 10 LTS: solo está instalado SDK 9.0.316. La
publicación sigue pendiente de esa migración, licencia, revisión final de
secretos y la decisión explícita de crear un remoto/tag/release.
