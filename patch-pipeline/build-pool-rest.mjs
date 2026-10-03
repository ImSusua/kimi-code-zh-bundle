// 全文件 UI 候选池 - 已翻译 = 剩余待翻译清单（带位置，便于识别 vendor 簇）
import fs from 'node:fs';

const FULL = 'main-original.mjs';
const src = fs.readFileSync(FULL, 'utf8');

// ---- 1. 全文件字符串提取（含位置），复用提取逻辑 ----
const found = new Map(); // raw -> first position
let i = 0; const n = src.length;
while (i < n) {
  const c = src[i];
  if (c === '/' && src[i + 1] === '/') { while (i < n && src[i] !== '\n') i++; continue; }
  if (c === '/' && src[i + 1] === '*') { i += 2; while (i < n && !(src[i] === '*' && src[i + 1] === '/')) i++; i += 2; continue; }
  if (c === '"' || c === "'") {
    const q = c; let j = i + 1;
    while (j < n) { if (src[j] === '\\') { j += 2; continue; } if (src[j] === q || src[j] === '\n') break; j++; }
    if (src[j] === q) {
      const raw = src.slice(i + 1, j);
      if (!found.has(raw)) found.set(raw, i);
      i = j + 1;
    } else i = j;
    continue;
  }
  if (c === '`') {
    let j = i + 1, depth = 0, chunkStart = j;
    while (j < n) {
      const d = src[j];
      if (d === '\\') { j += 2; continue; }
      if (d === '$' && src[j + 1] === '{') {
        const chunk = src.slice(chunkStart, j);
        if (chunk && !found.has(chunk)) found.set(chunk, chunkStart);
        depth = 1; j += 2; chunkStart = -1;
        let k = j;
        while (k < n && depth > 0) {
          if (src[k] === '{') depth++;
          else if (src[k] === '}') { depth--; if (!depth) break; }
          else if (src[k] === '"' || src[k] === "'") { const q2 = src[k]; k++; while (k < n) { if (src[k] === '\\') { k += 2; continue; } if (src[k] === q2) break; k++; } }
          k++;
        }
        j = k + 1; chunkStart = j;
        continue;
      }
      if (d === '`') break;
      j++;
    }
    if (chunkStart >= 0 && chunkStart <= j) { const chunk = src.slice(chunkStart, j); if (chunk && !found.has(chunk)) found.set(chunk, chunkStart); }
    i = j + 1;
    continue;
  }
  i++;
}
console.error('全文件唯一字面量:', found.size);

// ---- 2. UI 启发式 ----
const isUi = s => {
  if (s.length < 2 || s.length > 400) return false;
  if (/[\u4e00-\u9fff]/.test(s)) return false;
  if (!/[A-Za-z]{2}/.test(s)) return false;
  if (/^(https?:|\/|\.\/|[A-Za-z]:\\\\|node:|@)/.test(s)) return false;
  if (/^[a-z0-9_]+(_[a-z0-9_]+)+$/.test(s)) return false;
  if (/^[a-z0-9]+(-[a-z0-9]+)+$/.test(s)) return false;
  if (/^[A-Z_]{4,}$/.test(s)) return false;
  if (/^[a-zA-Z$][\w$]*$/.test(s)) return false;
  if (/[%^~#&|<>=+*]{3,}/.test(s)) return false;
  if (/^(text|application|image|audio|video|multipart|message|font)\//i.test(s)) return false;
  const hasSpace = /\s/.test(s.replace(/\\[nrt]/g, ' '));
  const capWords = /^[A-Z][a-z]+( [A-Za-z(' \[{0-9]+)+/.test(s);
  return hasSpace || capWords;
};

// ---- 3. 身份上下文风险（单遍）----
const occ = new Map();
{
  let p = 0;
  while (p < n) {
    const c = src[p];
    if (c === '"' || c === "'") {
      const q = c; let j = p + 1;
      while (j < n) { if (src[j] === '\\') { j += 2; continue; } if (src[j] === q || src[j] === '\n') break; j++; }
      if (src[j] === q) {
        let b = p - 1; while (b >= 0 && /\s/.test(src[b])) b--;
        const before = b >= 0 ? src[b] : '';
        let isCase = false;
        if (before === 'e' && b >= 3 && src.slice(b - 3, b + 1) === 'case' && !/[\w$]/.test(src[b - 4] ?? '')) isCase = true;
        let a2 = j + 1; while (a2 < n && /\s/.test(src[a2])) a2++;
        const after = a2 < n ? src[a2] : '';
        const risky = isCase || before === '=' || before === '!' || (before === '[' && after === ']') || (after === ':' && (before === ',' || before === '{'));
        if (risky) { const raw = src.slice(p + 1, j); const e = occ.get(raw) || 0; occ.set(raw, e + 1); }
        p = j + 1; continue;
      }
      p = j; continue;
    }
    p++;
  }
}

// ---- 4. vendor 指纹 ----
const VENDOR_FPS = [
  /^-\/\//i, /dtd (html|xhtml)/i,
  /^(text|application|image|audio|video|multipart|message|font)\//i,
  /^(ipaddr:|fastify|Fastify|pino|sonic|SonicBoom|Busboy|busboy|set-cookie-parser|ajv\b|JTD:|find-my-way|schemaController|highlight\.js|hljs |Chalk |chalk)/,
  /^(must (be|match|NOT|have)|must match a schema|Expected a mapping|Expected a sequence|Unresolved (alias|tag)|Anchor cannot|Invalid block scalar|Map keys must be unique|Comments must be separated|Tags and anchors|Tabs are not allowed|Implicit keys|Set items must all|Invalid escape sequence)/,
  /^(Bad Request|Bad Gateway|Not Found$|Internal Server Error|Unauthorized|Forbidden|Service Unavailable|Too Many Requests|404 Not Found|Default Response|Fastify Error|401 Unauthorized|403 Forbidden)/,
  /^(ReadableStream|next\(\) called|This context has no|Context is not finalized|No active router|Absolute URL for :path|Invalid absolute URL|Missing host header|Unsupported scheme|Invalid host header|Client connection prematurely|The user aborted a request|Request body|Body cannot|Body is not valid|Response\.body|The only possible value|You cannot use `send`|Undefined error has occurred|Called reply|Failed to serialize an error|Missing schema|Schema with|Schema is missing|Invalid initialization|Cannot set forceCloseConnections|Unexpected error from async|URL must be a string|Error Handler|Provided method is invalid|%s method is not supported|Body validation schema|'bodyLimit'|'handlerTimeout'|Request timed out after|Fastify has already|Fastify is already|Paths must be|Invalid redaction path|paths must be an array|pino –|minLength should|Unable to reopen|sonic\.fd|the object can't be undefined|A streaming way|this should not happen|the worker|bufferSize must|end\(\)|unable to flush|_flushSync|only one of|unknown level|levels cannot|pre-existing level|levelVal|missing bindings|callback must be a function|Converting circular|1 item|Object can not safely|stream object needs|customLevels|The serializer|unhandled rejection|stream closed|response terminated|stream payload|request errored|Trying to send a NotFound|argument req|argument is required|unsupported trust|invalid IP address|invalid range on address|req argument|trust argument|Cache id is mandatory|enum must|enum items|Core schema meta-schema|property name must be valid|You must provide|The dependencies should|The decorators should|URI malformed|options or stack|Invalid token type|find-my-way supports|const fn =|const params|return params|return this\.handlers|Major version|Host should be|strategy\.|Can't pass an undefined constraint|the FULL_PATH|the OPTIONAL_PARAM|the ESCAPE_REGEXP|the REMOVE_DUPL|The default route|The bad url handler|buildPrettyMeta|querystringParser|Handler should be|Constraints should|Stream cancelled|Cannot add|request aborted|onRequestAborted|the default handler for 404|dispatchFunc|Response payload is not available|To disable this behavior)/,
  /^(Limit |Boundary required|terminated early|Multipart:|Missing Content-Type|Unsupported Content-Type|anonymous function|The options object|reach (parts|files|fields) limit|request file too large|prototype property|the request is not multipart|a request field is not a valid JSON|the file buffer was not found|FormData is not available|starting multipart parsing|Providing options to busboy|save request file|Could not delete file|should be a file|multipart not initialized|missing `file` field|The needle)/,
  /^(Cannot resolve ref|json \+=|moduleCode|export const |export default |module\.exports|use strict|\}\]use |\[object Object\]|PUBLIC \\|instanceof Date|== \\"string\\"|typeof |return |delete |Bearer |\\r\\n|HTTP\/1\.1 |sha256=|\{error: |{type: |{allowedValues|, nullable: |JSON_STR_|addComma_|inlinedCode|Serializer|validatorState|serializerState|restoreFromState|asInteger|asDateTime|asUnsafeString|\\n\s*json|const main|function main)/,
  /^(usage: |Usage: yargs|yargs|Invalid argument|Did you mean|commands: |options: |positionals: |examples: |epilogue:)/,
];
const isVendor = s => VENDOR_FPS.some(re => s.length < 250 && re.test(s));

// ---- 5. 已翻译的 raw 集合 ----
const done = new Set();
for (const pf of ['pool-tui.tsv', 'pool-cli.tsv', 'pool-residual.tsv']) {
  if (!fs.existsSync(pf)) continue;
  for (const l of fs.readFileSync(pf, 'utf8').split('\n').filter(Boolean)) {
    const cols = l.split('\t');
    if (cols.length >= 3) done.add(JSON.parse(cols[2]));
  }
}
console.error('已翻译 raw 数:', done.size);

// ---- 6. 剩余候选 ----
const remaining = [];
for (const [raw, pos] of found) {
  if (done.has(raw)) continue;
  if (!isUi(raw)) continue;
  if (isVendor(raw)) continue;
  if (occ.has(raw)) continue; // 身份上下文出现 → 不动
  remaining.push({ raw, pos });
}
remaining.sort((a, b) => a.pos - b.pos);
console.error('剩余候选:', remaining.length);
fs.writeFileSync('pool-rest.tsv', remaining.map((e, i) => `${i}\t${e.pos}\t${JSON.stringify(e.raw)}`).join('\n'));
