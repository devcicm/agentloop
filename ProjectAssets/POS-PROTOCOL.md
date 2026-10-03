# POS minimo v1: contrato funcional del host

Node 24, TypeScript sin sintaxis que requiera transformacion (el servicio se
arranca con `node src/servidor.ts`). SQLite mediante `node:sqlite` o dependencia
disponible en el cache offline de la imagen. `npm ci --offline --ignore-scripts`
y `npm run build` deben funcionar; TypeScript 5.7.3 esta instalado en la imagen.

Variables: POS_DB (archivo SQLite), HOST y PORT; PORT=0 solicita puerto libre.
Al estar listo imprimir una linea `POS_READY <puerto>` y atender SIGTERM.

- GET /health -> 200 {ok:true}.
- POST /api/products {name,priceCents,stock} -> 201 producto con id entero.
- GET /api/products -> 200 array de productos.
- POST /api/sales {key,items:[{productId,quantity}],paidCents} -> 201 venta.
  Venta: id, folio, createdAt, totalCents, changeCents, items con precio unitario
  priceCents y quantity. key es idempotente; repetir la misma venta conserva id
  y devuelve 201. Pago insuficiente devuelve 400; stock insuficiente devuelve 409.
- GET /api/sales -> 200 array de ventas conservadas tras reiniciar.
- UI en /: controles data-testid=product (select), quantity, paid (importe en
  pesos), sell y ticket. Ticket muestra changeCents; el escenario de navegador
  vende una unidad de $25, paga $30 y comprueba cambio de 500 centavos.

Rechazar cantidades/importes invalidos, agrupar productos repetidos y descontar
stock junto con la venta dentro de una transaccion. No devolver exito antes del
commit. README explica instalacion, arranque y POS_DB. Este protocolo se entrega
al modelo; el oracle ejecutable se mantiene fuera de su scope.
