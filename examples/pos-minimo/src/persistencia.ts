import { DatabaseSync } from 'node:sqlite';
import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
const path = process.env.POS_DB ?? 'pos.sqlite';
mkdirSync(dirname(path), { recursive: true });
export const db = new DatabaseSync(path);
db.exec(`PRAGMA journal_mode=WAL;
CREATE TABLE IF NOT EXISTS products(id INTEGER PRIMARY KEY, name TEXT NOT NULL, priceCents INTEGER NOT NULL CHECK(priceCents>0), stock INTEGER NOT NULL CHECK(stock>=0));
CREATE TABLE IF NOT EXISTS sales(id INTEGER PRIMARY KEY, requestKey TEXT NOT NULL UNIQUE, requestJson TEXT NOT NULL, folio TEXT NOT NULL UNIQUE, createdAt TEXT NOT NULL, totalCents INTEGER NOT NULL, changeCents INTEGER NOT NULL, items TEXT NOT NULL);`);
export interface ProductDto {id:number; name:string; priceCents:number; stock:number}
export interface SaleItemDto {productId:number; quantity:number; priceCents:number}
export interface SaleDto {id:number; folio:string; createdAt:string; totalCents:number; changeCents:number; items:SaleItemDto[]}
export class PosError extends Error { status:number; constructor(status:number, message:string) {super(message);this.status=status;} }
