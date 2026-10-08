# Resultados, errores y trazabilidad

Se reutiliza `Result<T,E>` del Core. `Failure` representa errores seguros con
`Code`, `Message`, `CorrelationId`, `Retryable` y `StatusCode` opcional. No contiene
excepciones, headers, claves, prompts ni cuerpos remotos. `Result.ToString()`
solo indica ok/fail y no convierte los valores sensibles a texto.

## Fronteras implementadas

| Frontera | Contrato | Consumo |
| --- | --- | --- |
| Resolver credencial | `Result<ResolvedCredential, Failure>` | Composicion del cliente y arranque |
| Guardar credencial | `Result<Unit, Failure>` | Wizard; no anuncia exito si falla |
| Ejecutar comando | `Result<int, Failure>` mediante `ResultBoundary` | Codigo de salida y mensaje seguro |
| Cerrar reactor | `EventOperationResult.Outcome` | Estado de negocio, Failure y registro local |
| Verificar proveedor | `Result<Unit, Failure>` | Consulta acotada del catalogo |

Las excepciones del transporte existente se adaptan mediante `IFailureSource`;
no se cambia el puerto neutral `ILLMClient` ni el eje de eventos. El reactor
conserva el codigo seguro del fallo remoto al cerrar, aunque su adaptador de
compatibilidad siga usando excepciones internamente. Los DTOs de herramientas
y validadores existentes conservan sus contratos; no se envuelve cada getter.

```text
Inicio / comando / objetivo
  -> frontera Result + actividad de traza
  -> validacion o ejecucion
       -> Ok(valor) -> consumidor decide estado de negocio
       -> excepcion -> clasificador -> Fail(Failure)
  -> metadatos: correlacion, duracion, exito, codigo
  -> consola segura / codigo de salida / evento de cierre

Credencial: resolver -> dato sensible SOLO al cliente
                       (sin ToString del valor ni serializacion JSON del valor)
```

## Errores de credenciales

- `credentials.missing`: no hay clave; usar `setup` o variable dedicada.
- `credentials.malformed`: inadecuada por formato o tamaÃƒÂ±o local.
- `credentials.rejected`: HTTP 401 del proveedor.
- `provider.forbidden`: HTTP 403; acceso denegado, no identifica por si solo causa.
- `provider.quota`: HTTP 402; acceso/saldo requerido.
- `provider.request`: otro rechazo; incluye estado y posibilidad de reintento.
- `operation.cancelled`: cancelacion o deadline. No se afirma que la clave sea invalida.

La consola abre incluso si faltan credenciales para permitir configurarlas. Si
hay una clave con formato valido, consulta `/models` con limite de 20 segundos;
`--offline` lo omite. La aceptacion de ese endpoint no garantiza acceso a un
modelo ni herramientas. `credenciales verificar` permite repetir la consulta.
Los comandos locales como ayuda/version/setup no requieren acceso remoto.

## Observabilidad

`AgentLoop.Boundaries` es un `ActivitySource`; la actividad tambien existe sin
un listener instalado para mantener la correlacion durante tareas asincronas.
Se anotan estado y codigo, sin registrar excepciones completas. Un listener puede
exportar las actividades desde el host; no se configura un destino externo.

La consola agrega metadatos a `.agentloop/boundary-results.jsonl`, ignorado por
Git y excluido del paquete. A partir de 10 MiB conserva una generacion anterior.
El registro puede fallar: se informa `observability.unavailable`; no se convierte
en exito un fallo de negocio ni se repite una accion ya ejecutada. Los observers
no alteran el resultado. Un comando que devuelve codigo distinto de cero sigue
siendo un retorno capturado, no una excepcion: su codigo determina el exito CLI;
la observacion CLI marca el retorno no cero como fallo; el resultado del reactor
registra el estado de negocio separadamente.

## Compatibilidad y validacion

Se mantienen APIs anteriores (`TryGetLLMApiKey`, `Save`, excepciones y resultados
de negocio) para consumidores existentes. Las nuevas rutas CLI usan Result en
las fronteras indicadas. Esto no constituye una migracion completa de todos los
adaptadores heredados, ni un sandbox o un almacen cifrado.

Compilacion Release realizada sin errores ni advertencias. No se aÃƒÂ±adieron ni
ejecutaron pruebas automatizadas ni sondas reales en esta implementacion. Quedan
por verificar con proveedor simulado: 401/403/timeout, ausencia/formato, fallos
de almacenamiento, correlacion y cancelacion; sin cambiar la clave real.
El ZIP anterior no incluye estos cambios y requiere regenerarse antes de publicar.
