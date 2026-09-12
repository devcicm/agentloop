# Síntoma: el goal no cierra (SIN CERRAR) — diagnóstico y correcciones

Documento vivo del problema "el reactor trabaja pero la operación nunca
llega a `cerrar`". Cubre el síntoma, cómo diagnosticarlo con el comando
`diagnostico`, las causas raíz encontradas con su evidencia y las
correcciones aplicadas con su commit.

## 1. Síntoma

Un `goal` (o `lobos`) corre durante todo su presupuesto, el modelo ejecuta
herramientas reales y juega tarjetas de avance, pero la operación termina con:

```
SIN CERRAR: 1 operacion(es) sin cerrar; ...: juega la tarjeta de
comprobacion con evidencia real y despues la de cierre.
FINAL: el goal NO se cumplio.
```

El libro mayor queda con la transacción raíz abierta (`abrir` + N `avanzar`,
0 `comprobar`, 0 `cerrar`). El caso canónico observado: 22 ciclos, 15 tools,
5 tarjetas, 10 procesos reales, 620s, `BreakReason: MaxIterations`.

## 2. Cómo diagnosticar

### 2.1 Automático (recomendado)

```bash
dotnet run --project AgentLoop.csproj -- diagnostico
dotnet run --project AgentLoop.csproj -- diagnostico <runId> --journal-dir artifacts/journeys
```

Solo lee disco (el snapshot `artifacts/journeys/<runId>.json`). No toca la
red ni ejecuta el modelo. Devuelve 0 si el informe se imprimió. Clasifica el
cierre en: `cerrado`, `inanicion`, `presupuesto-watchdog`,
`presupuesto-agotado`, `rechazos`, `desconocido`, `ilegible`.

Limitación honesta: los snapshots no guardan `BreakReason` ni uso de tokens,
así que la clasificación fina es heurística y los tokens se reportan como no
persistidos.

### 2.2 Manual (procedimiento por etapas)

1. Leer `BreakReason`: `MaxIterations` = inanición; `TerminalState` + `Fallido`
   = el reactor abortó (ver `FailureReason`).
2. Leer la línea `pendiente:` del libro mayor: nombra la arista faltante.
3. Recorrer los plays: `RECHAZADA` = fallo de validación (schema/orden);
   `abrir, avanzar...` sin `comprobar` = sin evidencia declarada.
4. Medir razón tools:tarjetas y narraciones (`respondiste texto sin usar
   herramientas`): ratio alto = el agente trabaja sin declarar progreso.
5. Revisar salidas de `ejecutar` (código, duración, salida): confirma si el
   modelo incorpora datos reales o los ignora.
6. Revisar crecimiento de tokens del resumen: bloat de contexto degradan el
   cumplimiento de herramientas (`Trim` corta por conteo, no por bytes).
7. Cruce con filesystem: ¿existe el archivo que el objetivo pide? Si el
   modelo afirmó escribirlo y no existe, alucinó el resultado.

## 3. Causas raíz encontradas

### 3.1 Deadlock en `CardLedger` (infraestructura, crítica)

`DescribeState()` tomaba el lock y llamaba por dentro a `PendingDebt()` y a
la propiedad `Rejected`, que tomaban el mismo lock. El lock era un
`AsyncLock` sobre `SemaphoreSlim`, que **no es reentrante**: el segundo
`Wait()` en el mismo hilo bloquea para siempre.

Síntoma visible: el reactor se colgaba en `Brief → DescribeState` antes de
la primera llamada al modelo (el log se detenía en `[reason-async]
entrando`). Se confundía fácilmente con "el modelo tarda", pero el modelo
ni siquiera había sido invocado.

Corrección: commit `49973bd` — lock reentrante con `Monitor`, que sí
permite las lecturas anidadas. `AsyncLock` sigue usándose donde toca
(secciones async sin reentrancia).

### 3.2 Sin watchdog durante la llamada al modelo (infraestructura, crítica)

El chequeo `context.Expired` solo corría al inicio de cada iteración. Una
vez dentro de `ReasonAsync`/`WaitAsync`, una llamada colgada bloqueaba al
reactor más allá del budget. Combinado con `MaxRetries = 6` y timeout de
60s por intento, una sola iteración podía estirarse a 5+ minutos.

Corrección: commit `3c1b78f` — `CancellationTokenSource(context.Remaining)`
linkeado al token externo; si dispara el watchdog (y no fue Ctrl+C), se
marca `DeadlineReached` y se devuelve `Fallido` con motivo explícito.
`Despachando` queda fuera a propósito (sus tool calls tienen su propio tope
por política). Commit `3fd0dfb` — retries 6→3, backoff base 2s→1s, tope
10s→5s. Commit `8918c31` — `WatchdogSelfTest` con ambos casos.

### 3.3 Status 0 tratado como fallo permanente (infraestructura, media)

Una corrida murió en el ciclo 1 con `[0] Timeout tras 60s` clasificado como
no-transitorio: el reactor fue directo a `Fallido` sin reintentar. Status 0
significa "sin respuesta HTTP" (timeout/DNS/conexión): transitorio por
definición.

Corrección: commit `be1859d` — `IsTransient` incluye status 0 junto a 5xx
y 429.

### 3.4 Avisos sin ejemplo accionable (prompt, media)

Cuando el modelo narraba sin herramientas o ejecutaba tools sin jugar
tarjetas, el aviso listaba las tarjetas legales pero sin mostrar cómo
invocarlas. Cada ciclo desperdiciado cuesta ~1 min de llamada al modelo
(GLM 5.3 con thinking).

Corrección: commits `74f881b` (`TextOnlyStreak`: ejemplo tras 2+
narraciones), `1a08ee3` (`ToolCallsSinceCard`: ejemplo tras 3+ tools sin
tarjeta), `7d069e6` (el ejemplo sugiere la tarjeta más progresiva legal:
`cerrar` si verificado, `comprobar` si hay avances, `avanzar` si hay
transacción abierta). Verificado en corridas reales: cada aviso fue
seguido de una tarjeta en el ciclo siguiente.

### 3.5 Comportamiento del modelo (externo, no corregible en código)

GLM 5.3-free investiga de verdad (Wikipedia, Bing, DuckDuckGo, Fandom;
sortea captchas y documenta cada paso en `avanzar`) pero no converge al
cierre: no escribe el archivo ni juega `comprobar`/`cerrar`. Con fuentes
bloqueadas (Cloudflare, anti-bots) nunca reúne la "evidencia real" que su
propio criterio le exige. Ningún fix del harness lo cierra; hacen falta más
iteraciones (~40+) o un modelo que decida cerrar.

## 4. Estado actual

- `selftest`: verde (incluye watchdog, nudges, diagnóstico, concurrencia,
  sanitización).
- `diagnostico`: clasifica correctamente journeys reales.
- `lobos` con GLM 5.3: llega a `abrir` + N `avanzar` + research real, no
  cierra. Honesto por diseño: imprime `FINAL: el goal NO se cumplio`.
- Secretos: solo en `local-secrets/` (gitignored); la key sale enmascarada
  en logs; ningún archivo trackeado la contiene.
