# Contexto del repositorio

AgentLoop es una consola C# / .NET 9 con proveedor predeterminado NVIDIA NIM.
Consultar README.md y docs/README.md para el estado actual y los informes historicos.

- Las operaciones principales usan src/AgentLoop.Harness/Operations/EventOperationCoordinator.cs, eventos
  y tarjetas. Mantener handlers breves y separar admision, efectos y resultados.
- AgentLoopRunner y StateMachine quedan para compatibilidad y utilidades de chat.
- Componer tools con GoalRunFactory; aplicar Scope y admision antes de efectos.
- No acreditar cierre con memoria ni afirmaciones del modelo.
- Conservar identidad causal y registrar evidencia antes de declarar exito.
- Guardar secretos solo en local-secrets o variables de entorno.
- El endpoint y los controles NVIDIA estan en src/AgentLoop.Infrastructure/Providers/Nvidia/NvidiaOptions.cs.
- Identificar documentos vigentes e historicos. Conservar resultados anteriores
  y journals inciertos; no tratarlos como temporales.

- @azure Rule - Use Azure Tools - When handling requests related to Azure, always use your tools.
- @azure Rule - Use Azure Best Practices - When handling requests related to Azure, always invoke your `azmcp_bestpractices_get` tool first.
- @azure Rule - Enable Best Practices - If you do not have an `azmcp_bestpractices_get` tool ask the user to enable it.
