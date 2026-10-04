import assert from "node:assert/strict";
import fs from "node:fs";
import { add } from "/project/src/calculator.mjs";
assert.equal(add(17,23),40);
assert.equal(add(-8,3),-5);
assert.equal(add(0,0),0);
assert.equal(add(1.25,2.5),3.75);
assert.ok(fs.readFileSync("/project/README.txt","utf8").includes("add"));
console.log("PASS: modulo JavaScript ejecutado; cuatro casos y documento comprobados");
