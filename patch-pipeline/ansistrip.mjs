// 去除 ANSI 序列，便于查看 TUI 转写文本
import fs from 'node:fs';
let t = fs.readFileSync(process.argv[2], 'utf8');
t = t
  .replace(/\x1b\[[0-9;?]*[a-zA-Z]/g, '')
  .replace(/\x1b\][^\x07\x1b]*(\x07|\x1b\\)/g, '')
  .replace(/[\x00-\x08\x0b-\x1a\x1c-\x1f]/g, ch =>
    ch === '\x01' ? '\n[MARK] ' : ch === '\x02' ? '' : (ch === '\r' ? '' : ''))
  .replace(/\x1b[=>c]/g, '');
const lines = t.split('\n').map(l => l.replace(/\s+$/, ''));
const out = [];
let prev = '';
for (const l of lines) { if (l !== prev || !l) out.push(l); prev = l; }
console.log(out.join('\n').replace(/\n{3,}/g, '\n\n'));
