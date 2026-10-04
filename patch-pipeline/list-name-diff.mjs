// 列出被汉化的 name 与丢失的原名
import fs from 'node:fs';
function names(file) {
  const s = fs.readFileSync(file, 'utf8');
  const set = new Set();
  const re1 = /name:\s*"((?:[^"\\]|\\.)*)"/g;
  let m;
  while ((m = re1.exec(s))) set.add(m[1]);
  return set;
}
const a = names('main-original.mjs');
const b = names('main-patched.mjs');
const cjk = /[\u4e00-\u9fff]/;
console.log('补丁新增的中文 name:');
for (const x of [...b].filter(x => !a.has(x) && cjk.test(x))) console.log('  ', JSON.stringify(x));
console.log('丢失的原名:');
for (const x of [...a].filter(x => !b.has(x))) console.log('  ', JSON.stringify(x));
