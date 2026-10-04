import fs from "node:fs";
import assert from "node:assert/strict";
const definition=JSON.parse(fs.readFileSync("/project/definition.json","utf8"));
assert.equal(definition.name,"generic-example");
const readme=fs.readFileSync("/project/README.txt","utf8");
assert.ok(readme.includes(definition.name),"README debe incluir el nombre de definition.json");
console.log("PASS: JSON y documento coherentes; validador externo ejecutado");
