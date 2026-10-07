# Pendientes agenticos tras el setup

El eje sigue siendo eventos, tarjetas y evidencia: el LLM propone acciones y el
host admite, ejecuta, registra efectos y decide el cierre. Una respuesta de texto
no acredita que un proyecto se haya construido.

| Prioridad | Brecha y referencia | Propuesta | Criterio de cierre propuesto |
| --- | --- | --- | --- |
| Alta | Terminal arbitraria; proteccion de rutas de herramientas no equivale a aislamiento (`ProtectedResources`, admision) | Backend aislado con mounts, red y capacidades definidos por perfil externo | Comandos no confiables no acceden a secretos ni salen del scope |
| Alta | Clasificacion readonly por nombres (`OperationAdmission.IsReadOnly`) | Metadatos de capacidades y efectos por contrato de herramienta; defecto conservador | Herramientas nuevas declaran efectos sin editar una lista central |
| Alta | Credenciales en texto plano y raiz ligada a local-secrets (`SecretStore`) | Puerto de almacenes de secretos del host y resolucion independiente de workspace/datos | Cambiar almacen no cambia proyecto ni memoria; ningun secreto en DTOs/eventos |
| Alta | Recuperacion ante efecto externo incierto | Reconciliacion obligatoria por backend antes de reintentar; idempotencia por operacion | Una caida entre ejecucion y registro no duplica efectos |
| Media | Catalogo de modelos no garantiza disponibilidad ni herramientas | Sondas explicitas con vigencia y evidencia de capacidades por proveedor | La admision informa acceso/capacidades reales y caducidad |
| Media | Historial acotado y antecedentes heuristicos | Contexto estructurado de objetivo, restricciones, decisiones y referencias verificables | Compactar conserva criterios de cierre y restricciones del operador |
| Media | Utilidad heredada run-chat por turnos | Delimitar compatibilidad y compartir contratos con el reactor; deprecar gradualmente | F5/run/goal mantienen eventos como eje sin rutas alternativas de ejecucion |
| Media | Setup nuevo sin pruebas de persistencia dedicadas | Validar entrada oculta, cancelacion, ACL, reemplazo y errores Git con claves ficticias | No hay eco, ni cambios al cancelar, ni escritura en ruta versionada |
| Media | Sanitizacion por patrones no detecta todos los secretos | Propagacion de sensibilidad por DTO, redaccion antes de persistir y antes de exportar | Evidencia/contexto excluyen campos sensibles por contrato |
| Baja | Distribucion todavia candidata | Resolver licencia, canal privado, migracion de runtime y regenerar paquete | Candidato reproducible auditado; publicacion solo por autorizacion |

## Flujo objetivo

```text
Objetivo del operador
  -> admision (scope + capacidades + recursos + criterios)
  -> evento ObjetivoAdmitido
  -> propuesta LLM con contexto y referencias
  -> evento AccionPropuesta
  -> contrato de efectos y decision de permiso
       -> AprobacionSolicitada -> evento del operador
  -> backend aislado -> resultado tipado + evidencia
  -> reconciliacion si resultado incierto
  -> tarjetas actualizadas -> evaluacion de criterios
       -> recurso/accion pendiente -> nuevo evento
       -> evidencia suficiente -> cierre comprobado

Credenciales: operador -> wizard/almacen -> composicion del cliente
             (fuera del contexto, herramientas y eventos del agente)
```

Este documento es un backlog; no afirma que las propuestas esten implementadas.
El setup y la eliminacion del fallback entre proveedores si se implementaron.
No introduce reglas POS ni logica de negocio en Core o Kernel.
