// 将 zh-batch-*.json 的翻译原位应用到 main.mjs（单遍扫描重建）
import fs from 'node:fs';

const SRC_FILE = 'main-original.mjs';
const OUT_FILE = 'main-patched.mjs';

// 载入映射（按池分组，避免不同池的索引冲突）
function loadBatch(files) {
  const m = new Map();
  for (const f of files) {
    if (!fs.existsSync(f)) continue;
    const j = JSON.parse(fs.readFileSync(f, 'utf8'));
    for (const [k, v] of Object.entries(j)) m.set(k, v);
  }
  return m;
}
const tuiZh = loadBatch(['zh-batch-1.json', 'zh-batch-2.json']);
const cliZh = loadBatch(['zh-batch-cli.json', 'zh-batch-cli2.json']);
console.error('zh entries: tui', tuiZh.size, 'cli', cliZh.size);

const rawToZh = new Map();
function absorb(poolFile, batchMap) {
  if (!fs.existsSync(poolFile)) return;
  const rows2 = fs.readFileSync(poolFile, 'utf8').split('\n').filter(Boolean).map(l => {
    const [i, bt, rawJson] = l.split('\t');
    return { i, raw: JSON.parse(rawJson) };
  });
  let c = 0;
  for (const r of rows2) {
    const zh = batchMap.get(r.i);
    if (zh !== undefined && !rawToZh.has(r.raw)) { rawToZh.set(r.raw, zh); c++; }
  }
  console.error(poolFile, '->', c);
}
absorb('pool-tui.tsv', tuiZh);
absorb('pool-cli.tsv', cliZh);
absorb('pool-residual.tsv', loadBatch(['zh-batch-3.json']));
absorb('pool-rest.tsv', loadBatch(['zh-batch-4.json', 'zh-batch-5.json', 'zh-batch-6.json', 'zh-batch-7.json']));
console.error('raw->zh:', rawToZh.size);

const src = fs.readFileSync(SRC_FILE, 'utf8');
const n = src.length;
// 普通字符串语境下，把译文中未转义的外层引号补上反斜杠（\n 等既有转义保持原样）
function escForPlain(z, q) {
  let r = '';
  for (let k = 0; k < z.length; k++) {
    const ch = z[k];
    if (ch === q && z[k - 1] !== '\\') r += '\\' + ch;
    else r += ch;
  }
  return r;
}
let out = '';
let last = 0;
let replaced = 0;
let i = 0;
function flushUpTo(p) { out += src.slice(last, p); }

while (i < n) {
  const c = src[i];
  if (c === '/' && src[i + 1] === '/') { while (i < n && src[i] !== '\n') i++; continue; }
  if (c === '/' && src[i + 1] === '*') { i += 2; while (i < n && !(src[i] === '*' && src[i + 1] === '/')) i++; i += 2; continue; }
  if (c === '"' || c === "'") {
    const q = c; let j = i + 1;
    while (j < n) {
      if (src[j] === '\\') { j += 2; continue; }
      if (src[j] === q || src[j] === '\n') break;
      j++;
    }
    if (src[j] === q) {
      const inner = src.slice(i + 1, j);
      const zh = rawToZh.get(inner);
      if (zh !== undefined) {
        flushUpTo(i + 1);
        out += escForPlain(zh, q);
        last = j;
        replaced++;
      }
      i = j + 1;
    } else i = j;
    continue;
  }
  if (c === '`') {
    let j = i + 1, chunkStart = j, depth = 0;
    let chunk = '';
    while (j < n) {
      const d = src[j];
      if (d === '\\') { j += 2; continue; }
      if (d === '$' && src[j + 1] === '{') {
        // 结束当前分块
        chunk = src.slice(chunkStart, j);
        const zh = rawToZh.get(chunk);
        if (zh !== undefined) { flushUpTo(chunkStart); out += zh; last = j; replaced++; }
        depth = 1; j += 2; chunkStart = -1;
        while (j < n && depth > 0) {
          if (src[j] === '{') depth++;
          else if (src[j] === '}') { depth--; if (!depth) break; }
          else if (src[j] === '"' || src[j] === "'") {
            const q2 = src[j]; let k = j + 1;
            while (k < n) { if (src[k] === '\\') { k += 2; continue; } if (src[k] === q2 || src[k] === '\n') break; k++; }
            if (src[k] === q2) {
              const inner = src.slice(j + 1, k);
              const zh2 = rawToZh.get(inner);
              if (zh2 !== undefined) { flushUpTo(j + 1); out += escForPlain(zh2, q2); last = k; replaced++; }
              j = k + 1;
            } else j = k;
            continue;
          }
          j++;
        }
        j++; // 跳过 }
        chunkStart = j;
        continue;
      }
      if (d === '`') break;
      j++;
    }
    // 结束分块
    if (chunkStart >= 0 && chunkStart < j) {
      chunk = src.slice(chunkStart, j);
      const zh = rawToZh.get(chunk);
      if (zh !== undefined) { flushUpTo(chunkStart); out += zh; last = j; replaced++; }
    }
    i = j + 1;
    continue;
  }
  i++;
}
flushUpTo(n);
fs.writeFileSync(OUT_FILE, out);
console.error('replaced occurrences:', replaced);
console.error('output bytes:', out.length, '(input', n, ')');
