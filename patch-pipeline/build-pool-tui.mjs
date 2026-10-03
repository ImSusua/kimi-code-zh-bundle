// TUI 区域专属池（18.82M → end），应用层字符串
import fs from 'node:fs';
const FULL = 'build/prefix/node_modules/@moonshot-ai/kimi-code/dist/main.mjs';
const src = fs.readFileSync(FULL, 'utf8');
fs.writeFileSync('tui2-region.mjs', src.slice(18820000));

const VENDOR_FPS = [
  /^-\/\//i, /dtd (html|xhtml)/i,
  /^(text|application|image|audio|video|multipart|message|font)\//i,
  /^(ipaddr:|fastify|Fastify|pino|sonic|SonicBoom|Busboy|busboy|set-cookie-parser|ajv\b|JTD:|find-my-way|schemaController)/,
  /^(must (be|match|NOT|have)|must match a schema|Expected a mapping|Expected a sequence|Unresolved (alias|tag)|Anchor cannot|Invalid block scalar|Map keys must be unique|Comments must be separated|Tags and anchors|Tabs are not allowed|Implicit keys|Set items must all)/,
  /^(Bad Request|Bad Gateway|Not Found$|Internal Server Error|Unauthorized|Forbidden|Service Unavailable|Too Many Requests|404 Not Found|Default Response|Fastify Error)/,
  /^(ReadableStream|next\(\) called|This context has no|Context is not finalized|No active router|Absolute URL for :path|Invalid absolute URL|Missing host header|Unsupported scheme|Invalid host header|Client connection prematurely|The user aborted a request|Request body|Body cannot|Body is not valid|Response.body|The only possible value|You cannot use `send`|Undefined error has occurred|Called reply|Failed to serialize an error|Missing schema|Schema with|Schema is missing|Invalid initialization|Cannot set forceCloseConnections|Unexpected error from async|URL must be a string|Error Handler|Provided method is invalid|%s method is not supported|Body validation schema|'bodyLimit'|'handlerTimeout'|Request timed out after|Fastify has already|Fastify is already|Paths must be|Invalid redaction path|paths must be an array|pino –|minLength should|Unable to reopen|sonic\.fd|the object can't be undefined|A streaming way|this should not happen|the worker|bufferSize must|end\(\)|unable to flush|_flushSync|only one of|unknown level|levels cannot|pre-existing level|levelVal|missing bindings|callback must be a function|Converting circular|1 item|Object can not safely|stream object needs|customLevels|The serializer|unhandled rejection|stream closed|response terminated|stream payload|request errored|Trying to send a NotFound|argument req|argument is required|unsupported trust|invalid IP address|invalid range on address|req argument|trust argument|Cache id is mandatory|enum must|enum items|Core schema meta-schema|property name must be valid|You must provide|The dependencies should|The decorators should|URI malformed|options or stack|Invalid token type|find-my-way supports|const fn =|const params|return params|return this\.handlers|Major version|Host should be|strategy\.|Can't pass an undefined constraint|the FULL_PATH|the OPTIONAL_PARAM|the ESCAPE_REGEXP|the REMOVE_DUPL|The default route|The bad url handler|buildPrettyMeta|querystringParser|Handler should be|Constraints should|Stream cancelled|Cannot add|request aborted|onRequestAborted|the default handler for 404|dispatchFunc|Response payload is not available|To disable this behavior)/,
  /^(Limit |Boundary required|terminated early|Multipart:|Missing Content-Type|Unsupported Content-Type|anonymous function|The options object|reach (parts|files|fields) limit|request file too large|prototype property|the request is not multipart|a request field is not a valid JSON|the file buffer was not found|FormData is not available|starting multipart parsing|Providing options to busboy|save request file|Could not delete file|should be a file|multipart not initialized|missing `file` field|The needle|anonymous function)/,
];
const KW = new Set(['true','false','null','nil','NULL','nullptr','undefined','class','struct','union','enum','interface','trait','final','public','private','protected','static','return','throw','case','switch','catch','for','while','if','else','elif','endif','ifdef','ifndef','include','import','define','undef','warning','error','line','pragma','get','set','args','call','new','do','then','end','fn','let','var','const','in','is','and','or','not','use','strict','asm','extends','implements','infix','infixl','infixr','pre','code','Bool','_Complex','_Imaginary','True','False','function','return']);
const isKeywordList = s => /^[A-Za-z_]+( [A-Za-z_]+)+$/.test(s) && s.split(' ').every(w => KW.has(w));
const isVendor = s => VENDOR_FPS.some(re => re.test(s));

const { execSync } = await import('node:child_process');
execSync('node extract-strings.mjs tui2-region.mjs tui2-strings.json', { stdio: 'inherit' });

const a = JSON.parse(fs.readFileSync('tui2-strings.json', 'utf8'));
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
  const hasSpace = /\s/.test(s.replace(/\\[nrt]/g, ' '));
  const capWords = /^[A-Z][a-z]+( [A-Za-z(' \[{0-9]+)+/.test(s);
  return hasSpace || capWords;
};
const safeSingleWords = new Set(['OK','Yes','No','Cancel','Back','Next','Done','Error','Warning','Ready','Loading','Saving','Delete','Remove','Edit','Retry','Skip','Exit','Help','Unnamed','Disabled','Enabled','Working','Idle']);
const pool = new Map();
for (const x of a) {
  if (!isUi(x.raw) && !safeSingleWords.has(x.raw)) continue;
  if (isVendor(x.raw) || isKeywordList(x.raw)) continue;
  if (!pool.has(x.raw)) pool.set(x.raw, { raw: x.raw, bt: x.bt, count: x.count });
}
const out = [...pool.values()];
console.error('TUI pool:', out.length);
fs.writeFileSync('pool-tui.tsv', out.map((e, i) => `${i}\t${e.bt ? 'T' : 'P'}\t${JSON.stringify(e.raw)}`).join('\n'));
