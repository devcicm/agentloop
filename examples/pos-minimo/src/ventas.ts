import {db, PosError, type ProductDto, type SaleDto, type SaleItemDto} from './persistencia.ts';
interface SaleInputDto {key:string; items:{productId:number;quantity:number}[]; paidCents:number}
const decode = (row:any):SaleDto => ({id:row.id,folio:row.folio,createdAt:row.createdAt,totalCents:row.totalCents,changeCents:row.changeCents,items:JSON.parse(row.items)});
export const sales = ():SaleDto[] => db.prepare('SELECT * FROM sales ORDER BY id').all().map(decode);
export function sell(raw:unknown):SaleDto {
  const input = raw as SaleInputDto;
  if (!input || typeof input.key!=='string' || !/^[a-zA-Z0-9_-]{1,128}$/.test(input.key) || !Array.isArray(input.items) || input.items.length<1 || input.items.length>100 || !Number.isSafeInteger(input.paidCents) || input.paidCents<0 || input.paidCents>100000000)
    throw new PosError(400,'Venta invalida');
  const grouped = new Map<number,number>();
  for (const item of input.items) {
    if (!item || !Number.isSafeInteger(item.productId) || item.productId<1 || !Number.isSafeInteger(item.quantity) || item.quantity<1 || item.quantity>1000) throw new PosError(400,'Cantidad invalida');
    grouped.set(item.productId,(grouped.get(item.productId)??0)+item.quantity);
  }
  const requestJson = JSON.stringify({items:[...grouped].sort((a,b)=>a[0]-b[0]),paidCents:input.paidCents});
  db.exec('BEGIN IMMEDIATE');
  try {
    const previous = db.prepare('SELECT * FROM sales WHERE requestKey=?').get(input.key);
    if (previous) {if(previous.requestJson!==requestJson) throw new PosError(409,'Clave reutilizada con otra venta'); db.exec('COMMIT'); return decode(previous);}
    let totalCents = 0; const items:SaleItemDto[]=[];
    for (const [productId,quantity] of grouped) {
      const product:ProductDto = db.prepare('SELECT * FROM products WHERE id=?').get(productId);
      if (!product || product.stock<quantity) throw new PosError(409,'Inventario insuficiente');
      totalCents += product.priceCents*quantity;
      if (!Number.isSafeInteger(totalCents) || totalCents>100000000) throw new PosError(400,'Total fuera de rango');
      items.push({productId,quantity,priceCents:product.priceCents});
    }
    if(input.paidCents<totalCents) throw new PosError(400,'Pago insuficiente');
    for(const item of items) db.prepare('UPDATE products SET stock=stock-? WHERE id=?').run(item.quantity,item.productId);
    const createdAt=new Date().toISOString(); const folio='POS-'+crypto.randomUUID(); const changeCents=input.paidCents-totalCents;
    const result=db.prepare('INSERT INTO sales(requestKey,requestJson,folio,createdAt,totalCents,changeCents,items) VALUES(?,?,?,?,?,?,?)').run(input.key,requestJson,folio,createdAt,totalCents,changeCents,JSON.stringify(items));
    const sale:SaleDto={id:Number(result.lastInsertRowid),folio,createdAt,totalCents,changeCents,items};
    db.exec('COMMIT'); return sale;
  } catch(error) {db.exec('ROLLBACK');throw error;}
}
