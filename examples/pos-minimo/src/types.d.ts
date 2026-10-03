// Minimal platform boundary for this dependency-free fixture; domain DTOs remain typed.
declare module 'node:*';
declare const process: {env: Record<string,string|undefined>; on: (event:string, callback:()=>void)=>void; exit:(code:number)=>never};
declare const Buffer: {concat:(chunks:Uint8Array[])=>{toString:()=>string}};
