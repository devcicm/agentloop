# POS minimo: fixture de integracion

Requiere Node 24 y TypeScript 5.7.3 en PATH (incluido en la imagen del harness).
`npm ci --ignore-scripts --offline` instala el proyecto sin dependencias;
`npm run build` comprueba TypeScript y `npm start` inicia el servicio.

POS_DB define el archivo SQLite (por defecto pos.sqlite), HOST la interfaz local
y PORT el puerto (3000 por defecto). Usar una base temporal para comprobaciones.
Crear productos por POST /api/products con name, priceCents y stock; operar ventas
en la pagina /. Dinero en centavos enteros, venta atomica, claves idempotentes.

Este fixture demuestra el protocolo minimo; no es un POS para produccion.
No incorpora autenticacion, facturacion fiscal, pagos bancarios ni multiples cajas.
