# Estructura actual y propuesta del repositorio

Propuesta para revision, no implementada. Base: 654831d. No se movieron archivos ni se ejecutaron pruebas.

## Inventario

283 archivos versionados; 139 archivos C#. Program.cs tiene 2644 lineas. La solucion tiene hoy tres proyectos: ejecutable, Core y Kernel.

| Carpeta | Archivos versionados |
| --- | ---: |
| `(raiz)` | 5 |
| `.github` | 1 |
| `Cards` | 2 |
| `Cli` | 1 |
| `Configuration` | 1 |
| `Core` | 9 |
| `Discovery` | 2 |
| `Events` | 3 |
| `Goals` | 21 |
| `Harness` | 50 |
| `Kernel` | 8 |
| `Looping` | 2 |
| `Parsing` | 4 |
| `Prompting` | 6 |
| `Properties` | 1 |
| `Provider` | 10 |
| `Security` | 9 |
| `StateMachines` | 7 |
| `Terminals` | 3 |
| `Tools` | 5 |
| `docs` | 125 |
| `examples` | 5 |
| `local-secrets` | 1 |
| `scripts` | 2 |

## Antes: inventario completo versionado

```text
agent-loop/
+-- .github/
|   `-- copilot-instructions.md
+-- Cards/
|   +-- CardLedger.cs
|   `-- StateCard.cs
+-- Cli/
|   `-- ProjectCommands.cs
+-- Configuration/
|   `-- SecretStore.cs
+-- Core/
|   +-- Context/
|   |   `-- ProjectContextBudget.cs
|   +-- LLM/
|   |   `-- ResponseBudgets.cs
|   +-- Projects/
|   |   +-- Contracts/
|   |   |   +-- ProjectExecutionDtos.cs
|   |   |   `-- ProjectGoalDto.cs
|   |   +-- ProjectKnowledge.cs
|   |   +-- ProjectState.cs
|   |   `-- ProjectValidatorPort.cs
|   +-- AgentLoop.Core.csproj
|   `-- README.md
+-- Discovery/
|   +-- AgenticModelSelector.cs
|   `-- ModelCapabilityReport.cs
+-- docs/
|   +-- evidence/
|   |   +-- 2026-09-27/
|   |   |   +-- nvidia-poem-workspace/
|   |   |   |   `-- notes/
|   |   |   |       `-- poema-original.md
|   |   |   +-- after-fix-build.txt
|   |   |   +-- after-fix-probe-console.txt
|   |   |   +-- after-fix-probe.json
|   |   |   +-- after-fix-selftest.txt
|   |   |   +-- audit-summary.json
|   |   |   +-- before-fix-commit.txt
|   |   |   +-- before-fix-status.txt
|   |   |   +-- before-fix.patch
|   |   |   +-- build.txt
|   |   |   +-- chat-nvidia-console.txt
|   |   |   +-- evidence-manifest.json
|   |   |   +-- harness-probe.json
|   |   |   +-- HarnessProbe.cs.txt
|   |   |   +-- interactive-memo.txt
|   |   |   +-- nvidia-catalog-after-fix.json
|   |   |   +-- nvidia-ping-after-fix.json
|   |   |   +-- nvidia-poem-evidence.json
|   |   |   +-- nvidia-poem-run.txt
|   |   |   +-- provider-catalog.json
|   |   |   +-- remote-goal.txt
|   |   |   +-- run-probe.ps1
|   |   |   `-- selftest.txt
|   |   +-- 2026-09-30/
|   |   |   +-- compilacion.txt
|   |   |   +-- inspeccion-journal.txt
|   |   |   +-- nvidia-poema-eventos.jsonl
|   |   |   +-- nvidia-poema-traza.txt
|   |   |   +-- poema.txt
|   |   |   `-- selftest-completo.txt
|   |   +-- 2026-10-01/
|   |   |   +-- build.txt
|   |   |   +-- pos-before.json
|   |   |   +-- README.md
|   |   |   +-- repository-audit.json
|   |   |   +-- selftest-first-attempt.txt
|   |   |   +-- selftest-second-attempt.txt
|   |   |   `-- selftest.txt
|   |   +-- 2026-10-01-seguridad/
|   |   |   +-- build.txt
|   |   |   +-- README.md
|   |   |   +-- repository-audit.json
|   |   |   `-- selftest.txt
|   |   +-- 2026-10-02-proyectos-p0/
|   |   |   +-- build.txt
|   |   |   `-- README.md
|   |   +-- 2026-10-03-core-kernel/
|   |   |   +-- build.txt
|   |   |   +-- exercise-retention.json
|   |   |   +-- README.md
|   |   |   `-- repository-audit.json
|   |   +-- 2026-10-03-proyectos/
|   |   |   +-- backend-check.txt
|   |   |   +-- build-final.txt
|   |   |   +-- build-retry.txt
|   |   |   +-- build.txt
|   |   |   +-- docker-build.txt
|   |   |   +-- fixture-final.txt
|   |   |   +-- fixture-first.txt
|   |   |   +-- nvidia-pos-resume.txt
|   |   |   +-- nvidia-pos.txt
|   |   |   +-- README.md
|   |   |   +-- repository-audit.json
|   |   |   +-- selftest-final.txt
|   |   |   +-- selftest.txt
|   |   |   `-- validation-summary.json
|   |   +-- 2026-10-04-credenciales/
|   |   |   +-- build.txt
|   |   |   +-- exclusion-checks.json
|   |   |   +-- history-audit.json
|   |   |   +-- README.md
|   |   |   +-- repository-audit.json
|   |   |   +-- scanner-checks.json
|   |   |   +-- security-tests.txt
|   |   |   `-- selftest.txt
|   |   +-- 2026-10-04-validacion-core-kernel/
|   |   |   +-- bundle/
|   |   |   |   `-- verify.mjs
|   |   |   +-- code-bundle/
|   |   |   |   `-- verify.mjs
|   |   |   +-- fixture/
|   |   |   |   +-- definition.json
|   |   |   |   `-- README.txt
|   |   |   +-- admission-rejection.txt
|   |   |   +-- admission.txt
|   |   |   +-- build.txt
|   |   |   +-- code-contract.json
|   |   |   +-- code-profile.json
|   |   |   +-- code-recovery.txt
|   |   |   +-- code-remote.txt
|   |   |   +-- code-trace.json
|   |   |   +-- fixture-contract.json
|   |   |   +-- fixture-failure.txt
|   |   |   +-- fixture-trace.json
|   |   |   +-- fixture.txt
|   |   |   +-- invalid-backend-profile.json
|   |   |   +-- invalid-backend.txt
|   |   |   +-- negative-fixture-trace.json
|   |   |   +-- profile.json
|   |   |   +-- projects.txt
|   |   |   +-- README.md
|   |   |   +-- recovery.txt
|   |   |   +-- remote-contract.json
|   |   |   +-- remote-trace.json
|   |   |   +-- remote.txt
|   |   |   +-- repository-audit.json
|   |   |   +-- resume.txt
|   |   |   +-- resumed-trace.json
|   |   |   +-- runs.json
|   |   |   +-- selftest.txt
|   |   |   `-- summary.json
|   |   +-- eventos-antes-2026-09-28.log
|   |   +-- eventos-despues-2026-09-28.log
|   |   +-- eventos-final-2026-09-30.log
|   |   +-- eventos-nvidia-2026-09-28.log
|   |   +-- eventos-regresion-antes-2026-09-28.log
|   |   +-- eventos-regresion-despues-2026-09-28.log
|   |   `-- README.md
|   +-- historico/
|   |   `-- diagnostico-no-cierre-previo-2026-09-30.md
|   +-- incidentes/
|   |   `-- pos-planeacion-sin-ejecucion-2026-10-01.md
|   +-- analisis-harness-poema-2026-09-27.md
|   +-- auditoria-gaps-harness-2026-09-30.md
|   +-- contexto-y-memoria.md
|   +-- contexto-y-recursos.md
|   +-- core-kernel.md
|   +-- diagnostico-no-cierre.md
|   +-- goals-proyectos.md
|   +-- harness-eventos.md
|   +-- implementacion-coordinador-eventos-2026-09-30.md
|   +-- limpieza-datos-repo-2026-09-30.md
|   +-- limpieza-operaciones-2026-09-30.json
|   +-- plan-goals-proyectos-pos-2026-10-02.md
|   +-- README.md
|   +-- repository-data-inventory.json
|   +-- seguridad-datos-harness.md
|   `-- validacion-eventos-2026-09-28.md
+-- Events/
|   +-- AgentEvent.cs
|   +-- EventArchitectureSelfTest.cs
|   `-- EventBus.cs
+-- examples/
|   +-- fixtures/
|   |   `-- minimal/
|   |       +-- definition.json
|   |       `-- README.txt
|   +-- profiles/
|   |   +-- artifacts.example.json
|   |   `-- docker.template.json
|   `-- project-goals/
|       `-- minimal.example.json
+-- Goals/
|   +-- Projects/
|   |   +-- ProjectContractAdmission.cs
|   |   +-- ProjectRecovery.cs
|   |   +-- ProjectRuntime.cs
|   |   +-- ProjectScriptClient.cs
|   |   +-- ProjectSelfTest.cs
|   |   `-- ProjectValidationExecutor.cs
|   +-- AcceptanceCriteria.cs
|   +-- DispatcherPipelineProbe.cs
|   +-- DispatcherStressSelfTest.cs
|   +-- EventCoordinatorSelfTest.cs
|   +-- EventDrivenAgent.cs
|   +-- EventOperationCoordinator.cs
|   +-- EvidenceManifest.cs
|   +-- GoalContext.cs
|   +-- OperationAcceptance.cs
|   +-- OperationAdmission.cs
|   +-- OperationContextBuilder.cs
|   +-- OperationContract.cs
|   +-- OperationTools.cs
|   +-- TruncationRecoverySelfTest.cs
|   `-- WatchdogSelfTest.cs
+-- Harness/
|   +-- Composition/
|   |   +-- CommandHeader.cs
|   |   +-- CompositionSelfTest.cs
|   |   +-- GoalRunFactory.cs
|   |   `-- ProjectServices.cs
|   +-- Concurrency/
|   |   +-- AsyncLock.cs
|   |   +-- ConcurrencySelfTest.cs
|   |   +-- IdempotencyCache.cs
|   |   `-- IdempotencyKey.cs
|   +-- Core/
|   |   +-- Journey.cs
|   |   +-- JourneyDiagnosis.cs
|   |   +-- MachineState.cs
|   |   +-- Run.cs
|   |   +-- RunContext.cs
|   |   `-- RunResult.cs
|   +-- Durable/
|   |   +-- FileRunStore.cs
|   |   +-- IRunStore.cs
|   |   +-- OperationEventJournal.cs
|   |   `-- RunSnapshot.cs
|   +-- Execution/
|   |   +-- ArtifactProjectEnvironment.cs
|   |   +-- DockerProjectEnvironment.cs
|   |   `-- ProjectValidators.cs
|   +-- Functional/
|   |   +-- Composable.cs
|   |   +-- Composition.cs
|   |   +-- Immutable.cs
|   |   +-- Lazy.cs
|   |   +-- Pure.cs
|   |   `-- Result.cs
|   +-- Lifecycle/
|   |   +-- LifecycleAndBusDedupSelfTest.cs
|   |   +-- LifecycleRecorder.cs
|   |   +-- RunLifecycle.cs
|   |   `-- RunLifecycleMutex.cs
|   +-- Observability/
|   |   +-- ConsoleObserver.cs
|   |   +-- IRunObserver.cs
|   |   +-- MetricsCollector.cs
|   |   `-- StructuredLog.cs
|   +-- Patterns/
|   |   +-- CircuitBreakerPattern.cs
|   |   +-- Composition.cs
|   |   +-- IPattern.cs
|   |   +-- RetryPattern.cs
|   |   `-- TimeoutPattern.cs
|   +-- Runner/
|   |   +-- GoalRunner.cs
|   |   +-- IRunner.cs
|   |   `-- RunnerOptions.cs
|   +-- Steps/
|   |   +-- Step.cs
|   |   `-- StepRecorder.cs
|   +-- Testing/
|   |   `-- FakeLLMClient.cs
|   +-- Traceability/
|   |   +-- Correlation.cs
|   |   +-- CorrelationContext.cs
|   |   `-- CorrelationPropagator.cs
|   `-- Scope.cs
+-- Kernel/
|   +-- Concurrency/
|   |   `-- ResetEvents.cs
|   +-- Events/
|   |   `-- AgentEventKind.cs
|   +-- LLM/
|   |   +-- ChatContracts.cs
|   |   +-- ILLMClient.cs
|   |   `-- ThinkingPolicy.cs
|   +-- Projects/
|   |   `-- ProjectPorts.cs
|   +-- AgentLoop.Kernel.csproj
|   `-- README.md
+-- local-secrets/
|   `-- README.md
+-- Looping/
|   +-- AgentLoopRunner.cs
|   `-- LoopContext.cs
+-- Parsing/
|   +-- CompletionParser.cs
|   +-- ParsedCompletion.cs
|   +-- StreamAccumulator.cs
|   `-- ThinkingTrace.cs
+-- Prompting/
|   +-- PromptTemplate.cs
|   +-- PromptVariables.cs
|   +-- SanitizationContract.cs
|   +-- SanitizationSelfTest.cs
|   +-- SystemPrompt.cs
|   `-- TextSanitizer.cs
+-- Properties/
|   `-- launchSettings.json
+-- Provider/
|   +-- Contracts/
|   |   +-- LLMException.cs
|   |   `-- TransportOptions.cs
|   +-- Helpers/
|   |   `-- TransientErrors.cs
|   +-- Nvidia/
|   |   +-- NvidiaClient.cs
|   |   +-- NvidiaModelCatalog.cs
|   |   +-- NvidiaOptions.cs
|   |   +-- NvidiaRequestAdapter.cs
|   |   `-- ProviderContractSelfTest.cs
|   +-- Transport/
|   |   `-- ChatCompletionsTransport.cs
|   `-- README.md
+-- scripts/
|   +-- audit-git-secrets.py
|   `-- audit-repository.ps1
+-- Security/
|   +-- Contracts/
|   |   `-- HarnessDataDtos.cs
|   +-- BoundedReadStream.cs
|   +-- DataGuard.cs
|   +-- DataLimits.cs
|   +-- ProtectedResources.cs
|   +-- SafeStreamingText.cs
|   +-- SecuritySelfTest.cs
|   +-- StrictJson.cs
|   `-- ToolArgumentValidator.cs
+-- StateMachines/
|   +-- AgentContext.cs
|   +-- AgentStateMachine.cs
|   +-- ConversationMemory.cs
|   +-- ConversationThread.cs
|   +-- StateMachine.cs
|   +-- StateTransition.cs
|   `-- TickStateMachine.cs
+-- Terminals/
|   +-- ProcessHandle.cs
|   +-- ProcessSupervisor.cs
|   `-- ShellCommand.cs
+-- Tools/
|   +-- BuiltInTools.cs
|   +-- IdempotentTool.cs
|   +-- IdempotentToolSelfTest.cs
|   +-- ITool.cs
|   `-- ToolRegistry.cs
+-- .gitignore
+-- AgentLoop.csproj
+-- AgentLoop.sln
+-- Program.cs
`-- README.md
```

## Carpetas locales fuera del inventario Git

.git/, .vs/, bin/, obj/, .agentloop/, artifacts/, notes/ y archivos secretos son datos locales. Concurrency/ y ProjectAssets/ existen pero estan vacias. Los bin/obj de Core y Kernel tambien son generados. No se inspeccionan valores de secretos. local-secrets/README.md es la unica excepcion versionada de ese almacen.

## Despues: estructura objetivo representativa

Los nombres nuevos corresponden a separaciones propuestas; no representan archivos que ya existan. Los subarboles con comentarios agrupan varios archivos del inventario.

```text
agent-loop/
+-- AgentLoop.sln
+-- README.md
+-- .gitignore
+-- .github/
+-- src/
|   +-- AgentLoop.Kernel/
|   |   +-- AgentLoop.Kernel.csproj
|   |   +-- Events/               # vocabulario, eventos y puertos
|   |   |   +-- AgentEvent.cs
|   |   |   +-- AgentEventKind.cs
|   |   |   `-- IEventPublisher.cs
|   |   +-- Llm/                  # capacidades neutrales
|   |   |   +-- ILLMClient.cs
|   |   |   +-- ChatRequest.cs
|   |   |   +-- ChatMessage.cs
|   |   |   `-- ReasoningPreferences.cs
|   |   +-- Concurrency/
|   |   |   +-- AsyncManualResetEvent.cs
|   |   |   `-- AsyncAutoResetEvent.cs
|   |   +-- Tools/ITool.cs
|   |   `-- Execution/IProjectExecutionEnvironment.cs
|   +-- AgentLoop.Core/
|   |   +-- AgentLoop.Core.csproj
|   |   +-- Cards/                # reglas y deuda transaccional
|   |   +-- Goals/Goal.cs
|   |   +-- Projects/
|   |   |   +-- Contracts/        # DTOs, uno por responsabilidad
|   |   |   +-- ProjectState.cs
|   |   |   +-- ProjectKnowledge.cs
|   |   |   `-- Validation/       # interfaces del dominio
|   |   +-- Context/              # presupuestos y politicas puras
|   |   `-- Primitives/Result.cs
|   +-- AgentLoop.Harness/
|   |   +-- AgentLoop.Harness.csproj
|   |   +-- Operations/
|   |   |   +-- EventOperationCoordinator.cs
|   |   |   +-- OperationContext.cs
|   |   |   +-- Admission/
|   |   |   `-- Acceptance/
|   |   +-- Projects/             # runtime y validacion por eventos
|   |   +-- Execution/            # runner y contratos de ejecucion
|   |   +-- Events/               # mailbox/bus y presentacion
|   |   +-- Tools/                # registro y admision de invocaciones
|   |   +-- Profiles/             # contrato de perfil y binding
|   |   +-- Context/              # sesiones, poda y briefs
|   |   +-- Prompting/            # plantillas y mensajes de sistema
|   |   +-- Security/             # limites de confianza, DTOs y reglas
|   |   +-- Persistence/Ports/    # journal, snapshots y run store
|   |   +-- Lifecycle/
|   |   +-- Observability/
|   |   +-- Traceability/
|   |   `-- Resilience/
|   +-- AgentLoop.Infrastructure/
|   |   +-- AgentLoop.Infrastructure.csproj
|   |   +-- Providers/
|   |   |   +-- Nvidia/           # adaptacion especifica de NIM
|   |   |   +-- Transport/        # HTTP compartido
|   |   |   +-- Parsing/          # respuesta y SSE
|   |   |   +-- Discovery/        # sondas remotas
|   |   |   `-- Contracts/        # contratos concretos de transporte
|   |   +-- Execution/
|   |   |   +-- Processes/        # supervisor y handles del SO
|   |   |   +-- Docker/           # descriptor y backend Docker
|   |   |   `-- Artifacts/        # backend de archivos
|   |   +-- Files/                # tools y verificaciones de archivos
|   |   +-- Persistence/
|   |   |   +-- FileRunStore.cs
|   |   |   +-- JsonlEventJournal.cs
|   |   |   `-- FileConversationMemory.cs
|   |   `-- Credentials/SecretStore.cs
|   +-- AgentLoop.Cli/
|   |   +-- AgentLoop.Cli.csproj
|   |   +-- Program.cs            # entrada breve
|   |   +-- CliApplication.cs     # dispatch y codigos de salida
|   |   +-- Commands/             # operacion, proyecto, chat, modelos, diagnostico
|   |   +-- Interactive/          # consola y controles del usuario
|   |   +-- Composition/          # registro de adaptadores
|   |   `-- Properties/launchSettings.json
|   `-- AgentLoop.Compatibility/
|       +-- AgentLoop.Compatibility.csproj
|       +-- Conversation/         # maquina conversacional anterior
|       +-- Reactor/LegacyEventDrivenAgent.cs
|       `-- StateMachines/        # base generica anterior
+-- tests/
|   +-- AgentLoop.SelfTests/       # ejecutor de comprobaciones sin NuGet
|   |   +-- Kernel/
|   |   +-- Core/
|   |   +-- Harness/
|   |   +-- Infrastructure/
|   |   +-- Cli/
|   |   `-- Compatibility/
|   +-- Shared/FakeLLMClient.cs
|   `-- Fixtures/ProjectFixtureClient.cs
+-- examples/
|   +-- contracts/minimal.example.json
|   +-- profiles/
|   `-- fixtures/minimal/
+-- docs/
|   +-- README.md
|   +-- architecture/
|   +-- guides/
|   +-- plans/
|   +-- incidents/
|   +-- archive/
|   `-- evidence/                 # capturas historicas; conservar rutas
+-- scripts/
|   +-- audit-git-secrets.py
|   `-- audit-repository.ps1
+-- local-secrets/                 # ignorada, salvo README
+-- .agentloop/                    # estado local ignorado
`-- artifacts/                     # salidas y ejercicios locales ignorados

Proyectos generados: fuera de agent-loop/; ruta elegida por el contrato.
```

## Relacion actual -> destino y naturaleza

Los destinos abreviados Core/, Harness/, Infrastructure/ y Kernel/ corresponden a sus proyectos dentro de src/.

| Origen | Destino propuesto | Proposito y naturaleza |
| --- | --- | --- |
| `Program.cs` | `src/AgentLoop.Cli/Program.cs + Commands/ + Interactive/` | Entrada, dispatch, comandos y consola; extraer tambien las pruebas incrustadas. |
| `Cli/ProjectCommands.cs` | `src/AgentLoop.Cli/Commands/ProjectCommands.cs` | Adaptador CLI para proyectos. |
| `Kernel/` | `src/AgentLoop.Kernel/` | Puertos y primitivas neutrales; sacar serializacion especifica y descriptor Docker. |
| `Core/` | `src/AgentLoop.Core/` | Estado y politicas sin E/S; mantener dependencia solo hacia Kernel. |
| `Cards/` | `src/AgentLoop.Core/Cards/` | Reglas transaccionales; separar renderizado y acceso a evidencia del SO. |
| `Goals/EventOperationCoordinator.cs` | `src/AgentLoop.Harness/Operations/EventOperationCoordinator.cs` | Motor vigente por eventos. |
| `Goals/GoalContext.cs` | `Core/Goals/Goal.cs + Harness/Operations/OperationContext.cs` | Separar objetivo puro del contexto con servicios vivos. |
| `Goals/OperationAdmission.cs, OperationAcceptance.cs` | `src/AgentLoop.Harness/Operations/Admission/ y Acceptance/` | Admision de acciones y aceptacion con recibos; efectos mediante puertos. |
| `Goals/OperationTools.cs` | `src/AgentLoop.Harness/Tools/OperationToolRegistration.cs` | Registra herramientas operativas; separar implementaciones concretas. |
| `Goals/OperationContract.cs, OperationContextBuilder.cs` | `src/AgentLoop.Harness/Context/` | Construccion del prompt y antecedentes; no confundir con el contrato DTO del proyecto. |
| `Goals/Projects/` | `src/AgentLoop.Harness/Projects/` | Runtime por eventos, admision y recuperacion; separar lecturas de archivos en Infrastructure. |
| `Goals/EventDrivenAgent.cs` | `src/AgentLoop.Compatibility/Reactor/LegacyEventDrivenAgent.cs` | Reactor anterior; su nombre actual oculta que no es el ejecutor principal. |
| `Events/` | `src/AgentLoop.Kernel/Events/ + src/AgentLoop.Harness/Events/` | Evento neutral en Kernel; bus y formateo/sanitizacion en Harness. |
| `Harness/Core/` | `src/AgentLoop.Harness/Execution/Contracts/` | Run, Journey, RunResult, RunContext; son contratos/runtime de ejecucion, no el Core independiente. |
| `Harness/Composition/` | `src/AgentLoop.Cli/Composition/ + src/AgentLoop.Harness/Profiles/` | Composicion concreta en host; binding abstracto en Harness. |
| `Harness/Durable/` | `src/AgentLoop.Harness/Persistence/Ports/ + src/AgentLoop.Infrastructure/Persistence/` | Separar interfaces/snapshots de FileRunStore y journal JSONL. |
| `Harness/Execution/` | `src/AgentLoop.Infrastructure/Execution/` | Backends Docker/archivos y validadores que producen efectos. |
| `Harness/Observability/, Lifecycle/, Steps/, Traceability/` | `src/AgentLoop.Harness/` | Instrumentacion del runtime; Steps bajo Observability/Steps. |
| `Harness/Patterns/` | `src/AgentLoop.Harness/Resilience/` | Politicas de reintento, timeout y circuit breaker; comprobar uso antes de promoverlas. |
| `Harness/Functional/` | `Core/Primitives/ + Harness/Composition/Utilities/` | Result y utilidades segun uso real; no crear una carpeta Helpers sin responsabilidad. |
| `Harness/Concurrency/` | `src/AgentLoop.Harness/Concurrency/` | Idempotencia y exclusion del runtime; no mover automaticamente caches al Kernel. |
| `Harness/Testing/, *SelfTest.cs, *Probe.cs` | `tests/` | Comprobaciones y dobles fuera de assemblies de produccion. |
| `Goals/Projects/ProjectScriptClient.cs` | `tests/Fixtures/ProjectFixtureClient.cs` | Cliente determinista de prueba, no cliente LLM de produccion. |
| `Provider/` | `src/AgentLoop.Infrastructure/Providers/` | Nvidia, transporte y contratos concretos; mantener subcarpetas por proveedor. |
| `Parsing/` | `src/AgentLoop.Infrastructure/Providers/Parsing/` | Parser de respuestas y SSE; DTOs neutrales que necesite Harness pasan a Kernel. |
| `Discovery/AgenticModelSelector.cs` | `src/AgentLoop.Infrastructure/Providers/Discovery/ModelCapabilityProbe.cs` | Hace sondas remotas; separar la politica de seleccion de sus llamadas al proveedor. |
| `Configuration/SecretStore.cs` | `src/AgentLoop.Infrastructure/Credentials/SecretStore.cs` | Carga dedicada de credenciales; nunca en Core/Kernel. |
| `Terminals/` | `src/AgentLoop.Infrastructure/Execution/Processes/` | Gestion de procesos del SO; puertos del supervisor permiten desacoplar Harness. |
| `Tools/` | `Kernel/Tools/ + Harness/Tools/ + Infrastructure/Files/` | ITool neutral, registro/admisibilidad y efectos locales tienen distintas naturalezas. |
| `Prompting/` | `src/AgentLoop.Harness/Prompting/` | Plantillas del agente; TextSanitizer puede formar parte de Security/Text. |
| `Security/` | `src/AgentLoop.Harness/Security/ + Infrastructure/Files/` | Reglas/DTOs de confianza frente a comprobaciones concretas de rutas y streams. |
| `StateMachines/ConversationThread.cs` | `src/AgentLoop.Harness/Context/ConversationSession.cs` | Sesion/contexto; no es una maquina de estados por naturaleza. |
| `StateMachines/ConversationMemory.cs` | `src/AgentLoop.Infrastructure/Persistence/FileConversationMemory.cs` | Persistencia JSON de notas; exponer un puerto para la sesion. |
| `StateMachines/AgentStateMachine.cs, StateMachine.cs, StateTransition.cs; Looping/` | `src/AgentLoop.Compatibility/` | Conversacion anterior y soporte de compatibilidad; no eje de operaciones. |
| `StateMachines/TickStateMachine.cs` | `tests/Fixtures/TickStateMachine.cs` | Fixture usada por selftest, no logica del producto. |
| `Properties/` | `src/AgentLoop.Cli/Properties/` | Perfil F5 pertenece al ejecutable. |
| `examples/project-goals/` | `examples/contracts/` | Contratos externos de ejemplo; nombre explicito. |
| `docs/*.md` | `docs/architecture/, guides/, plans/, archive/` | Guias vigentes separadas de auditorias y planes fechados; actualizar enlaces. |
| `docs/evidence/` | `docs/evidence/` | Capturas inmutables; conservar rutas y hashes historicos. |
| `Concurrency/, ProjectAssets/ locales vacias` | `Retirar si siguen vacias al ejecutar la migracion` | Residuos fisicos de movimientos anteriores; no estan versionadas. |

## Nombres que conviene aclarar

| Actual | Propuesto | Motivo |
| --- | --- | --- |
| `ResetEvents.cs` | `AsyncManualResetEvent.cs + AsyncAutoResetEvent.cs` | Contiene dos primitivas publicas. |
| `ChatContracts.cs` | `ChatRequest.cs, ChatMessage.cs y DTOs asociados` | Contratos neutrales agrupados hoy. |
| `ProjectPorts.cs` | `Execution/Ports/, Profiles/Contracts/, Context/Ports/` | Mezcla puertos, politicas y descriptor Docker. |
| `ProjectValidatorPort.cs` | `IProjectValidator.cs + IProjectValidatorFactory.cs + ValidationObservation.cs` | Cada archivo indica su contrato. |
| `ProjectServices.cs` | `ProjectServices.cs + ProjectProfileLoader.cs` | Servicios vinculados y loader tienen responsabilidades distintas. |
| `Composition.cs (Patterns)` | `ResiliencePolicyChain.cs` | Compone decisiones de resiliencia. |
| `Composition.cs (Functional)` | `FunctionComposition.cs` | Compone funciones; evita colision de nombre. |
| `MachineState.cs` | `RunState.cs + RunStatus.cs` | Estado de ejecucion, no maquina generica. |
| `BuiltInTools.cs` | `FileTools.cs + SystemTools.cs + ArithmeticTools.cs` | Catalogo actual mezcla capacidades. |
| `ShellCommand.cs` | `ShellCommand.cs + ExecutionPolicy.cs + PolicyDecision.cs` | Comando, politica y decision son naturalezas diferentes. |
| `ProjectExecutionDtos.cs` | `WorkStateDto.cs, ValidationResultDto.cs, ProjectSnapshotDto.cs, ProjectEventDataDto.cs` | DTOs identificables por archivo. |
| `Provider/Helpers/TransientErrors.cs` | `Providers/Transport/TransientErrorClassifier.cs` | Clasifica errores de transporte. |
| `ProjectScriptClient.cs` | `ProjectFixtureClient.cs` | Hace explicita su naturaleza de prueba. |

## Dependencias objetivo

```text
Kernel         -> sin referencias a otros proyectos
Core           -> Kernel
Harness        -> Core, Kernel
Infrastructure -> Harness, Core, Kernel
Cli            -> Harness, Infrastructure, Core, Kernel
Compatibility  -> dependencias anteriores que necesite el codigo legado
Tests          -> proyectos bajo prueba; produccion no referencia Tests
```

La dependencia Infrastructure -> Harness permite implementar sus puertos. Harness no debe referenciar Infrastructure. El host instala implementaciones. Si el CLI conserva chat anterior, el host puede componer Compatibility; no se convierte en dependencia del motor por eventos.

## No basta mover carpetas

1. CardLedger usa Result del Harness y TextSanitizer; separar primitivas/estado de presentacion antes de llevarlo a Core.
2. AgentEvent contiene formateo y referencias a Security/Prompting; separar DTO neutral de sus renderizadores.
3. GoalContext y GoalRunner dependen del ProcessSupervisor concreto; introducir el puerto del supervisor antes de separar Infrastructure.
4. Run incluye ProjectServices de composicion y contrato admitido con E/S; desacoplar datos de ejecucion de servicios instalados.
5. ProjectPorts incluye DockerExecutionDefinition. El descriptor Docker debe vivir en Infrastructure; el perfil neutral referencia settings validados por el adaptador instalado.
6. ThinkingOptions y ChatContracts incluyen dialecto/atributos de transporte. Separar preferencias neutrales de serializacion del proveedor y comprobar compatibilidad de consumidores.
7. No marcar Functional, Patterns o Traceability como codigo muerto solo por su carpeta: revisar referencias y consumidores antes de retirar tipos.
8. Extraer pruebas de Program.cs y archivos SelfTest. Mantener una ruta explicita para las utilidades selftest; el host no debe depender del assembly de pruebas.

## Orden de migracion propuesto

1. Mover el layout a src/ manteniendo namespaces y tres assemblies existentes; ajustar .sln, ProjectReference, rutas de scripts y F5.
2. Separar Program en comandos y consola, y aislar pruebas/fixtures con su ejecutor.
3. Separar los puertos y dependencias concretas indicados arriba; extraer Harness e Infrastructure como bibliotecas.
4. Ubicar compatibilidad, aplicar renombres y actualizar namespaces en una fase explicita con plan de compatibilidad.
5. Ordenar documentacion y examples; conservar evidencia historica y sus hashes/rutas. Crear un indice vigente que enlace las capturas anteriores.
6. Validar compilacion, pruebas existentes, F5, carga de secretos, perfiles, journal/recuperacion y una ejecucion remota. Estos checks pertenecen a la implementacion futura, no se ejecutaron para preparar esta propuesta.

Los proyectos generados y los ejercicios de negocio permanecen externos. Las tarjetas y los eventos conservan el flujo principal; esta reorganizacion no crea un planificador por turnos ni otro bus.
