// 从 main.mjs 提取字符串字面量与模板静态块（只读扫描）
import fs from 'node:fs';
const src = fs.readFileSync(process.argv[2], 'utf8');
const n = src.length;
let i = 0;
const out = new Map(); // raw -> {count, backtick}
function add(raw, bt) {
  if (!raw) return;
  const e = out.get(raw);
  if (e) e.count++;
  else out.set(raw, { count: 1, bt });
}
while (i < n) {
  const c = src[i];
  if (c === '\n') { i++; continue; }
  if (c === '/' && src[i + 1] === '/') { while (i < n && src[i] !== '\n') i++; continue; }
  if (c === '/' && src[i + 1] === '*') {
    i += 2;
    while (i < n && !(src[i] === '*' && src[i + 1] === '/')) i++;
    i += 2; continue;
  }
  if (c === '"' || c === "'") {
    const q = c; let j = i + 1, buf = '';
    while (j < n) {
      if (src[j] === '\\') { buf += src[j] + (src[j + 1] ?? ''); j += 2; continue; }
      if (src[j] === q) break;
      if (src[j] === '\n') break;
      buf += src[j]; j++;
    }
    if (src[j] === q) { add(buf, false); i = j + 1; } else i = j;
    continue;
  }
  if (c === '`') {
    let j = i + 1, chunk = '';
    while (j < n) {
      if (src[j] === '\\') { chunk += src[j] + (src[j + 1] ?? ''); j += 2; continue; }
      if (src[j] === '`') break;
      if (src[j] === '$' && src[j + 1] === '{') {
        add(chunk, true); chunk = '';
        let depth = 1; j += 2;
        while (j < n && depth > 0) {
          if (src[j] === '{') depth++;
          else if (src[j] === '}') { depth--; if (!depth) break; }
          else if (src[j] === '"' || src[j] === "'" || src[j] === '`') {
            const q2 = src[j]; j++;
            while (j < n) {
              if (src[j] === '\\') { j += 2; continue; }
              if (src[j] === q2) break;
              j++;
            }
          }
          j++;
        }
        j++;
        continue;
      }
      chunk += src[j]; j++;
    }
    if (chunk) add(chunk, true);
    i = j + 1; continue;
  }
  i++;
}
const arr = [...out.entries()].map(([raw, e]) => ({ raw, count: e.count, bt: e.bt }));
fs.writeFileSync(process.argv[3], JSON.stringify(arr));
console.error('total unique literals:', arr.length);
