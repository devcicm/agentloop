# Presentacion de la consola

La presentacion vive en `src/AgentLoop.Cli/Presentation/ConsoleUi.cs`.
Centraliza paneles, colores, sombreado, margenes, sangrias y campos alineados.
No incorpora decisiones del agente ni modifica los datos persistidos.

## Aspecto

```text
  ----------------------------------------------------------
   AGENTLOOP / Consola de operaciones      [fondo azul oscuro]
  ----------------------------------------------------------
    Modelo       : z-ai/glm-5.3
    Proveedor    : NVIDIA NIM
    Stream       : activo
    Directorio   : ...

  ----------------------------------------------------------
   OPERACION / Objetivo por eventos
  ----------------------------------------------------------
    Objetivo     : Crear y comprobar el archivo solicitado
    evento ... ModeloSolicitado ...                [cian]
    evento ... Tarjeta ...                         [verde]
    evento ... TarjetaRechazada ...                [aviso]

  CUMPLIDO: ...                                    [verde]
    Evidencia: ...

  agentloop [1] >
```

| Elemento | Presentacion |
| --- | --- |
| Encabezados | Fondo azul oscuro y texto blanco; separadores ASCII |
| Modelo y entrada | Cian |
| Tarjetas y exito | Verde |
| Fallos, rechazos y aprobacion | Rojo o amarillo segun el evento |
| Razonamiento y trazas auxiliares | Gris oscuro |
| Metricas, historial y variables | Etiquetas y valores alineados |
| Respuesta en streaming | Sangria al inicio de cada linea; separada del razonamiento |

Los bloques de texto se ajustan al ancho disponible hasta 96 columnas.
Los colores originales de la terminal se restauran al terminar cada escritura.
El sombreado solo se aplica en una terminal; la salida redirigida no recibe controles de color.
El texto pasa por DataGuard; el streaming mantiene SafeStreamingText.
Los paneles y escrituras con color comparten una exclusion de consola.
El ajuste de lineas es visual: los datos y archivos conservan su contenido original.

## Opciones

```powershell
dotnet run --project src/AgentLoop.Cli -- interactive
dotnet run --project src/AgentLoop.Cli -- interactive --no-color
dotnet run --project src/AgentLoop.Cli -- interactive --plain-ui
$env:NO_COLOR = '1'
```

`--no-color` y `NO_COLOR` desactivan color y sombreado.
`--plain-ui` tambien quita paneles y margenes de los bloques del presentador.
Las salidas JSON de admision, inspeccion y resultados de proyectos siguen siendo datos estructurados.
El host mantiene sus comandos y sus controles por eventos.

Compilacion de la implementacion: `dotnet build AgentLoop.sln`, sin errores ni advertencias.
No se ejecutaron pruebas adicionales ni inferencia remota para este cambio de presentacion.
