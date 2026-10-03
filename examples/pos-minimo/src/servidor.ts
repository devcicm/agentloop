import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {products,addProduct} from './catalogo.ts';
import {sales,sell} from './ventas.ts';
import {db,PosError} from './persistencia.ts';
const server=createServer(async(req:any,res:any)=>{
  try {
    if(req.method==='GET' && req.url==='/') {res.setHeader('Content-Type','text/html; charset=utf-8');res.end(await readFile('public/index.html'));return;}
    res.setHeader('Content-Type','application/json');
    let body:unknown;
    if(req.method==='POST') {const chunks:Uint8Array[]=[];let bytes=0;for await(const data of req){bytes+=data.length;if(bytes>32000)throw new PosError(413,'Entrada excesiva');chunks.push(data);}try{body=JSON.parse(Buffer.concat(chunks).toString());}catch{throw new PosError(400,'JSON invalido');}}
    let result:unknown;
    if(req.method==='GET' && req.url==='/health') result={ok:true};
    else if(req.url==='/api/products' && req.method==='GET') result=products();
    else if(req.url==='/api/products' && req.method==='POST') {result=addProduct(body);res.statusCode=201;}
    else if(req.url==='/api/sales' && req.method==='GET') result=sales();
    else if(req.url==='/api/sales' && req.method==='POST') {result=sell(body);res.statusCode=201;}
    else throw new PosError(404,'No encontrado');
    res.end(JSON.stringify(result));
  } catch(error) {res.statusCode=error instanceof PosError?error.status:500;res.end(JSON.stringify({error:error instanceof PosError?error.message:'Error interno'}));}
});
server.listen(Number(process.env.PORT??3000),process.env.HOST??'127.0.0.1',()=>console.log('POS_READY '+server.address().port));
process.on('SIGTERM',()=>server.close(()=>{db.close();process.exit(0);}));
