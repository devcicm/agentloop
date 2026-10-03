import { db, PosError, type ProductDto } from './persistencia.ts';
export const products = ():ProductDto[] => db.prepare('SELECT * FROM products ORDER BY id').all();
export function addProduct(input:unknown):ProductDto {
  const p = input as Partial<ProductDto>;
  if (!p || typeof p.name !== 'string' || !p.name.trim() || p.name.length>120 || !Number.isSafeInteger(p.priceCents) || !Number.isSafeInteger(p.stock) || p.priceCents!<=0 || p.stock!<0 || p.stock!>1000000 || p.priceCents!>100000000)
    throw new PosError(400,'Producto invalido');
  const result = db.prepare('INSERT INTO products(name,priceCents,stock) VALUES(?,?,?)').run(p.name.trim(),p.priceCents,p.stock);
  return db.prepare('SELECT * FROM products WHERE id=?').get(Number(result.lastInsertRowid));
}
