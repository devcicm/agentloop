Calculator
==========

Modulo JavaScript ESM con operaciones aritmeticas basicas.

Funcion add(a, b)
-----------------
Suma dos numeros y devuelve el resultado numerico.

- Parametros:
  a (number): primer sumando
  b (number): segundo sumando
- Devuelve: number, la suma de a y b.

Ejemplos:
  add(2, 3)   // 5
  add(-1, 1)  // 0
  add(2.5, 0.5) // 3

Uso
---
El modulo vive en src/calculator.mjs y se importa como ESM:

  import { add } from './src/calculator.mjs';

  console.log(add(2, 3)); // 5

Requisitos: Node.js con soporte de modulos ES (archivos .mjs).
