// 用 node-pty 驱动 kimi TUI，抓取各场景渲染文本（去除 ANSI 后存盘）
import fs from 'node:fs';
import path from 'node:path';
import url from 'node:url';

const HERE = path.dirname(url.fileURLToPath(import.meta.url));
const PTY = path.join(HERE, 'build/prefix/node_modules/@moonshot-ai/kimi-code/node_modules/node-pty');
const { spawn: ptySpawn } = await import(url.pathToFileURL(path.join(PTY, 'lib/index.js')).href);

const MAIN = path.join(HERE, 'build/prefix/node_modules/@moonshot-ai/kimi-code/dist/main.mjs');
const HOME = path.join(HERE, 'kimi-home'); // 隔离的 KIMI_CODE_HOME
fs.rmSync(HOME, { recursive: true, force: true });
fs.mkdirSync(HOME, { recursive: true });

// 场景脚本：{ waitMs 后 write }
const steps = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const outFile = process.argv[3];

const term = ptySpawn(process.execPath, [MAIN], {
  name: 'xterm-256color',
  cols: 100, rows: 34,
  cwd: HERE,
  env: { ...process.env, KIMI_CODE_HOME: HOME, TERM: 'xterm-256color', NO_COLOR: '' },
});

let transcript = '';
term.onData(d => { transcript += d; });

const sleep = ms => new Promise(r => setTimeout(r, ms));
for (const st of steps) {
  await sleep(st.waitMs);
  if (st.write) term.write(st.write);
  if (st.mark) transcript += `\n\x01MARK:${st.mark}\x02`;
}
await sleep(1500);
term.kill();
await sleep(800);

fs.writeFileSync(outFile, transcript);
console.log('transcript bytes:', transcript.length, '->', outFile);
