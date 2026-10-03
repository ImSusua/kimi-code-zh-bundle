// 汇总待翻译池 v2：单遍扫描全文，记录每个字面量出现的上下文，识别身份比较用法
import fs from 'node:fs';

const FULL = 'build/prefix/node_modules/@moonshot-ai/kimi-code/dist/main.mjs';
const src = fs.readFileSync(FULL, 'utf8');
const n = src.length;

// ---- 单遍扫描：收集所有 '...' / "..." 出现处及其风险标志 ----
// raw -> {total, risky}
const occ = new Map();
function rec(raw, open, close) {
  // 向前找前一个非空白字符
  let p = open - 1;
  while (p >= 0 && (src[p] === ' ' || src[p] === '\t' || src[p] === '\n' || src[p] === '\r')) p--;
  const before = p >= 0 ? src[p] : '';
  // case 判定：再往前看是否是 case 关键字
  let isCase = false;
  if (before === 'e' && p >= 3) {
    const w = src.slice(p - 3, p + 1);
    if (w === 'case' && !/[\w$]/.test(src[p - 4] ?? '')) isCase = true;
  }
  // 向后找后一个非空白字符
  let q = close + 1;
  while (q < n && (src[q] === ' ' || src[q] === '\t' || src[q] === '\n' || src[q] === '\r')) q++;
  const after = q < n ? src[q] : '';
  const risky =
    isCase ||
    before === '=' || before === '!' ||
    (before === '[' && after === ']') ||
    (after === ':' && (before === ',' || before === '{'));
  const e = occ.get(raw) || { total: 0, risky: 0 };
  e.total++;
  if (risky) e.risky++;
  occ.set(raw, e);
}

let i = 0;
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
      const raw = src.slice(i + 1, j);
      // 还原转义后的真实内容（与提取器一致的最小处理）
      let unesc = '';
      for (let k = 0; k < raw.length; k++) {
        if (raw[k] === '\\') {
          const d = raw[k + 1];
          unesc += d === 'n' ? '\n' : d === 't' ? '\t' : d === 'r' ? '\r' : d;
          k++;
        } else unesc += raw[k];
      }
      rec(unesc, i, j);
      i = j + 1;
    } else i = j;
    continue;
  }
  if (c === '`') {
    // 模板串跳过内容即可（分块风险低），但要跳过 ${} 里的字符串——此处简单跳过整个模板
    let j = i + 1, depth = 0;
    while (j < n) {
      if (src[j] === '\\') { j += 2; continue; }
      if (src[j] === '$' && src[j + 1] === '{') { depth++; j += 2; continue; }
      if (src[j] === '}' && depth > 0) { depth--; j++; continue; }
      if (src[j] === '`' && depth === 0) break;
      j++;
    }
    i = j + 1;
    continue;
  }
  i++;
}
console.error('occurrence map done:', occ.size, 'unique');

// ---- 合并池 ----
const tui = JSON.parse(fs.readFileSync('tui-ui.json', 'utf8'));
const appUi = JSON.parse(fs.readFileSync('app-ui.json', 'utf8'));
const pool = new Map();
for (const x of [...appUi, ...tui]) if (!pool.has(x.raw)) pool.set(x.raw, x);

const safeSingleWords = new Set(['OK', 'Yes', 'No', 'Cancel', 'Back', 'Next', 'Done', 'Error', 'Warning', 'Ready', 'Loading', 'Saving', 'Delete', 'Remove', 'Edit', 'Retry', 'Skip', 'Exit', 'Help', 'Unnamed', 'Disabled', 'Enabled', 'Working', 'Idle', 'Unnamed']);
const out = [];
let skippedRisky = 0, skippedWord = 0, skippedOcc = 0;
for (const [raw, e] of pool) {
  const o = occ.get(raw);
  if (e.bt) { out.push({ raw, bt: true, count: o ? o.total : e.count }); continue; } // 模板分块不做身份检查
  if (!o) { skippedOcc++; continue; } // 提取与扫描不一致（罕见）
  if (o.risky > 0) { skippedRisky++; continue; }
  if (!/\s/.test(raw) && !safeSingleWords.has(raw)) { skippedWord++; continue; }
  out.push({ raw, bt: e.bt, count: o.total });
}
console.error('pool:', out.length, '| risky:', skippedRisky, '| bare-word:', skippedWord, '| no-occ:', skippedOcc);
fs.writeFileSync('pool.json', JSON.stringify(out));
fs.writeFileSync('pool.tsv', out.map((e, i) => `${i}\t${e.bt ? 'T' : 'P'}\t${JSON.stringify(e.raw)}`).join('\n'));
console.error('pool.tsv written');
