# Proveedores LLM

Estructura activa desde el 1 de octubre de 2026. Namespace publico compartido
`AgentLoop.Provider`; las carpetas delimitan las responsabilidades reales.

```text
ANTES (6d0e954)
Provider/
+-- Contracts.cs
+-- ITokenRouterClient.cs
+-- TokenRouterException.cs
+-- TokenRouterOptions.cs
+-- TokenRouterClient.cs
+-- Thinking.cs

IMPLEMENTADO
Provider/
+-- Contracts/
|   +-- ChatContracts.cs          mensajes, tools, requests y modelos
|   +-- ILLMClient.cs             puerto usado por harness y discovery
|   +-- LLMException.cs          error tipado, sin credenciales
|   +-- TransportOptions.cs      endpoint, timeout, retry y credencial
+-- Helpers/
|   +-- ThinkingPolicy.cs        preferencias y soporte observado
|   +-- ResponseBudgets.cs       respuesta de recuperacion acotada
|   +-- TransientErrors.cs       clasificacion HTTP compartida
+-- Transport/
|   +-- ChatCompletionsTransport.cs  envio cancelable y retry 1,2,4 segundos
+-- Nvidia/
    +-- NvidiaClient.cs          catalogo, HTTP JSON y framing SSE
    +-- NvidiaOptions.cs         endpoint y defaults
    +-- NvidiaModelCatalog.cs    GLM 5.3 y Kimi K3 configurados
    +-- NvidiaRequestAdapter.cs  dialecto por modelo
    +-- ProviderContractSelfTest.cs  contratos sin red por HTTP simulado
```

## Contrato de integracion

CLI/GoalRunner -> ILLMClient -> NvidiaClient -> NvidiaRequestAdapter
-> ChatCompletionsTransport -> HTTP. Parsing interpreta JSON y SSE para el actor.
Un proveedor futuro implementa ILLMClient en su carpeta y configura su propio
adaptador; las tarjetas no dependen de NVIDIA. No hay carpetas ficticias de
proveedores todavia no implementados.

GLM 5.3: el adaptador envia `reasoning_effort=low` por defecto y
`chat_template_kwargs.clear_thinking=true`; omite el bloque generico `thinking`
y el razonamiento historico, conservando IDs, tools y contenido del protocolo.
Puede recibir `high` o `max` mediante ChatRequest.ReasoningEffort; otros valores
fallan localmente. HTTP y SSE usan exactamente el mismo adaptador. Un rechazo
GLM no provoca un retry que quite estos controles y vuelva al default remoto.

No se promete que budget=256 limite los tokens reales. NVIDIA define esfuerzos
low/high/max, con default max; recomienda clear_thinking=true en chat.
Fuente: [referencia GLM 5.3](https://docs.api.nvidia.com/nim/reference/z-ai-glm-5-3).
La plantilla GLM 5.3 no permite garantizar apagado mediante enable_thinking=false.
Fuente: [plantillas GLM](https://docs.nvidia.com/nemo/megatron-bridge/nightly/models/glm/glm5.html).
Consultadas el 1 de octubre de 2026.

Kimi K3 conserva el dialecto anterior y su fallback de parametros opcionales.
No se le aplican automaticamente controles GLM ni se afirma soporte remoto
verificado en esta entrega. Su presencia en el catalogo local no garantiza acceso.

Secretos: NVIDIA_API_KEY o local-secrets/nvidia-api-key.txt, con fallback legado
documentado en [la guia](../local-secrets/README.md). Nunca versionar la clave.
Los nombres activos TokenRouter fueron sustituidos; sondas historicas congeladas
requieren su commit de origen. El transporte comparte envio/retry; framing SSE,
catalogo y errores del protocolo siguen en NvidiaClient.
